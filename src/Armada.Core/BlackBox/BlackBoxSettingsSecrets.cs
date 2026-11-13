namespace Armada.Core.BlackBox
{
    using System;
    using System.Text.Json;
    using Armada.Core.Settings;

    /// <summary>
    /// Masks black box storage secrets for settings reads (S3 AccessKey and SecretKey, Azure AccessKey and
    /// ConnectionString, Google CredentialsPath, CIFS Password) and keeps stored secrets when a masked value is written
    /// back. Thread-safe (stateless).
    /// </summary>
    public static class BlackBoxSettingsSecrets
    {
        /// <summary>
        /// A copy of the settings with every secret replaced by the marker (null secrets stay null).
        /// </summary>
        /// <param name="source">Settings.</param>
        /// <param name="marker">Mask marker (for example "********").</param>
        /// <returns>The masked copy.</returns>
        /// <exception cref="ArgumentNullException">Thrown when source or marker is null.</exception>
        public static BlackBoxSettings Mask(BlackBoxSettings source, string marker)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (marker == null) throw new ArgumentNullException(nameof(marker));
            BlackBoxSettings copy = JsonSerializer.Deserialize<BlackBoxSettings>(JsonSerializer.Serialize(source)) ?? new BlackBoxSettings();
            BlackBoxStorageSettings s = copy.Storage;
            s.S3.AccessKey = MaskValue(s.S3.AccessKey, marker);
            s.S3.SecretKey = MaskValue(s.S3.SecretKey, marker);
            s.AzureBlob.AccessKey = MaskValue(s.AzureBlob.AccessKey, marker);
            s.AzureBlob.ConnectionString = MaskValue(s.AzureBlob.ConnectionString, marker);
            s.GoogleCloud.CredentialsPath = MaskValue(s.GoogleCloud.CredentialsPath, marker);
            s.Cifs.Password = MaskValue(s.Cifs.Password, marker);
            return copy;
        }

        /// <summary>
        /// Keep the current secret wherever the incoming settings carry the marker (a value read back from GET).
        /// </summary>
        /// <param name="incoming">Incoming settings (modified in place).</param>
        /// <param name="current">Current settings.</param>
        /// <param name="marker">Mask marker.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void KeepMasked(BlackBoxSettings incoming, BlackBoxSettings current, string marker)
        {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (marker == null) throw new ArgumentNullException(nameof(marker));
            BlackBoxStorageSettings i = incoming.Storage;
            BlackBoxStorageSettings c = current.Storage;
            if (i.S3.AccessKey == marker) i.S3.AccessKey = c.S3.AccessKey;
            if (i.S3.SecretKey == marker) i.S3.SecretKey = c.S3.SecretKey;
            if (i.AzureBlob.AccessKey == marker) i.AzureBlob.AccessKey = c.AzureBlob.AccessKey;
            if (i.AzureBlob.ConnectionString == marker) i.AzureBlob.ConnectionString = c.AzureBlob.ConnectionString;
            if (i.GoogleCloud.CredentialsPath == marker) i.GoogleCloud.CredentialsPath = c.GoogleCloud.CredentialsPath;
            if (i.Cifs.Password == marker) i.Cifs.Password = c.Cifs.Password;
        }

        private static string? MaskValue(string? value, string marker)
        {
            return String.IsNullOrEmpty(value) ? null : marker;
        }
    }
}
