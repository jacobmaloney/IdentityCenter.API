using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;

namespace Common.Encryption
{
    /// <summary>
    /// Single source of truth for protecting/unprotecting the IdentityCenter database
    /// connection string at rest in appsettings.json.
    ///
    /// CRITICAL: The DataProtection parameters here (application name, key-ring directory,
    /// and protector purpose) MUST stay byte-for-byte identical to the DI-registered
    /// <see cref="EncryptionService"/> and the keyring configured in WebPortal/Program.cs.
    /// If any of them drift, a value encrypted by one path becomes undecryptable by the
    /// other — and because this protects the DB connection string, that bricks startup.
    ///
    ///   - Application name : "IdentityCenter"                  (Program.cs SetApplicationName)
    ///   - Key directory    : C:\ProgramData\IdentityCenter\Keys (Program.cs PersistKeysToFileSystem)
    ///   - Protector purpose: "IdentityCenter.Encryption"        (EncryptionService ctor)
    ///
    /// This helper exists so the bootstrap decrypt in Program.cs (which runs BEFORE the DI
    /// container that owns EncryptionService is built) can construct a standalone provider
    /// with the exact same parameters, and so the wizard/settings write-sites encrypt with
    /// the exact same purpose string.
    /// </summary>
    public static class ConnectionStringProtector
    {
        /// <summary>Sentinel prefix marking an encrypted connection string in appsettings.json.</summary>
        public const string EncryptedPrefix = "enc:";

        private const string ApplicationName = "IdentityCenter";
        private const string ProtectorPurpose = "IdentityCenter.Encryption";
        private const string KeyDirectory = @"C:\ProgramData\IdentityCenter\Keys";

        /// <summary>True if the stored value carries the <see cref="EncryptedPrefix"/> sentinel.</summary>
        public static bool IsEncrypted(string? value) =>
            !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Builds a standalone DataProtection protector using the same parameters as the
        /// DI EncryptionService. Used only by the early bootstrap path in Program.cs.
        /// </summary>
        public static IDataProtector CreateBootstrapProtector()
        {
            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(KeyDirectory),
                configure => configure.SetApplicationName(ApplicationName));
            return provider.CreateProtector(ProtectorPurpose);
        }

        /// <summary>
        /// Resolves a stored connection string to plaintext for use at startup.
        ///   - "enc:"-prefixed  → strip prefix, unprotect (throws if the keyring can't decrypt).
        ///   - anything else    → returned unchanged (backward compatible with existing plaintext).
        /// Placeholder detection (localdb / ** / YOUR_*) is the caller's responsibility and runs
        /// against the value this method returns.
        /// </summary>
        /// <exception cref="ConnectionStringDecryptException">
        /// Thrown when an "enc:"-prefixed value cannot be unprotected (missing/rotated keyring,
        /// machine change, etc.). The caller should surface an actionable message and route to setup.
        /// </exception>
        public static string? Resolve(string? storedValue)
        {
            if (!IsEncrypted(storedValue))
                return storedValue; // bare plaintext (legacy) or null/empty — pass through unchanged

            var cipher = storedValue!.Substring(EncryptedPrefix.Length);
            try
            {
                return CreateBootstrapProtector().Unprotect(cipher);
            }
            catch (Exception ex)
            {
                throw new ConnectionStringDecryptException(
                    "The database connection string in appsettings.json is encrypted but could not be " +
                    "decrypted with this machine's DataProtection keyring at " + KeyDirectory + ". " +
                    "This usually means the keyring was lost, rotated, or the app was moved to a different " +
                    "machine. Re-run the setup wizard to re-enter and re-encrypt the connection string.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Raised when an encrypted connection string cannot be decrypted at startup.
    /// Distinguishes a recoverable "re-run setup" condition from a generic crash.
    /// </summary>
    public sealed class ConnectionStringDecryptException : Exception
    {
        public ConnectionStringDecryptException(string message, Exception inner)
            : base(message, inner) { }
    }
}
