namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box Azure Blob target settings (blackBox.storage.azureBlob). AccessKey and ConnectionString are secrets.
    /// </summary>
    public class BlackBoxAzureBlobSettings
    {
        /// <summary>
        /// Storage account name.
        /// </summary>
        public string? AccountName
        {
            get => _AccountName;
            set => _AccountName = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Container.
        /// </summary>
        public string? Container
        {
            get => _Container;
            set => _Container = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Account key (secret).
        /// </summary>
        public string? AccessKey
        {
            get => _AccessKey;
            set => _AccessKey = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Connection string (secret), used instead of AccountName and AccessKey when set.
        /// </summary>
        public string? ConnectionString
        {
            get => _ConnectionString;
            set => _ConnectionString = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private string? _AccountName = null;
        private string? _Container = null;
        private string? _AccessKey = null;
        private string? _ConnectionString = null;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxAzureBlobSettings()
        {
        }
    }
}
