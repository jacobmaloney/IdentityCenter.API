namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Active Directory connection configuration
    /// </summary>
    public class ADConnectionConfig
    {
        public string Server { get; set; } = string.Empty;
        public string? SearchBase { get; set; }
        public int? Port { get; set; }
        public string? UserFilter { get; set; }
        public string? GroupFilter { get; set; }
        public int? PageSize { get; set; }
        public bool UseSsl { get; set; }
    }

    /// <summary>
    /// Active Directory credentials
    /// </summary>
    public class ADCredentials
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Domain { get; set; }
    }
}
