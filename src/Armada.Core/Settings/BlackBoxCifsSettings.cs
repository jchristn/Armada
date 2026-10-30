namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box CIFS (SMB) target settings (blackBox.storage.cifs). Password is a secret.
    /// </summary>
    public class BlackBoxCifsSettings
    {
        /// <summary>
        /// Server host name.
        /// </summary>
        public string? Hostname
        {
            get => _Hostname;
            set => _Hostname = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Share name.
        /// </summary>
        public string? Share
        {
            get => _Share;
            set => _Share = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Domain, or null.
        /// </summary>
        public string? Domain
        {
            get => _Domain;
            set => _Domain = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// User name.
        /// </summary>
        public string? Username
        {
            get => _Username;
            set => _Username = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Password (secret).
        /// </summary>
        public string? Password
        {
            get => _Password;
            set => _Password = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// SMB port (1 to 65535, default 445).
        /// </summary>
        public int Port
        {
            get => _Port;
            set => _Port = Math.Clamp(value, 1, 65535);
        }

        private string? _Hostname = null;
        private string? _Share = null;
        private string? _Domain = null;
        private string? _Username = null;
        private string? _Password = null;
        private int _Port = 445;

        /// <summary>
        /// Prefer SMB 3 encryption when the server and platform support it (default true).
        /// </summary>
        public bool PreferEncryption { get; set; } = true;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxCifsSettings()
        {
        }
    }
}
