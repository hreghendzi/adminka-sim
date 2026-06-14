using System.ComponentModel.DataAnnotations;
using AdminkaSim.Web.Data;
using AdminkaSim.Web.Wallet;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AdminkaSim.Web.Pages;

/// <summary>
/// Start a withdraw (Havale) through adminka's merchant API. Balance is debited
/// only when the approval callback confirms. Balance-guarded at start.
/// </summary>
public sealed class WithdrawModel(
    UserManager<SimUser> userManager,
    SimDbContext db,
    WalletService wallet) : PageModel
{
    [BindProperty]
    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [BindProperty]
    public string Method { get; set; } = "transfer";

    [BindProperty]
    [Required]
    public string Iban { get; set; } = "";

    [BindProperty]
    public string? BankName { get; set; }

    [BindProperty]
    public string? BankBranch { get; set; }

    public decimal Balance { get; private set; }
    public string? ResultMessage { get; private set; }
    public bool ResultSuccess { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadBalanceAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadBalanceAsync(ct);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var w = await db.Wallets.FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (w is null)
        {
            ResultMessage = "No wallet found for this account.";
            return Page();
        }

        var userCode = LocalPart(user.Email) ?? user.Id;
        var (result, _) = await wallet.StartWithdrawAsync(
            w, userCode, user.DisplayName ?? userCode, Amount, Method,
            account: Iban, iban: Iban, bankName: BankName, bankBranch: BankBranch, ct);

        ResultSuccess = result.Success;
        ResultMessage = result.Success
            ? "Withdraw started. It stays pending until an operator approves it in adminka; your balance is debited when the callback confirms."
            : $"Withdraw could not be started: {result.Message ?? result.StatusCode ?? "unknown error"}";

        return Page();
    }

    private async Task LoadBalanceAsync(CancellationToken ct)
    {
        var userId = userManager.GetUserId(User);
        if (userId is not null)
        {
            var w = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            Balance = w?.Balance ?? 0m;
        }
    }

    private static string? LocalPart(string? email)
        => string.IsNullOrEmpty(email) ? null : email.Split('@', 2)[0];
}
