using System.ComponentModel.DataAnnotations;
using AdminkaSim.Web.Data;
using AdminkaSim.Web.Wallet;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AdminkaSim.Web.Pages;

/// <summary>
/// Start a deposit through adminka's merchant API. On success adminka returns
/// the pool account to "pay"; the entry stays Pending until an operator approves
/// it in the adminka console and the webhook callback credits the wallet.
/// </summary>
public sealed class DepositModel(
    UserManager<SimUser> userManager,
    SimDbContext db,
    WalletService wallet) : PageModel
{
    [BindProperty]
    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [BindProperty]
    public string Method { get; set; } = "transfer";

    public string? ResultMessage { get; private set; }
    public bool ResultSuccess { get; private set; }
    public IReadOnlyDictionary<string, string?>? PayToAccount { get; private set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
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
        var (result, _) = await wallet.StartDepositAsync(w, userCode, user.DisplayName ?? userCode, Amount, Method, ct);

        ResultSuccess = result.Success;
        if (result.Success)
        {
            PayToAccount = result.Account;
            ResultMessage = "Deposit started. Pay to the account below, then an operator approves it in adminka — your balance updates when the callback confirms.";
        }
        else
        {
            ResultMessage = $"Deposit could not be started: {result.Message ?? result.StatusCode ?? "unknown error"}";
        }

        return Page();
    }

    private static string? LocalPart(string? email)
        => string.IsNullOrEmpty(email) ? null : email.Split('@', 2)[0];
}
