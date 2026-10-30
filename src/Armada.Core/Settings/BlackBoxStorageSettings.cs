namespace Armada.Core.Settings
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Black box storage settings (blackBox.storage). The index rows are always in the database; these settings choose
    /// where object bytes go. The target is recorded per entry, so changing it affects new entries only.
    /// </summary>
    public class BlackBoxStorageSettings
    {
        #region Public-Members

        /// <summary>
        /// Target for new entries. Default Database (blackbox_objects.data, included in backups). Disk, S3, AzureBlob,
        /// GoogleCloud, Cifs and Nfs need their sub-settings.
        /// </summary>
        public BlackBoxStorageTargetEnum Target { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Key prefix for external targets. Default "armada/blackbox". Leading and trailing slashes are removed; empty
        /// restores the default. Keys are {keyPrefix}/{tenantId}/{vesselId or _}/{yyyy}/{MM}/{entryId}/{objectName}.
        /// </summary>
        public string KeyPrefix
        {
            get => _KeyPrefix;
            set
            {
                string trimmed = (value ?? String.Empty).Trim().Trim('/');
                _KeyPrefix = trimmed.Length == 0 ? DefaultKeyPrefix : trimmed;
            }
        }

        /// <summary>
        /// Whether objects are gzip-compressed when stored. Default true.
        /// </summary>
        public bool Compress { get; set; } = true;

        /// <summary>
        /// Largest evidence object in bytes (uncompressed). Default 1048576 (1 MB), clamped to 65536..16777216. Larger
        /// objects keep their head and tail with a marker line, and the original size is recorded.
        /// </summary>
        public int MaxEvidenceObjectBytes
        {
            get => _MaxEvidenceObjectBytes;
            set => _MaxEvidenceObjectBytes = Math.Clamp(value, 65536, 16777216);
        }

        /// <summary>
        /// Largest entry in bytes (stored, compressed). Default 8388608 (8 MB), clamped to 1048576..67108864. Over budget,
        /// the largest evidence objects are trimmed first.
        /// </summary>
        public int MaxEntryBytes
        {
            get => _MaxEntryBytes;
            set => _MaxEntryBytes = Math.Clamp(value, 1048576, 67108864);
        }

        /// <summary>
        /// When an external write fails, write the entry to the database target instead and raise
        /// blackbox.storage_fallback. Default true. With fallback off, the index row and record.json still go to the
        /// database; only the evidence objects are missing.
        /// </summary>
        public bool FallbackToDatabase { get; set; } = true;

        /// <summary>Disk target settings. Never null.</summary>
        public BlackBoxDiskSettings Disk
        {
            get => _Disk;
            set => _Disk = value ?? new BlackBoxDiskSettings();
        }

        /// <summary>S3 and S3-compatible target settings. Never null.</summary>
        public BlackBoxS3Settings S3
        {
            get => _S3;
            set => _S3 = value ?? new BlackBoxS3Settings();
        }

        /// <summary>Azure Blob target settings. Never null.</summary>
        public BlackBoxAzureBlobSettings AzureBlob
        {
            get => _AzureBlob;
            set => _AzureBlob = value ?? new BlackBoxAzureBlobSettings();
        }

        /// <summary>Google Cloud Storage target settings. Never null.</summary>
        public BlackBoxGoogleCloudSettings GoogleCloud
        {
            get => _GoogleCloud;
            set => _GoogleCloud = value ?? new BlackBoxGoogleCloudSettings();
        }

        /// <summary>CIFS target settings. Never null.</summary>
        public BlackBoxCifsSettings Cifs
        {
            get => _Cifs;
            set => _Cifs = value ?? new BlackBoxCifsSettings();
        }

        /// <summary>NFS target settings. Never null.</summary>
        public BlackBoxNfsSettings Nfs
        {
            get => _Nfs;
            set => _Nfs = value ?? new BlackBoxNfsSettings();
        }

        /// <summary>
        /// Default key prefix.
        /// </summary>
        public const string DefaultKeyPrefix = "armada/blackbox";

        #endregion

        #region Private-Members

        private string _KeyPrefix = DefaultKeyPrefix;
        private int _MaxEvidenceObjectBytes = 1048576;
        private int _MaxEntryBytes = 8388608;
        private BlackBoxDiskSettings _Disk = new BlackBoxDiskSettings();
        private BlackBoxS3Settings _S3 = new BlackBoxS3Settings();
        private BlackBoxAzureBlobSettings _AzureBlob = new BlackBoxAzureBlobSettings();
        private BlackBoxGoogleCloudSettings _GoogleCloud = new BlackBoxGoogleCloudSettings();
        private BlackBoxCifsSettings _Cifs = new BlackBoxCifsSettings();
        private BlackBoxNfsSettings _Nfs = new BlackBoxNfsSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxStorageSettings()
        {
        }

        #endregion
    }
}
