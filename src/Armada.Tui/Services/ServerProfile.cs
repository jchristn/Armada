namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// A saved Armada server the TUI can connect to.
    /// </summary>
    public class ServerProfile
    {
        #region Public-Members

        /// <summary>
        /// Profile name, unique (case-insensitive).
        /// </summary>
        public string Name { get; set; } = "default";

        /// <summary>
        /// Admiral base URL.
        /// </summary>
        public string Url { get; set; } = "http://127.0.0.1:7890";

        /// <summary>
        /// Email last used to sign in, or null.
        /// </summary>
        public string? LastUser { get; set; } = null;

        /// <summary>
        /// Tenant last used, or null.
        /// </summary>
        public string? LastTenantId { get; set; } = null;

        /// <summary>
        /// password or apikey: the last login method.
        /// </summary>
        public string AuthMethod { get; set; } = "password";

        /// <summary>
        /// Last successful sign-in, or null.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ServerProfile()
        {
        }

        #endregion
    }
}
