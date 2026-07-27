using AdminkaSim.Web.Data;
using AdminkaSim.Web.Merchant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WalletEntity = AdminkaSim.Web.Data.Wallet;

namespace AdminkaSim.Web.Wallet;

/// <summary>Outcome of processing an inbound adminka callback.</summary>
public enum CallbackOutcome
{
    /// <summary>Applied: ledger entry transitioned (and wallet moved if confirmed).</summary>
    Accepted,
    /// <summary>Hash did not verify — wallet NOT touched (plan §3.1.2a).</summary>
    HashInvalid,
    /// <summary>
    /// No matching pending ledger entry: the lookup is keyed on the callback's
    /// <c>clientId</c> (§13), matched against the ledger's
    /// <c>WalletLedgerEntry.MerchantTxId</c> column.
    /// </summary>
    NotFound,
    /// <summary>Entry was already in a terminal state — idempotent no-op (webhook re-delivery).</summary>
    AlreadyProcessed,
}

/// <summary>
/// The sim's wallet authority (plan D2). adminka-sim owns these balances; the
/// balance moves ONLY here, ONLY when a hash-verified callback reports
/// <c>confirmed</c>. Deposit/withdraw are driven through adminka's merchant API;
/// nothing reads adminka's DB.
/// </summary>
public sealed partial class WalletService(
    SimDbContext db,
    AdminkaMerchantClient merchant,
    IOptions<AdminkaMerchantOptions> options,
    ILogger<WalletService> logger)
{
    private readonly AdminkaMerchantOptions _o = options.Value;

    /// <summary>Deposit banks for the UI (transfer/Havale needs a bankId). Active accounts only.</summary>
    public async Task<IReadOnlyList<MerchantBank>> GetDepositBanksAsync(CancellationToken ct = default)
    {
        var (success, banks, _) = await merchant.GetDepositBanksAsync(ct).ConfigureAwait(false);
        return success ? banks.Where(b => b.HasActiveAccount).ToList() : [];
    }

    /// <summary>Starts a deposit: creates a Pending ledger entry and calls adminka. Returns the start result for the UI to render the pay-to account.</summary>
    public async Task<(MerchantStartResult Result, string MerchantTxId)> StartDepositAsync(
        WalletEntity wallet, string userCode, string name, decimal amount, string? method, int? bankId, CancellationToken ct = default)
    {
        var m = string.IsNullOrWhiteSpace(method) ? _o.DefaultMethod : method!;
        var merchantTxId = Guid.NewGuid().ToString("N");

        var result = await merchant.StartDepositAsync(merchantTxId, amount, m, userCode, name, bankId, ct).ConfigureAwait(false);
        if (result.Success)
        {
            db.WalletLedger.Add(NewPending(wallet.Id, LedgerDirection.Deposit, amount, merchantTxId, result.PublicTxId));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return (result, merchantTxId);
    }

    /// <summary>Starts a withdraw (Havale): balance-guarded, creates a Pending entry, calls adminka.</summary>
    public async Task<(MerchantStartResult Result, string MerchantTxId)> StartWithdrawAsync(
        WalletEntity wallet, string userCode, string name, decimal amount, string? method,
        string account, string? iban, string? bankName, string? bankBranch, CancellationToken ct = default)
    {
        if (amount > wallet.Balance)
        {
            return (new MerchantStartResult(false, null, "Insufficient wallet balance.", null,
                new Dictionary<string, string?>(), ""), "");
        }

        var m = string.IsNullOrWhiteSpace(method) ? _o.DefaultMethod : method!;
        var merchantTxId = Guid.NewGuid().ToString("N");

        var result = await merchant.StartWithdrawAsync(
            merchantTxId, amount, m, userCode, name, account, iban, bankName, bankBranch, ct).ConfigureAwait(false);
        if (result.Success)
        {
            db.WalletLedger.Add(NewPending(wallet.Id, LedgerDirection.Withdraw, amount, merchantTxId, result.PublicTxId));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return (result, merchantTxId);
    }

    /// <summary>
    /// Single writer of the wallet balance. Verifies the callback hash FIRST
    /// (constant-time) and refuses to move money on mismatch. Idempotent: a
    /// re-delivered callback for an already-terminal entry is a no-op.
    /// </summary>
    public async Task<CallbackOutcome> ProcessCallbackAsync(AdminkaCallbackBody body, CancellationToken ct = default)
    {
        // 1) Verify authenticity BEFORE anything touches the ledger (§13, plan §3.1.2a).
        var expected = MerchantHash.Md5Hex(_o.Mid, _o.CallbackUrl, _o.SecretKey);
        if (!MerchantHash.ConstantTimeEquals(expected, body.Hash ?? ""))
        {
            // clientId is the only settlement key on the strict wire (§13) — log it.
            LogHashInvalid(logger, body.Transaction?.ClientId ?? "(none)");
            return CallbackOutcome.HashInvalid;
        }

        var tx = body.Transaction;
        if (tx is null)
        {
            LogNotFound(logger, "(none)");
            return CallbackOutcome.NotFound;
        }

        // 2) Settlement key = clientId, and ONLY clientId (§13 gate G2: the strict
        // FASTPAY v1.1 body dropped merchantTxId; a consumer that keyed on it
        // migrates to clientId, which carries the SAME value).
        //
        // The id mapping, spelled out rather than inferred: the sim generates its
        // own id, sends it to adminka as the `transactionId` REQUEST parameter, and
        // adminka echoes it back as `clientId` on the callback. Only the WIRE SOURCE
        // of the key changed — the local ledger column is still
        // WalletLedgerEntry.MerchantTxId and the database is unchanged (no migration).
        // Team memory 068f6f15: merchant-supplied ids (transactionId / merchantTxId)
        // and server-issued ids (txId / clientId… and adminka's public id, memory
        // c6a9d7e8, which is the server-generated UUID surfaced as transaction.id)
        // are trivially confused on this wire — that exact confusion already shipped
        // a wrong-hash-input bug once.
        var key = tx.ClientId;

        // A body with no clientId is a wire REGRESSION toward the pre-G2 shape, not
        // a routine miss: it falls through to NotFound, which CallbackEndpoint maps
        // to HTTP 404, which drives adminka's §13 retry ladder into the DLQ. That
        // loud, visible failure IS the canary property of this simulator — never
        // soften it with a fallback key.
        if (string.IsNullOrWhiteSpace(key))
        {
            LogNotFound(logger, "(none)");
            return CallbackOutcome.NotFound;
        }

        var entry = await db.WalletLedger
            .Include(l => l.Wallet)
            .FirstOrDefaultAsync(l => l.MerchantTxId == key, ct)
            .ConfigureAwait(false);

        if (entry is null)
        {
            LogNotFound(logger, key);
            return CallbackOutcome.NotFound;
        }

        // 3) Idempotency — webhook re-delivery for a settled entry is a no-op (§13).
        if (entry.Status != LedgerStatus.Pending)
        {
            return CallbackOutcome.AlreadyProcessed;
        }

        entry.AdminkaTxId ??= tx.Id;
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        switch (tx.Status)
        {
            case AdminkaCallbackTransaction.StatusConfirmed:
                entry.Status = LedgerStatus.Confirmed;
                // §3.2 partial-approval (deposit) → ConfirmedAmount. The coalesce also
                // absorbs byte-parity plan §4 A6, which makes `confirmedAmount: null`
                // the normal terminal-unconfirmed value; on Confirmed adminka always
                // sends a value, so the fallback to Amount is a safety net only.
                var effective = tx.ConfirmedAmount ?? tx.Amount;
                entry.Amount = effective;
                entry.Wallet.Balance += entry.Direction == LedgerDirection.Deposit ? effective : -effective;
                break;
            case AdminkaCallbackTransaction.StatusDenied:
                entry.Status = LedgerStatus.Denied;
                break;
            case AdminkaCallbackTransaction.StatusCancelled:
                entry.Status = LedgerStatus.Cancelled;
                break;
            default:
                // status 0 (pending) is never sent on the wire (§13); ignore anything unexpected.
                LogUnexpectedStatus(logger, key, tx.Status);
                return CallbackOutcome.AlreadyProcessed;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogApplied(logger, key, entry.Status, entry.Wallet.Balance);
        return CallbackOutcome.Accepted;
    }

    private static WalletLedgerEntry NewPending(
        Guid walletId, LedgerDirection direction, decimal amount, string merchantTxId, string? adminkaTxId) =>
        new()
        {
            Id = Guid.NewGuid(),
            WalletId = walletId,
            Direction = direction,
            Status = LedgerStatus.Pending,
            Amount = amount,
            Currency = "TRY",
            MerchantTxId = merchantTxId,
            AdminkaTxId = adminkaTxId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "Callback hash invalid for clientId {ClientId}; wallet not touched")]
    private static partial void LogHashInvalid(ILogger logger, string clientId);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Callback for unknown clientId {ClientId}")]
    private static partial void LogNotFound(ILogger logger, string clientId);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Warning, Message = "Callback for {ClientId} had unexpected status {Status}")]
    private static partial void LogUnexpectedStatus(ILogger logger, string clientId, short status);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Information, Message = "Callback applied: {ClientId} -> {Status}; wallet balance now {Balance}")]
    private static partial void LogApplied(ILogger logger, string clientId, LedgerStatus status, decimal balance);
}
