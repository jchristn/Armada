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

        private string? _Hostname = null;
        private string? _Share = null;
        private string? _Domain = null;
        private string? _Username = null;
        private string? _Password = null;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxCifsSettings()
        {
        }
    }
}
