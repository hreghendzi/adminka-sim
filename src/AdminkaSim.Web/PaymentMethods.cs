namespace AdminkaSim.Web;

/// <summary>
/// Demo payment-method catalogue + limits for the cashier UI (m-11 jumpobet
/// replication). Only the Havale (<c>transfer</c>) rail is ACTIVE; the rest
/// render as disabled tiles. Floors are demo-friendly (wallets seed at 0, so
/// jumpobet's literal 2,000/5,000 would block a withdraw demo) and apply to
/// both deposit and withdraw. Single source of truth referenced by the tiles
/// partial and the deposit/withdraw amount validation, so the two never drift.
/// </summary>
public static class PaymentMethods
{
    public const decimal MinAmount = 100m;
    public const decimal MaxAmount = 100_000m;
    public const string ActiveMethodCode = "transfer";

    /// <summary>A cashier method tile.</summary>
    public sealed record Tile(string Name, string Tagline, bool Active, decimal Min, decimal Max);

    public static readonly IReadOnlyList<Tile> All =
    [
        new("AdminkaPay Havale", "Havale / EFT", true,  MinAmount, MaxAmount),
        new("Parolapara",     "Wallet",       false, 250m,     250_000m),
        new("PayPay",         "Fast",         false, 250m,     100_000m),
        new("VevoPay Kripto", "Crypto",       false, 100m,     100_000m),
        new("MEFETE",         "Voucher",      false, 250m,     100_000m),
    ];
}
