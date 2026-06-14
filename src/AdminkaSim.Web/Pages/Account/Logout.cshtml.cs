using AdminkaSim.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AdminkaSim.Web.Pages.Account;

/// <summary>Signs the player out (POST only) and returns to the login page.</summary>
public sealed class LogoutModel(SignInManager<SimUser> signInManager) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
