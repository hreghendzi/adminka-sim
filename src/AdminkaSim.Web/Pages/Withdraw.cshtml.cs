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
    [Range(100, 100_000, ErrorMessage = "Amount must be between 100 and 100,000 TRY.")]
    public decimal Amount { get; set; }

    [BindProperty]
    public string Method { get; set; } = "transfer";

    [BindProperty]
    [Required(ErrorMessage = "Select your bank.")]
    public string? BankName { get; set; }

    [BindProperty]
    [Required]
    public string Iban { get; set; } = "";

    public decimal Balance { get; private set; }
    public string? ResultMessage { get; private set; }
    public bool ResultSuccess { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadBalanceAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadBalanceAsync(ct);

        // Amount is whole TRY (adminka types amount as int — integration-check #2).
        if (Amount != decimal.Truncate(Amount))
        {
            ModelState.AddModelError(nameof(Amount), "Amount must be a whole number of TRY.");
        }

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
        // jumpobet collects only bank + IBAN (no branch). adminka still requires a
        // bankBranch for a transfer withdraw, so we send a placeholder "-" rather
        // than change the contract (owner decision §6.1 — no adminka change).
        var (result, _) = await wallet.StartWithdrawAsync(
            w, userCode, user.DisplayName ?? userCode, Amount, Method,
            account: Iban, iban: Iban, bankName: BankName, bankBranch: "-", ct);

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
