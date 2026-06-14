namespace AdminkaSim.Web.Data;

/// <summary>Deposit grows the wallet; Withdraw shrinks it (on confirmation).</summary>
public enum LedgerDirection
{
    Deposit = 0,
    Withdraw = 1,
}

/// <summary>
/// Lifecycle of a ledger entry, mirroring the FASTPAY transaction state machine
/// the merchant observes. The balance only moves on <see cref="Confirmed"/>.
/// </summary>
public enum LedgerStatus
{
    Pending = 0,
    Confirmed = 1,
    Denied = 2,
    Cancelled = 3,
}

/// <summary>
/// One deposit/withdraw attempt against adminka's merchant API. Created
/// <see cref="LedgerStatus.Pending"/> when the sim starts a transaction (P1),
/// transitioned by adminka's webhook callback. The single writer of the wallet
/// balance is the callback handler (plan §3.1). P0 ships the schema only.
/// </summary>
public sealed class WalletLedgerEntry
{
    public Guid Id { get; set; }

    public Guid WalletId { get; set; }
    public Wallet Wallet { get; set; } = default!;

    public LedgerDirection Direction { get; set; }
    public LedgerStatus Status { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";

    /// <summary>The merchant-side transaction id the sim sends to adminka.</summary>
    public string MerchantTxId { get; set; } = default!;

    /// <summary>adminka's transaction id, once known.</summary>
    public string? AdminkaTxId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
