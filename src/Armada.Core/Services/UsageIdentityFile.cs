namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text.Json;
    using SyslogLogging;

    /// <summary>
    /// The persisted usage capture identity of one host: its stable host id and the protected key its
    /// account keys are HMAC'd with. Stored as one file under the host's data directory, created on
    /// first use and never rewritten, so identities survive restarts. Key loss is documented: it creates
    /// a new account identity and closes the old membership; it never merges accounts.
    /// </summary>
    public class UsageIdentityFile
    {
        #region Public-Members

        /// <summary>
        /// Stable host identity (for example hst_... on the Admiral, or the Harbor id on a Harbor).
        /// </summary>
        public string HostId
        {
            get => _HostId;
            set => _HostId = String.IsNullOrWhiteSpace(value) ? "host-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant() : value.Trim();
        }

        /// <summary>
        /// Protected account key material (at least 32 bytes), hex encoded.
        /// </summary>
        public string Key
        {
            get => _Key;
            set => _Key = String.IsNullOrWhiteSpace(value) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() : value.Trim();
        }

        #endregion

        #region Private-Members

        private string _HostId = String.Empty;
        private string _Key = String.Empty;
        private const string _FileName = "usage-identity.json";
        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageIdentityFile()
        {
            HostId = String.Empty;
            Key = String.Empty;
        }

        /// <summary>
        /// Load the identity file from the data directory, creating it (with a generated host id and key)
        /// when it does not exist yet. Returns the in-memory identity even when the write fails, so
        /// capture keeps working with a process-local identity rather than refusing to run.
        /// </summary>
        /// <param name="dataDirectory">Host data directory.</param>
        /// <param name="logging">Logging module, or null for silent operation.</param>
        /// <returns>The loaded or created identity.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the data directory is null or empty.</exception>
        public static UsageIdentityFile Load(string dataDirectory, LoggingModule? logging = null)
        {
            if (String.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentNullException(nameof(dataDirectory));

            string path = Path.Combine(dataDirectory, _FileName);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    UsageIdentityFile? loaded = JsonSerializer.Deserialize<UsageIdentityFile>(json, _Options);
                    if (loaded != null && !String.IsNullOrWhiteSpace(loaded.HostId) && !String.IsNullOrWhiteSpace(loaded.Key))
                    {
                        return loaded;
                    }

                    logging?.Warn("[UsageIdentityFile] " + path + " exists but is incomplete; a new identity is generated");
                }
                catch (Exception e)
                {
                    logging?.Warn("[UsageIdentityFile] could not read " + path + "; a new identity is generated: " + e.Message);
                }
            }

            UsageIdentityFile identity = new UsageIdentityFile();
            try
            {
                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(path, JsonSerializer.Serialize(identity, _Options));
            }
            catch (Exception e)
            {
                logging?.Warn("[UsageIdentityFile] could not persist the usage identity to " + path + "; identities restart with this process: " + e.Message);
            }

            return identity;
        }

        #endregion
    }
}