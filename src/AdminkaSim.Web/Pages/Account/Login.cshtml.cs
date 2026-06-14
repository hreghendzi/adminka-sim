using System.ComponentModel.DataAnnotations;
using AdminkaSim.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AdminkaSim.Web.Pages.Account;

/// <summary>
/// Minimal cookie login against ASP.NET Core Identity. No registration, no
/// password reset, no 2FA — just sign in the 3 seeded demo players.
/// </summary>
public sealed class LoginModel(SignInManager<SimUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            return LocalRedirect(returnUrl ?? "/");
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return Page();
    }
}
