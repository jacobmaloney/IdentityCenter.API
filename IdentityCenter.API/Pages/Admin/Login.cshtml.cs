using System.ComponentModel.DataAnnotations;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using DataAccessLibrary.Services;
using IdentityCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityCenter.API.Pages.Admin;

/// <summary>
/// Local-credential sign-in for the API admin UI. This is a faithful port of the IdentityCenter
/// WebPortal login (Areas/Identity/Pages/Account/Login.cshtml.cs) running against the SAME
/// database and the SAME ASP.NET Identity binaries (ApplicationUser/ApplicationDbContext from
/// the shared DataAccessLibrary), so portal credentials work here unchanged — including the
/// 5-attempts/30-minute account lockout.
///
/// Deliberate differences from the WebPortal page (all scoped down, nothing loosened):
///   - per-IP attempt throttle (<see cref="LoginAttemptThrottle"/>) — the API's global rate
///     limiter exempts the admin UI surface, so the login POST carries its own gate;
///   - no 2FA flow, no forgot-password, no self-registration — those live in the WebPortal;
///   - external-IDP sign-in (ExternalLogin.cshtml.cs) shows one button per enabled provider in
///     the SHARED IdentityProviders table (the portal's configuration page writes it). Buttons
///     render only for providers whose authentication scheme actually registered at startup —
///     a provider configured in the portal AFTER this service started needs a service restart
///     to appear here (same startup-time registration model as the portal itself).
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LoginModel> _logger;
    private readonly IBrandingService _brandingService;
    private readonly LoginAttemptThrottle _throttle;
    private readonly IAdminRepository _adminRepo;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<LoginModel> logger,
        IBrandingService brandingService,
        LoginAttemptThrottle throttle,
        IAdminRepository adminRepo)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _brandingService = brandingService;
        _throttle = throttle;
        _adminRepo = adminRepo;
    }

    public string ProductName { get; set; } = "IdentityCenter";
    public string? LogoDataUri { get; set; }

    /// <summary>
    /// Enabled external providers from the SHARED IdentityProviders table (written by the
    /// IdentityCenter portal's configuration page), filtered to those whose authentication
    /// scheme actually registered at startup. Empty (today's default) renders the page exactly
    /// as before the external sign-in support existed.
    /// </summary>
    public List<IdentityProvider> ExternalProviders { get; set; } = new();

    /// <summary>Set by ExternalLogin.cshtml.cs when a callback fails; surfaced as a ModelState error.</summary>
    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = SanitizeReturnUrl(returnUrl);

        // Already signed in as an admin → straight to the dashboard.
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Admin"))
            return LocalRedirect(ReturnUrl);

        if (!string.IsNullOrEmpty(ErrorMessage))
            ModelState.AddModelError(string.Empty, ErrorMessage);

        await LoadBrandingAsync();
        await LoadExternalProvidersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = SanitizeReturnUrl(returnUrl);
        await LoadBrandingAsync();
        await LoadExternalProvidersAsync();

        if (!ModelState.IsValid)
            return Page();

        // Per-IP throttle BEFORE touching the user store: blunts password spraying and keeps
        // a single source from hammering SQL. Account lockout (below) covers per-account.
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_throttle.TryAcquire(sourceIp))
        {
            _logger.LogWarning("Login throttled for IP {IpAddress}", sourceIp);
            ModelState.AddModelError(string.Empty, "Too many sign-in attempts. Please wait a minute and try again.");
            return Page();
        }

        try
        {
            // Same sequence as the WebPortal login: existence check, IsActive check, then
            // PasswordSignInAsync with lockoutOnFailure — identical credential behavior.
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                _logger.LogWarning("Admin UI login attempt for non-existent user: {Email}", Input.Email);
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return Page();
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Admin UI login attempt for inactive user: {Email}", Input.Email);
                ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact an administrator.");
                return Page();
            }

            var result = await _signInManager.PasswordSignInAsync(
                Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // The admin UI is for Admins only. Valid non-admin credentials do NOT get a
                // session here — sign the cookie back out and say so.
                if (!await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    await _signInManager.SignOutAsync();
                    _logger.LogWarning("Admin UI login rejected (not in Admin role): {Email}", Input.Email);
                    ModelState.AddModelError(string.Empty, "This account does not have administrator access.");
                    return Page();
                }

                _logger.LogInformation("Admin UI: user {Email} logged in successfully.", Input.Email);

                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);

                return LocalRedirect(ReturnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("Admin UI: account locked out: {Email}", Input.Email);
                ModelState.AddModelError(string.Empty, "Account locked due to multiple failed login attempts. Please try again later.");
                return Page();
            }

            if (result.RequiresTwoFactor)
            {
                // No 2FA UI on the API admin surface — direct the user to the portal it exists in.
                ModelState.AddModelError(string.Empty, "This account requires two-factor authentication. Please sign in through the IdentityCenter portal.");
                return Page();
            }

            _logger.LogWarning("Admin UI: invalid password for user: {Email}", Input.Email);
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during admin UI login attempt for user {Email}", Input?.Email);
            ModelState.AddModelError(string.Empty, "An unexpected error occurred during sign-in. Please try again later.");
            return Page();
        }
    }

    /// <summary>
    /// Same load as the WebPortal login page (enabled, non-Local providers via Dapper), with
    /// one extra guard: intersect with the schemes that actually registered at startup so a
    /// provider row added after boot never renders a button that would fail to challenge.
    /// Any failure here degrades to local-password-only — the page must always render.
    /// </summary>
    private async Task LoadExternalProvidersAsync()
    {
        try
        {
            var registeredSchemes = (await _signInManager.GetExternalAuthenticationSchemesAsync())
                .Select(s => s.Name)
                .ToHashSet(StringComparer.Ordinal);

            if (registeredSchemes.Count == 0)
                return; // nothing registered → keep the empty default, no DB query needed

            var providers = await _adminRepo.GetEnabledIdentityProvidersAsync();
            ExternalProviders = providers
                .Where(p => p.Type != "Local" && registeredSchemes.Contains(p.Name))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load external identity providers for admin login; showing local login only");
            ExternalProviders = new List<IdentityProvider>();
        }
    }

    private async Task LoadBrandingAsync()
    {
        try
        {
            var branding = await _brandingService.GetBrandingAsync();
            ProductName = branding.ProductName;
            LogoDataUri = branding.GetLogoDataUri();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load branding settings for admin login, using defaults");
        }
    }

    /// <summary>
    /// Post-login destinations are confined to the admin UI: local paths under /admin only.
    /// Anything else (absolute URLs, API paths, nulls) lands on the dashboard.
    /// </summary>
    private string SanitizeReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl)
            && Url.IsLocalUrl(returnUrl)
            && returnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
        {
            return returnUrl;
        }
        return "/admin";
    }
}
