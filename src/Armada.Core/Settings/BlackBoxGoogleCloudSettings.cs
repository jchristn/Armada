namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box Google Cloud Storage target settings (blackBox.storage.googleCloud). The credentials file content is secret and never read into settings responses.
    /// </summary>
    public class BlackBoxGoogleCloudSettings
    {
        /// <summary>
        /// Project id.
        /// </summary>
        public string? ProjectId
        {
            get => _ProjectId;
            set => _ProjectId = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Bucket.
        /// </summary>
        public string? Bucket
        {
            get => _Bucket;
            set => _Bucket = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Path to the service account credentials JSON file (secret: masked on read).
        /// </summary>
        public string? CredentialsPath
        {
            get => _CredentialsPath;
            set => _CredentialsPath = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private string? _ProjectId = null;
        private string? _Bucket = null;
        private string? _CredentialsPath = null;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxGoogleCloudSettings()
        {
        }
    }
}
