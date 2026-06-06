using System.Security.Cryptography;

namespace Common;

/// <summary>
/// Generates strong random passwords for the bootstrap admin account during first-run setup.
/// Used by the QuickConfig wizard, RunSeed page, DatabaseConfig page, and the standalone
/// CreateAdminUser CLI utility. The cleartext is surfaced to the operator once at creation
/// time and is never persisted (only the hash is stored). Operator must change it on first
/// sign-in.
/// </summary>
public static class BootstrapPasswordGenerator
{
    /// <summary>
    /// Returns a 32-character URL-safe base64 password derived from 24 bytes of cryptographic
    /// randomness (~144 bits of entropy). Includes upper/lowercase letters, digits, and the
    /// '-' / '_' separators, which satisfies typical password-complexity policies.
    /// </summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
