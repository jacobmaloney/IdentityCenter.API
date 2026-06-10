using System.Security.Claims;
using System.Text.Json;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityCenter.API.Pages.Admin;

/// <summary>
/// External IDP sign-in for the API admin UI. Ported from the IdentityCenter WebPortal
/// (Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs): same challenge shape, same
/// external-cookie round trip, same claim-mapping/email fallback chain on the callback,
/// against the SAME database — an IDP configured in the portal works here unchanged.
///
/// Deliberate divergences from the WebPortal copy, all tightenings for the ADMIN surface
/// (this page can only ever mint an admin cookie, so it is stricter than the portal):
///   - NO auto-provisioning. The portal creates a new user (role "User") on first external
///     sign-in; here an external identity must match an EXISTING user by linked login or by
///     email, or it is rejected. Nobody signs into the admin UI who wasn't already a user.
///   - Admin role REQUIRED, exactly like the password path: a valid external identity that
///     maps to a non-admin user gets signed straight back out with the same error message.
///   - IsActive + lockout are enforced on the email-match path too (the portal's
///     ExternalLoginSignInAsync only covers the linked-login path).
///   - No Person matching, no Entra group→role sync. Roles are never granted here — they
///     must already exist in the shared AspNetUserRoles table (the portal manages them).
///   - ReturnUrl is confined to /admin (same sanitizer as the login page).
/// </summary>
[AllowAnonymous]
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAdminRepository _adminRepo;
    private readonly ILogger<ExternalLoginModel> _logger;

    public ExternalLoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAdminRepository adminRepo,
        ILogger<ExternalLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _adminRepo = adminRepo;
        _logger = logger;
    }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IActionResult OnGet() => RedirectToPage("./Login");

    /// <summary>
    /// Issues the OIDC challenge. POST only (antiforgery-validated — the button form on the
    /// login page carries the token), mirroring the WebPortal's ExternalLogin OnPost.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(string provider, string? returnUrl = null)
    {
        returnUrl = SanitizeReturnUrl(returnUrl);

        // Only challenge schemes that actually exist as external sign-in schemes. An arbitrary
        // posted value (e.g. "ApiKey" or an unregistered name) is refused up front.
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        if (string.IsNullOrWhiteSpace(provider) ||
            !schemes.Any(s => string.Equals(s.Name, provider, StringComparison.Ordinal)))
        {
            _logger.LogWarning("Admin UI external login requested for unknown provider: {Provider}", provider);
            ErrorMessage = "Unknown identity provider.";
            return RedirectToPage("./Login");
        }

        _logger.LogInformation("Admin UI external login requested for provider: {Provider}", provider);

        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl = SanitizeReturnUrl(returnUrl);

        if (remoteError != null)
        {
            _logger.LogError("Admin UI: error from external provider: {Error}", remoteError);
            ErrorMessage = "The identity provider reported an error. Please try again or sign in with your local account.";
            return RedirectToPage("./Login");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            _logger.LogError("Admin UI: error loading external login information");
            ErrorMessage = "Error loading external login information.";
            return RedirectToPage("./Login");
        }

        _logger.LogInformation("Admin UI: external login callback received for provider: {Provider}", info.LoginProvider);

        // Fast path: the external identity is already linked (AspNetUserLogins — the same rows
        // the WebPortal writes, so a user who has signed into the portal with this IDP is
        // already linked). ExternalLoginSignInAsync honors lockout state.
        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user == null)
            {
                await SignOutCompletelyAsync();
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login");
            }

            return await CompleteAdminSignInAsync(user, info, returnUrl);
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Admin UI: external login for locked-out account ({Provider})", info.LoginProvider);
            await SignOutExternalAsync();
            ErrorMessage = "Account locked due to multiple failed login attempts. Please try again later.";
            return RedirectToPage("./Login");
        }

        // Not linked yet: match an EXISTING user by email, exactly the WebPortal's claim chain
        // (configured claim mappings first, then the standard email/UPN claims). NO new user is
        // created here — that is the portal's auto-provisioning behavior, deliberately not
        // ported to the admin surface.
        var email = await ResolveEmailClaimAsync(info);
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogError("Admin UI: no email claim found in external login from {Provider}", info.LoginProvider);
            await SignOutExternalAsync();
            ErrorMessage = "Unable to retrieve email from external login provider.";
            return RedirectToPage("./Login");
        }

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser == null)
        {
            // Do not reveal whether the account exists beyond what the message implies.
            _logger.LogWarning("Admin UI: external login from {Provider} matched no existing user", info.LoginProvider);
            await SignOutExternalAsync();
            ErrorMessage = "This account does not have administrator access.";
            return RedirectToPage("./Login");
        }

        // The linked-login fast path enforces lockout via ExternalLoginSignInAsync; this manual
        // match path must enforce it itself before issuing any cookie.
        if (await _userManager.IsLockedOutAsync(existingUser))
        {
            _logger.LogWarning("Admin UI: external login for locked-out account: {Email}", email);
            await SignOutExternalAsync();
            ErrorMessage = "Account locked due to multiple failed login attempts. Please try again later.";
            return RedirectToPage("./Login");
        }

        // Pre-check the gates BEFORE linking or signing in.
        var gate = await CheckAdminGatesAsync(existingUser);
        if (gate != null)
        {
            await SignOutExternalAsync();
            ErrorMessage = gate;
            return RedirectToPage("./Login");
        }

        // Link the external login to the existing user (same as the portal does for existing
        // users), so subsequent sign-ins — here AND in the portal — take the fast path.
        var addLoginResult = await _userManager.AddLoginAsync(existingUser, info);
        if (!addLoginResult.Succeeded)
        {
            _logger.LogError("Admin UI: failed to add external login to existing user: {Errors}",
                string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
            await SignOutExternalAsync();
            ErrorMessage = "Unable to complete external sign-in. Please try again or use your local account.";
            return RedirectToPage("./Login");
        }

        await _signInManager.SignInAsync(existingUser, isPersistent: false);
        return await CompleteAdminSignInAsync(existingUser, info, returnUrl);
    }

    /// <summary>
    /// Final gate shared by both match paths: the user behind the freshly issued cookie must be
    /// active AND in the Admin role, mirroring the password path's post-sign-in check. Anything
    /// else is signed back out with the password path's exact error message.
    /// </summary>
    private async Task<IActionResult> CompleteAdminSignInAsync(ApplicationUser user, ExternalLoginInfo info, string returnUrl)
    {
        var gate = await CheckAdminGatesAsync(user);
        if (gate != null)
        {
            await SignOutCompletelyAsync();
            ErrorMessage = gate;
            return RedirectToPage("./Login");
        }

        _logger.LogInformation("Admin UI: user {Email} logged in via external provider {Provider}.",
            user.Email, info.LoginProvider);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // The external cookie has served its purpose.
        await SignOutExternalAsync();

        return LocalRedirect(returnUrl);
    }

    /// <summary>Returns an error message when the user fails an admin-surface gate, else null.</summary>
    private async Task<string?> CheckAdminGatesAsync(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            _logger.LogWarning("Admin UI: external login for inactive user: {Email}", user.Email);
            return "Your account has been deactivated. Please contact an administrator.";
        }

        if (!await _userManager.IsInRoleAsync(user, "Admin"))
        {
            _logger.LogWarning("Admin UI: external login rejected (not in Admin role): {Email}", user.Email);
            return "This account does not have administrator access.";
        }

        return null;
    }

    /// <summary>
    /// The WebPortal's email-resolution chain: provider-configured claim mappings first, then
    /// the standard email/UPN claim fallbacks.
    /// </summary>
    private async Task<string?> ResolveEmailClaimAsync(ExternalLoginInfo info)
    {
        string? email = null;

        try
        {
            var provider = await _adminRepo.GetIdentityProviderByNameAsync(info.LoginProvider);
            if (provider != null)
            {
                var configDoc = JsonDocument.Parse(provider.Configuration);
                if (configDoc.RootElement.TryGetProperty("ClaimMappings", out var mappingsElement) &&
                    mappingsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mapping in mappingsElement.EnumerateArray())
                    {
                        if (mapping.TryGetProperty("ExternalClaim", out var externalClaim) &&
                            mapping.TryGetProperty("InternalProperty", out var internalProperty) &&
                            string.Equals(internalProperty.GetString(), "Email", StringComparison.OrdinalIgnoreCase))
                        {
                            var claimType = externalClaim.GetString();
                            if (!string.IsNullOrWhiteSpace(claimType))
                            {
                                email = info.Principal.FindFirstValue(claimType);
                                if (!string.IsNullOrEmpty(email)) break;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load claim mappings for provider {Provider}", info.LoginProvider);
        }

        if (string.IsNullOrEmpty(email))
        {
            email = info.Principal.FindFirstValue(ClaimTypes.Email)
                ?? info.Principal.FindFirstValue("email")
                ?? info.Principal.FindFirstValue("preferred_username")
                ?? info.Principal.FindFirstValue("upn")
                ?? info.Principal.FindFirstValue(ClaimTypes.Upn);
        }

        return email;
    }

    private async Task SignOutExternalAsync()
    {
        try
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not clear external sign-in cookie");
        }
    }

    private async Task SignOutCompletelyAsync()
    {
        await _signInManager.SignOutAsync();
        await SignOutExternalAsync();
    }

    /// <summary>Same confinement as the login page: local paths under /admin only.</summary>
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
