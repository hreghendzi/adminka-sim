namespace AdminkaSim.Web.Data;

/// <summary>
/// A player's wallet. adminka-sim is the sole authority for this balance
/// (plan D2): the balance moves ONLY when adminka's webhook callback confirms
/// a transaction (see <see cref="WalletLedgerEntry"/>). The deposit/withdraw
/// LOGIC that mutates this lands in P1 — for P0 this is just the schema.
/// </summary>
public sealed class Wallet
{
    public Guid Id { get; set; }

    /// <summary>FK to <see cref="SimUser"/>.Id. One wallet per user.</summary>
    public string UserId { get; set; } = default!;

    public string Currency { get; set; } = "TRY";

    /// <summary>Available balance. Mutated only on a confirmed callback (P1).</summary>
    public decimal Balance { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<WalletLedgerEntry> Ledger { get; set; } = new List<WalletLedgerEntry>();
}
