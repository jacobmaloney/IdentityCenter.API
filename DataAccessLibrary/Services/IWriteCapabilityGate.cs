namespace DataAccessLibrary.Services;

/// <summary>
/// The discrete write capabilities IdentityCenter exposes. Each maps to a
/// read-only-by-default Settings flag (Category="WriteCapabilities") that must be
/// explicitly enabled before the corresponding directory write is permitted, and
/// (for attribute/lifecycle/membership/license actions) to a per-target delegation
/// action checked against <see cref="IDelegationScopeService"/>.
/// </summary>
public enum WriteCapability
{
    EditAttributes,
    EnableDisable,
    ManageMembership,
    AssignLicense,
    CreateUser,
    CreateGroup,
    InviteGuest,
    CreateEnterpriseApp,

    /// <summary>
    /// Override required to add/remove members on privileged, role-assignable groups
    /// (Domain Admins, Enterprise Admins, Schema Admins, Administrators, Account Operators).
    /// Read-only by default and checked IN ADDITION to <see cref="ManageMembership"/>.
    /// </summary>
    ManagePrivilegedGroups,

    /// <summary>
    /// Apply AWS IAM writes via the Conduit agent (tag/untag user, add/remove group member,
    /// enable/disable access key, remove console access). Read-only by default.
    /// </summary>
    AwsManageWrite,

    /// <summary>
    /// Override required to attach/detach AWS managed policies (the AWS analog of
    /// <see cref="ManagePrivilegedGroups"/>). Read-only by default and checked IN ADDITION
    /// to <see cref="AwsManageWrite"/>.
    /// </summary>
    AwsManagePrivileged
}

/// <summary>
/// Result of a capability check. <see cref="Allowed"/> is the only success state;
/// every denial carries a typed <see cref="WriteDenialReason"/> and a human-readable
/// message the UI renders inline. A denied check MUST NOT mutate the directory.
/// </summary>
public sealed class WriteCapabilityDecision
{
    public bool Allowed { get; init; }
    public WriteDenialReason Reason { get; init; }
    public string Message { get; init; } = "";

    /// <summary>The Settings key that gates this capability, for the "enable in Settings" affordance.</summary>
    public string? SettingKey { get; init; }

    public static WriteCapabilityDecision Allow() =>
        new() { Allowed = true, Reason = WriteDenialReason.None };

    public static WriteCapabilityDecision DenyCapabilityOff(WriteCapability cap, string settingKey) =>
        new()
        {
            Allowed = false,
            Reason = WriteDenialReason.CapabilityDisabled,
            SettingKey = settingKey,
            Message = $"capability {cap} not enabled"
        };

    public static WriteCapabilityDecision DenyScope(WriteCapability cap) =>
        new()
        {
            Allowed = false,
            Reason = WriteDenialReason.ScopeDenied,
            Message = $"scope denied for {cap}"
        };

    public static WriteCapabilityDecision DenyAttributeNotWritable(string attribute) =>
        new()
        {
            Allowed = false,
            Reason = WriteDenialReason.ScopeDenied,
            Message = $"scope denied: attribute '{attribute}' is not writable on this target"
        };
}

public enum WriteDenialReason
{
    None = 0,
    /// <summary>The Settings flag for this capability is OFF (read-only default).</summary>
    CapabilityDisabled,
    /// <summary>The caller's delegation scope does not permit this action on the target.</summary>
    ScopeDenied
}

/// <summary>
/// The single server-side chokepoint for "may this write happen?". Called at the top
/// of every directory-mutating method in ObjectWriteBackService, LicenseWriteService,
/// and EntraIdProvisioningService. Enforces BOTH the read-only-default Settings flag
/// AND the per-target delegation action. Background/system callers (caller.UserId ==
/// "system") bypass the operator-facing flags — those gates protect the interactive UI,
/// not lifecycle/remediation automation which has its own system-only guards.
/// </summary>
public interface IWriteCapabilityGate
{
    /// <summary>
    /// Check a capability that has no per-target delegation action (create/invite-guest/
    /// create-app are global capabilities). Enforces the Settings flag only.
    /// </summary>
    Task<WriteCapabilityDecision> CheckAsync(
        WriteCapability capability,
        bool isSystemCaller,
        CancellationToken ct = default);

    /// <summary>
    /// Check a capability that maps to a per-target delegation action (edit/enable/
    /// membership/license). Enforces the Settings flag AND
    /// <see cref="IDelegationScopeService.CanPerformActionAsync"/> for the target object class.
    /// </summary>
    Task<WriteCapabilityDecision> CheckForTargetAsync(
        WriteCapability capability,
        string objectClass,
        bool isSystemCaller,
        CancellationToken ct = default);

    /// <summary>
    /// Check a capability whose delegation action is dynamic at the call site
    /// (e.g. enable resolves to "Enable", disable to "Disable"). Enforces the Settings
    /// flag for the capability AND the explicit delegation action for the target class.
    /// </summary>
    Task<WriteCapabilityDecision> CheckActionForTargetAsync(
        WriteCapability capability,
        string explicitAction,
        string objectClass,
        bool isSystemCaller,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the writable-attribute intersection for the target: the delegation
    /// writable set (null = all) is the authoritative gate. Used to reject any field
    /// the caller names that the delegation scope does not permit.
    /// </summary>
    Task<HashSet<string>?> GetWritableAttributesAsync(string objectClass, bool isSystemCaller);

    /// <summary>True if the Settings flag for this capability is enabled (UI degradation).</summary>
    Task<bool> IsCapabilityEnabledAsync(WriteCapability capability, CancellationToken ct = default);

    /// <summary>The Settings(Category, Key) pair that gates a capability.</summary>
    (string Category, string Key) SettingFor(WriteCapability capability);
}
