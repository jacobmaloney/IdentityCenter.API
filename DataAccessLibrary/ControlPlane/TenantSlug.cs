using System.Text.RegularExpressions;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Validation + normalization for a tenant slug.
///
/// SECURITY-CRITICAL: a tenant slug becomes part of a physical database name
/// (<c>IdentityCenter_{slug}</c>) which is then interpolated into a non-parameterizable
/// <c>CREATE DATABASE [..]</c> statement. SQL Server does not allow database identifiers
/// to be passed as parameters, so the ONLY thing standing between a caller-supplied slug
/// and SQL injection is this whitelist. Treat any change here as a security change.
///
/// Rules (intentionally strict):
///   - lowercase ASCII letters, digits, and single internal hyphens only ([a-z0-9-])
///   - must start and end with an alphanumeric (no leading/trailing/doubled hyphen
///     is *required*, but doubled hyphens are allowed mid-string for readability)
///   - length 2..40 characters
///
/// Because the character class is a strict whitelist (no quotes, brackets, semicolons,
/// whitespace, or comment tokens can ever appear), a validated slug is safe to embed in a
/// bracketed identifier. We STILL escape any closing bracket defensively in
/// <see cref="ToDatabaseName"/> as belt-and-suspenders, even though the whitelist already
/// forbids it.
/// </summary>
public static class TenantSlug
{
    public const int MinLength = 2;
    public const int MaxLength = 40;

    /// <summary>Prefix applied to every tenant database name.</summary>
    public const string DatabaseNamePrefix = "IdentityCenter_";

    // Anchored, lowercase-only, alphanumeric boundaries, hyphens allowed internally.
    private static readonly Regex SlugPattern = new(
        @"^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True if <paramref name="slug"/> satisfies every rule. Does NOT mutate or trim —
    /// callers should normalize first if they want to accept mixed-case input.
    /// </summary>
    public static bool IsValid(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return false;
        if (slug.Length < MinLength || slug.Length > MaxLength) return false;
        return SlugPattern.IsMatch(slug);
    }

    /// <summary>
    /// Lowercases + trims surrounding whitespace, then validates. Returns the normalized
    /// slug on success. Throws <see cref="ArgumentException"/> on any rule violation so an
    /// invalid slug can never silently flow into a database name.
    /// </summary>
    public static string Normalize(string? slug)
    {
        var candidate = slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsValid(candidate))
        {
            throw new ArgumentException(
                $"Invalid tenant slug '{slug}'. A slug must be {MinLength}-{MaxLength} characters, " +
                "lowercase letters/digits/hyphens only, and start and end with a letter or digit.",
                nameof(slug));
        }
        return candidate;
    }

    /// <summary>
    /// Composes the physical database name for a slug. Re-validates (defense in depth) so this
    /// method is safe to call even if a caller skipped <see cref="Normalize"/>, then escapes any
    /// closing bracket for bracketed-identifier safety (the whitelist already forbids ']', so this
    /// can never actually fire — it exists so a future relaxation of the whitelist cannot silently
    /// open an injection hole).
    /// </summary>
    public static string ToDatabaseName(string slug)
    {
        var normalized = Normalize(slug);
        var dbName = DatabaseNamePrefix + normalized;
        return dbName.Replace("]", "]]");
    }
}
