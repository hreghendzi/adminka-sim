using AdminkaSim.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AdminkaSim.Web.Pages;

/// <summary>
/// Transactions History cashier tab (m-11). Filters the player's wallet ledger
/// by period (From/To) and type (Deposits | Withdrawals | All), mirroring the
/// reference merchant. Read-only — straight LINQ over <see cref="WalletLedgerEntry"/>.
/// </summary>
public sealed class HistoryModel(UserManager<SimUser> userManager, SimDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    /// <summary>"all" | "deposit" | "withdraw".</summary>
    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "all";

    public IReadOnlyList<WalletLedgerEntry> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Default window: last 24 hours (matches the reference default).
        var today = DateTime.UtcNow.Date;
        From ??= today.AddDays(-1);
        To ??= today;

        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return;
        }

        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            return;
        }

        // Inclusive end-of-day for the To bound.
        var fromUtc = new DateTimeOffset(From.Value.Date, TimeSpan.Zero);
        var toUtc = new DateTimeOffset(To.Value.Date.AddDays(1), TimeSpan.Zero);

        var q = db.WalletLedger.AsNoTracking()
            .Where(l => l.WalletId == wallet.Id && l.CreatedAt >= fromUtc && l.CreatedAt < toUtc);

        if (string.Equals(Type, "deposit", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(l => l.Direction == LedgerDirection.Deposit);
        }
        else if (string.Equals(Type, "withdraw", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(l => l.Direction == LedgerDirection.Withdraw);
        }

        Rows = await q.OrderByDescending(l => l.CreatedAt).Take(200).ToListAsync(ct);
    }
}
