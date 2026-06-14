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
    /// <summary>No matching pending ledger entry for the merchantTxId.</summary>
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
        // 1) Verify authenticity BEFORE anything touches the ledger (plan §3.1.2a).
        var expected = MerchantHash.Md5Hex(_o.Mid, _o.CallbackUrl, _o.SecretKey);
        if (!MerchantHash.ConstantTimeEquals(expected, body.Hash ?? ""))
        {
            LogHashInvalid(logger, body.Transaction?.MerchantTxId ?? "(none)");
            return CallbackOutcome.HashInvalid;
        }

        var tx = body.Transaction;
        var entry = await db.WalletLedger
            .Include(l => l.Wallet)
            .FirstOrDefaultAsync(l => l.MerchantTxId == tx.MerchantTxId, ct)
            .ConfigureAwait(false);

        if (entry is null)
        {
            LogNotFound(logger, tx.MerchantTxId);
            return CallbackOutcome.NotFound;
        }

        // 2) Idempotency — webhook re-delivery for a settled entry is a no-op.
        if (entry.Status != LedgerStatus.Pending)
        {
            return CallbackOutcome.AlreadyProcessed;
        }

        entry.AdminkaTxId ??= tx.Id.ToString();
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        switch (tx.Status)
        {
            case AdminkaCallbackTransaction.StatusConfirmed:
                entry.Status = LedgerStatus.Confirmed;
                var effective = tx.ConfirmedAmount ?? tx.Amount;   // §3.2 partial-approval (deposit) → ConfirmedAmount
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
                LogUnexpectedStatus(logger, tx.MerchantTxId, tx.Status);
                return CallbackOutcome.AlreadyProcessed;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogApplied(logger, tx.MerchantTxId, entry.Status, entry.Wallet.Balance);
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

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "Callback hash invalid for merchantTxId {MerchantTxId}; wallet not touched")]
    private static partial void LogHashInvalid(ILogger logger, string merchantTxId);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Callback for unknown merchantTxId {MerchantTxId}")]
    private static partial void LogNotFound(ILogger logger, string merchantTxId);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Warning, Message = "Callback for {MerchantTxId} had unexpected status {Status}")]
    private static partial void LogUnexpectedStatus(ILogger logger, string merchantTxId, short status);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Information, Message = "Callback applied: {MerchantTxId} -> {Status}; wallet balance now {Balance}")]
    private static partial void LogApplied(ILogger logger, string merchantTxId, LedgerStatus status, decimal balance);
}
