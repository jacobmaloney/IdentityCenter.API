namespace DataAccessLibrary.Models
{
    /// <summary>
    /// White-label branding configuration for the portal.
    /// Stored as JSON in the Settings table under Category = "Branding".
    /// </summary>
    public class BrandingSettings
    {
        public string ProductName { get; set; } = "Identity Center";
        public string? CompanyName { get; set; }
        public string? LogoBase64 { get; set; }       // base64-encoded image data (no data: URI prefix)
        public string? LogoMimeType { get; set; }      // e.g. image/png, image/svg+xml, image/jpeg
        public string PrimaryColor { get; set; } = "#00bcd4";
        public string AccentColor { get; set; } = "#14b8a6";
        public string? FaviconBase64 { get; set; }     // base64-encoded favicon (ICO or PNG)
        public string? FaviconMimeType { get; set; }
        public string? FooterText { get; set; }
        public string? Tagline { get; set; }           // "I'm lovin' it", "Have it your way", etc. Shows on dashboard

        // Dark mode colors — if not set, uses same as light mode
        public string? DarkPrimaryColor { get; set; }
        public string? DarkAccentColor { get; set; }

        /// <summary>Support email shown on login / footer / error pages for white-label deployments.</summary>
        public string? SupportEmail { get; set; }

        /// <summary>When true, hides "Powered by IdentityCenter" attribution and other vendor markings.</summary>
        public bool WhiteLabelMode { get; set; } = false;

        /// <summary>Returns a CSS data URI for the logo, or null if no logo is set.</summary>
        public string? GetLogoDataUri()
        {
            if (string.IsNullOrWhiteSpace(LogoBase64) || string.IsNullOrWhiteSpace(LogoMimeType))
                return null;
            return string.Concat("data:", LogoMimeType, ";base64,", LogoBase64);
        }

        /// <summary>Returns a CSS data URI for the favicon, or null if no favicon is set.</summary>
        public string? GetFaviconDataUri()
        {
            if (string.IsNullOrWhiteSpace(FaviconBase64) || string.IsNullOrWhiteSpace(FaviconMimeType))
                return null;
            return string.Concat("data:", FaviconMimeType, ";base64,", FaviconBase64);
        }
    }
}
