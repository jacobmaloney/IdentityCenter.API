namespace DataAccessLibrary.Models;

/// <summary>
/// Represents a team member fetched live from Graph API.
/// </summary>
public class TeamMemberInfo
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string MembershipId { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsOwner => Roles.Contains("owner", StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents a file or folder item in a OneDrive/SharePoint drive.
/// </summary>
public class DriveItemInfo
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public int ChildCount { get; set; }
    public string? MimeType { get; set; }
    public string? WebUrl { get; set; }
}

/// <summary>
/// Represents a sharing link/permission on a drive item.
/// </summary>
public class SharingLinkInfo
{
    public string PermissionId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string? ItemName { get; set; }
    public string? ItemPath { get; set; }
    public string? LinkType { get; set; }
    public string? Scope { get; set; }
    public DateTimeOffset? Expiration { get; set; }
    public string? GrantedTo { get; set; }
}

/// <summary>
/// Editable team settings from Graph API (fun, messaging, member, guest settings).
/// </summary>
public class TeamSettingsInfo
{
    // Fun settings
    public bool AllowGiphy { get; set; }
    public bool AllowStickersAndMemes { get; set; }
    public bool AllowCustomMemes { get; set; }

    // Member settings
    public bool AllowCreateUpdateChannels { get; set; }
    public bool AllowDeleteChannels { get; set; }
    public bool AllowAddRemoveApps { get; set; }
    public bool AllowCreateUpdateRemoveTabs { get; set; }
    public bool AllowCreateUpdateRemoveConnectors { get; set; }

    // Guest settings
    public bool AllowGuestCreateUpdateChannels { get; set; }
    public bool AllowGuestDeleteChannels { get; set; }

    // Messaging settings
    public bool AllowUserEditMessages { get; set; }
    public bool AllowUserDeleteMessages { get; set; }
    public bool AllowOwnerDeleteMessages { get; set; }
    public bool AllowTeamMentions { get; set; }
    public bool AllowChannelMentions { get; set; }
}

/// <summary>
/// Represents a permission entry on a SharePoint site.
/// </summary>
public class SitePermissionInfo
{
    public string Id { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "Member";
    public string? GrantedVia { get; set; }
}

/// <summary>
/// Represents a user assigned to a specific license SKU.
/// </summary>
public class LicenseAssignmentInfo
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? Email { get; set; }
    public bool AccountEnabled { get; set; }
    public string? Department { get; set; }
    public List<string> DisabledPlans { get; set; } = new();
}

/// <summary>
/// Represents a column definition in a SharePoint list.
/// </summary>
public class ListColumnInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string ColumnType { get; set; } = "text";
    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsHidden { get; set; }
    public List<string>? ChoiceValues { get; set; }
}

/// <summary>
/// Represents a single item in a SharePoint list.
/// </summary>
public class ListItemInfo
{
    public string Id { get; set; } = string.Empty;
    public string? ETag { get; set; }
    public DateTimeOffset? CreatedDateTime { get; set; }
    public DateTimeOffset? LastModifiedDateTime { get; set; }
    public Dictionary<string, object?> Fields { get; set; } = new();
}

/// <summary>
/// Paginated result for list items.
/// </summary>
public class ListItemsPage
{
    public List<ListItemInfo> Items { get; set; } = new();
    public string? NextPageToken { get; set; }
    public bool HasNextPage => !string.IsNullOrEmpty(NextPageToken);
}

/// <summary>
/// Represents a member of a directory role fetched live from Graph API.
/// </summary>
public class DirectoryRoleMemberInfo
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? Email { get; set; }
    public string ObjectType { get; set; } = "user";
    public bool AccountEnabled { get; set; } = true;
}

/// <summary>
/// Represents an app role assignment on a service principal.
/// </summary>
public class AppRoleAssignmentInfo
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string? PrincipalDisplayName { get; set; }
    public string? PrincipalType { get; set; }
    public string? AppRoleId { get; set; }
    public string? ResourceDisplayName { get; set; }
    public DateTimeOffset? CreatedDateTime { get; set; }
}

/// <summary>
/// Represents a user search result from Graph API.
/// </summary>
public class GraphUserSearchResult
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? Email { get; set; }
}
