namespace DataAccessLibrary.Models;

/// <summary>
/// Microsoft Graph delegated/application permission scopes that grant broad,
/// tenant-impacting access. An enterprise app holding any one of these via an
/// OAuth2 permission grant is flagged as "high permission" on the License Center
/// overview. List is intentionally short — 10 scopes that an SOC would alert on.
/// </summary>
public static class HighPrivilegeScopes
{
    public static readonly IReadOnlySet<string> Scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Mail.ReadWrite",
        "Mail.Send",
        "Files.ReadWrite.All",
        "Sites.ReadWrite.All",
        "Directory.ReadWrite.All",
        "User.ReadWrite.All",
        "Application.ReadWrite.All",
        "RoleManagement.ReadWrite.Directory",
        "Group.ReadWrite.All",
        "AccessReview.ReadWrite.All"
    };

    /// <summary>
    /// Returns true if any space-delimited token in <paramref name="scopeString"/>
    /// matches a high-privilege scope. Graph stores granted scopes as a single
    /// space-separated string in oAuth2PermissionGrant.scope.
    /// </summary>
    public static bool ContainsHighPrivilege(string? scopeString)
    {
        if (string.IsNullOrWhiteSpace(scopeString)) return false;
        var tokens = scopeString.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            if (Scopes.Contains(t)) return true;
        }
        return false;
    }
}
