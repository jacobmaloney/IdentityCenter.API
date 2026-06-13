namespace DataAccessLibrary.Services;

/// <summary>
/// Service for writing changes back to directory services (Active Directory, Entra ID, etc.)
/// </summary>
public interface IDirectoryWriteService
{
    /// <summary>
    /// Update user attributes
    /// </summary>
    Task<bool> UpdateUserAsync(Guid userId, Dictionary<string, string> attributes);

    /// <summary>
    /// Update group attributes
    /// </summary>
    Task<bool> UpdateGroupAsync(Guid groupId, Dictionary<string, string> attributes);

    /// <summary>
    /// Update computer attributes
    /// </summary>
    Task<bool> UpdateComputerAsync(Guid computerId, Dictionary<string, string> attributes);

    /// <summary>
    /// Update a single attribute on a directory object
    /// </summary>
    Task<bool> UpdateAttributeAsync(Guid objectId, string attributeName, string? newValue);

    /// <summary>
    /// Add user or computer to a group
    /// </summary>
    Task<bool> AddUserToGroupAsync(Guid memberId, Guid groupId);

    /// <summary>
    /// Remove user or computer from a group
    /// </summary>
    Task<bool> RemoveUserFromGroupAsync(Guid memberId, Guid groupId);

    /// <summary>
    /// Reset user password
    /// </summary>
    Task<bool> ResetPasswordAsync(Guid userId, string newPassword, bool mustChangeAtNextLogon = false);

    /// <summary>
    /// Move an AD object to a different OU
    /// </summary>
    Task<bool> MoveObjectAsync(Guid objectId, string targetOU);

    /// <summary>
    /// Delete an AD object permanently
    /// </summary>
    Task<bool> DeleteObjectAsync(Guid objectId);

    /// <summary>
    /// Enable user account
    /// </summary>
    Task<bool> EnableUserAsync(Guid userId);

    /// <summary>
    /// Disable user account
    /// </summary>
    Task<bool> DisableUserAsync(Guid userId);

    /// <summary>
    /// Enable computer account
    /// </summary>
    Task<bool> EnableComputerAsync(Guid computerId);

    /// <summary>
    /// Disable computer account
    /// </summary>
    Task<bool> DisableComputerAsync(Guid computerId);

    /// <summary>
    /// Add member to a group (both from Objects table)
    /// </summary>
    Task<bool> AddGroupMemberAsync(Guid groupId, Guid memberId);

    /// <summary>
    /// Remove member from a group (both from Objects table)
    /// </summary>
    Task<bool> RemoveGroupMemberAsync(Guid groupId, Guid memberId);

    /// <summary>
    /// Set or clear specific UAC flags on a user/computer account
    /// </summary>
    Task<bool> SetUacFlagsAsync(Guid objectId, int flagsToSet, int flagsToClear);

    /// <summary>
    /// Create a new user account in Active Directory
    /// </summary>
    Task<Guid?> CreateUserAsync(
        Guid connectionId,
        string targetOU,
        string samAccountName,
        string userPrincipalName,
        string displayName,
        Dictionary<string, string> attributes,
        string password,
        bool enableAccount = true);

    /// <summary>
    /// Create a new group in the target directory. Returns the created object's source id
    /// (Entra object id / AD objectGUID) and DN, or null on failure.
    /// </summary>
    /// <param name="connectionId">The connection to create the group in.</param>
    /// <param name="displayName">Group display name.</param>
    /// <param name="mailNickname">mailNickname / sAMAccountName for the group.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="securityEnabled">True for a security group.</param>
    /// <param name="mailEnabled">True for a mail-enabled (M365) group.</param>
    /// <param name="targetOU">AD OU DN (ignored by cloud connectors).</param>
    Task<DirectoryCreateResult?> CreateGroupAsync(
        Guid connectionId,
        string displayName,
        string mailNickname,
        string? description,
        bool securityEnabled,
        bool mailEnabled,
        string? targetOU = null);

    /// <summary>
    /// Search Active Directory for an existing user by sAMAccountName or display name.
    /// Returns objectGUID, DN, sAMAccountName, UPN, and display name if found; null if not found.
    /// </summary>
    Task<DirectorySearchResult?> FindUserInDirectoryAsync(
        Guid connectionId,
        string? samAccountName = null,
        string? displayName = null);

    /// <summary>
    /// Search Active Directory for a user using a broad multi-criteria LDAP filter.
    /// Tries multiple sAMAccountName patterns, CN, displayName, givenName+sn, and mail.
    /// </summary>
    Task<DirectorySearchResult?> FindUserBroadSearchAsync(
        Guid connectionId,
        string? firstName,
        string? lastName,
        string? email,
        string? knownUsername = null);
}

/// <summary>
/// Result from creating an object in a directory.
/// </summary>
public class DirectoryCreateResult
{
    /// <summary>Source-system id (Entra object id / AD objectGUID).</summary>
    public Guid SourceObjectGuid { get; set; }
    public string? SourceObjectId { get; set; }
    public string? DN { get; set; }
    public string? DisplayName { get; set; }
}

/// <summary>
/// Result from searching for a user in Active Directory.
/// </summary>
public class DirectorySearchResult
{
    public Guid ObjectGuid { get; set; }
    public string? ObjectSid { get; set; }
    public string DN { get; set; } = "";
    public string? CN { get; set; }
    public string? SamAccountName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public bool IsEnabled { get; set; }
}
