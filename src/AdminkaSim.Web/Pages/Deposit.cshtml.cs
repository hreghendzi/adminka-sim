using System.ComponentModel.DataAnnotations;
using AdminkaSim.Web.Data;
using AdminkaSim.Web.Merchant;
using AdminkaSim.Web.Wallet;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AdminkaSim.Web.Pages;

/// <summary>
/// Start a deposit through adminka's merchant API. Havale (<c>transfer</c>)
/// requires a <c>bankId</c> — the bank list is fetched from adminka's
/// <c>action=banks</c> and shown as a dropdown. On success adminka returns the
/// pool account to "pay"; the entry stays Pending until an operator approves it
/// and the webhook callback credits the wallet.
/// </summary>
public sealed class DepositModel(
    UserManager<SimUser> userManager,
    SimDbContext db,
    WalletService wallet) : PageModel
{
    [BindProperty]
    [Range(100, 100_000, ErrorMessage = "Amount must be between 100 and 100,000 TRY.")]
    public decimal Amount { get; set; } = 300;

    [BindProperty]
    public string Method { get; set; } = "transfer";

    [BindProperty]
    public int? BankId { get; set; }

    public IReadOnlyList<MerchantBank> Banks { get; private set; } = [];
    public string? ResultMessage { get; private set; }
    public bool ResultSuccess { get; private set; }
    public IReadOnlyDictionary<string, string?>? PayToAccount { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => Banks = await wallet.GetDepositBanksAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Banks = await wallet.GetDepositBanksAsync(ct);

        // Havale requires a bank.
        if (string.Equals(Method, "transfer", StringComparison.OrdinalIgnoreCase) && BankId is null)
        {
            ModelState.AddModelError(nameof(BankId), "Select a bank for a Havale (transfer) deposit.");
        }

        // Amount is whole TRY (FASTPAY/adminka type amount as int — integration-check #2).
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
        var (result, _) = await wallet.StartDepositAsync(w, userCode, user.DisplayName ?? userCode, Amount, Method, BankId, ct);

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
