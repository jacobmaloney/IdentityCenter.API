using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityCenter.API.Pages;

/// <summary>
/// Blazor Server host page for the admin UI (catch-all under /admin except the explicit
/// /admin/login and /admin/logout pages, which are more-specific routes and win).
///
/// The AdminUi policy = authenticated via the Identity APPLICATION COOKIE scheme + Admin role.
/// An unauthenticated browser is challenged by the cookie handler → redirected to /admin/login
/// with ReturnUrl. API keys cannot reach this surface (the policy's scheme list does not
/// include ApiKey), and conversely the cookie principal is only honored on /admin//_blazor
/// paths — see the scheme-selection middleware in Program.cs.
/// </summary>
[Authorize(Policy = "AdminUi")]
public class AdminHostModel : PageModel
{
}
