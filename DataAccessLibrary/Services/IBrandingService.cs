using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Provides white-label branding configuration for the portal.
    /// Settings are cached in memory; call InvalidateCacheAsync after saves.
    /// </summary>
    public interface IBrandingService
    {
        /// <summary>Returns the current branding settings (cached).</summary>
        Task<BrandingSettings> GetBrandingAsync();

        /// <summary>
        /// Returns the cached product name synchronously, or a default if the cache is cold.
        /// Use this from sync paths (Razor markup, sync method bodies, log messages) to avoid the
        /// sync-over-async <c>GetBrandingAsync().GetAwaiter().GetResult()</c> pattern. The cache is
        /// populated on the first <see cref="GetBrandingAsync"/> call (typically MainLayout init).
        /// </summary>
        string ProductName { get; }

        /// <summary>Persists branding settings and invalidates the cache.</summary>
        Task SaveBrandingAsync(BrandingSettings settings);

        /// <summary>Forces the in-memory cache to reload on the next call.</summary>
        void InvalidateCache();
    }
}
