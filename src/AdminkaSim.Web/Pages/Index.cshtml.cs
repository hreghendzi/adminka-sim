using AdminkaSim.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AdminkaSim.Web.Pages;

/// <summary>
/// The player's own wallet. P0 shows the balance + (empty) recent ledger;
/// the deposit/withdraw actions that drive adminka's merchant API land in P1.
/// </summary>
public sealed class IndexModel(UserManager<SimUser> userManager, SimDbContext db) : PageModel
{
    public string DisplayName { get; private set; } = "";
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public IReadOnlyList<WalletLedgerEntry> RecentLedger { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return;
        }

        var user = await userManager.GetUserAsync(User);
        DisplayName = user?.DisplayName ?? user?.Email ?? "Player";

        var wallet = await db.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet is null)
        {
            return;
        }

        Balance = wallet.Balance;
        Currency = wallet.Currency;
        RecentLedger = await db.WalletLedger
            .AsNoTracking()
            .Where(l => l.WalletId == wallet.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(10)
            .ToListAsync();
    }
}
