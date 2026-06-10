using DataAccessLibrary.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityCenter.API.Pages.Admin;

/// <summary>
/// Cookie sign-out for the admin UI. POST only (Razor Pages antiforgery validation applies —
/// the token is minted in _AdminHost.cshtml and rendered into the layout's logout form), so a
/// cross-site link can't log the admin out. GET redirects to the login page without side effects.
/// </summary>
[AllowAnonymous]
public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(SignInManager<ApplicationUser> signInManager, ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public IActionResult OnGet() => LocalRedirect("/admin/login");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            _logger.LogInformation("Admin UI: user {Name} logged out.", User.Identity.Name);
            await _signInManager.SignOutAsync();
        }
        return LocalRedirect("/admin/login");
    }
}
