namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Validates black box storage settings for a target: required fields, endpoint URLs, host names and paths. Messages
    /// name the setting (for example blackBox.storage.s3.bucket) and never contain a secret value. Thread-safe.
    /// </summary>
    public static class BlackBoxStorageSettingsValidator
    {
        #region Public-Methods

        /// <summary>
        /// Validate the settings of a target (the configured target when target is null).
        /// </summary>
        /// <param name="settings">Storage settings.</param>
        /// <param name="target">Target to validate, or null for settings.Target.</param>
        /// <returns>Errors; empty when the target is usable.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static List<string> Validate(BlackBoxStorageSettings settings, BlackBoxStorageTargetEnum? target = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            List<string> errors = new List<string>();
            BlackBoxStorageTargetEnum t = target ?? settings.Target;
            switch (t)
            {
                case BlackBoxStorageTargetEnum.Database:
                    break;
                case BlackBoxStorageTargetEnum.Disk:
                    ValidateDisk(settings.Disk, errors);
                    break;
                case BlackBoxStorageTargetEnum.S3:
                    ValidateS3(settings.S3, errors);
                    break;
                case BlackBoxStorageTargetEnum.AzureBlob:
                    ValidateAzure(settings.AzureBlob, errors);
                    break;
                case BlackBoxStorageTargetEnum.GoogleCloud:
                    ValidateGoogle(settings.GoogleCloud, errors);
                    break;
                case BlackBoxStorageTargetEnum.Cifs:
                    ValidateCifs(settings.Cifs, errors);
                    break;
                case BlackBoxStorageTargetEnum.Nfs:
                    ValidateNfs(settings.Nfs, errors);
                    break;
                default:
                    errors.Add("Unknown black box storage target '" + t + "'; use Database, Disk, S3, AzureBlob, GoogleCloud, Cifs or Nfs.");
                    break;
            }
            return errors;
        }

        /// <summary>
        /// Validate and throw when the target is not usable.
        /// </summary>
        /// <param name="settings">Storage settings.</param>
        /// <param name="target">Target to validate, or null for settings.Target.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ArgumentException">Thrown with every error joined when the settings are invalid.</exception>
        public static void Require(BlackBoxStorageSettings settings, BlackBoxStorageTargetEnum? target = null)
        {
            List<string> errors = Validate(settings, target);
            if (errors.Count > 0) throw new ArgumentException(String.Join(" ", errors));
        }

        /// <summary>
        /// Parse an HTTP or HTTPS endpoint (a scheme is optional; useSsl chooses it when absent).
        /// </summary>
        /// <param name="endpoint">Endpoint text.</param>
        /// <param name="useSsl">Scheme to assume when none is given.</param>
        /// <param name="uri">Parsed URI.</param>
        /// <returns>True when the endpoint is an absolute http or https URL with a host.</returns>
        public static bool TryParseEndpoint(string? endpoint, bool useSsl, out Uri? uri)
        {
            uri = null;
            if (String.IsNullOrWhiteSpace(endpoint)) return false;
            string text = endpoint.Trim();
            if (!text.Contains("://", StringComparison.Ordinal)) text = (useSsl ? "https://" : "http://") + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
            if (String.IsNullOrEmpty(parsed.Host)) return false;
            uri = parsed;
            return true;
        }

        #endregion

        #region Private-Methods

        private static void ValidateDisk(BlackBoxDiskSettings disk, List<string> errors)
        {
            if (disk.Directory == null) return;
            if (disk.Directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errors.Add("blackBox.storage.disk.directory contains characters that are not valid in a path.");
                return;
            }
            try
            {
                Path.GetFullPath(disk.Directory);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                errors.Add("blackBox.storage.disk.directory is not a valid path: " + e.Message);
            }
        }

        private static void ValidateS3(BlackBoxS3Settings s3, List<string> errors)
        {
            if (s3.Bucket == null) errors.Add("blackBox.storage.s3.bucket is required for the S3 target.");
            else if (s3.Bucket.Length < 3 || s3.Bucket.Length > 255 || s3.Bucket.IndexOfAny(new[] { '/', '\\', ' ', '\t' }) >= 0)
                errors.Add("blackBox.storage.s3.bucket must be 3 to 255 characters without slashes or spaces.");
            if (s3.Region == null) errors.Add("blackBox.storage.s3.region is required for the S3 target (us-east-1 for most S3-compatible stores).");
            if ((s3.AccessKey == null) != (s3.SecretKey == null))
                errors.Add("blackBox.storage.s3.accessKey and blackBox.storage.s3.secretKey must both be set or both be empty.");
            if (s3.Endpoint != null)
            {
                if (!TryParseEndpoint(s3.Endpoint, s3.UseSsl, out Uri? uri))
                {
                    errors.Add("blackBox.storage.s3.endpoint must be an http or https URL with a host, for example http://localhost:9000.");
                }
                else if (s3.Endpoint.Contains("://", StringComparison.Ordinal) && (uri!.Scheme == Uri.UriSchemeHttps) != s3.UseSsl)
                {
                    errors.Add("blackBox.storage.s3.endpoint uses " + uri.Scheme + " but blackBox.storage.s3.useSsl is " + (s3.UseSsl ? "true" : "false") + ".");
                }
                if (s3.AccessKey == null) errors.Add("blackBox.storage.s3.accessKey and secretKey are required with a custom endpoint.");
            }
        }

        private static void ValidateAzure(BlackBoxAzureBlobSettings azure, List<string> errors)
        {
            if (azure.Container == null) errors.Add("blackBox.storage.azureBlob.container is required for the AzureBlob target.");
            if (azure.ConnectionString != null)
            {
                Dictionary<string, string> parts = ParseConnectionString(azure.ConnectionString);
                if (!parts.ContainsKey("AccountName") || !parts.ContainsKey("AccountKey"))
                    errors.Add("blackBox.storage.azureBlob.connectionString must contain AccountName and AccountKey.");
                if (parts.TryGetValue("BlobEndpoint", out string? blobEndpoint) && !TryParseEndpoint(blobEndpoint, true, out _))
                    errors.Add("The BlobEndpoint in blackBox.storage.azureBlob.connectionString must be an http or https URL.");
            }
            else
            {
                if (azure.AccountName == null) errors.Add("blackBox.storage.azureBlob.accountName is required for the AzureBlob target (or set connectionString).");
                if (azure.AccessKey == null) errors.Add("blackBox.storage.azureBlob.accessKey is required for the AzureBlob target (or set connectionString).");
            }
            if (azure.Endpoint != null && !TryParseEndpoint(azure.Endpoint, true, out _))
                errors.Add("blackBox.storage.azureBlob.endpoint must be an http or https URL with a host.");
        }

        private static void ValidateGoogle(BlackBoxGoogleCloudSettings google, List<string> errors)
        {
            if (google.ProjectId == null) errors.Add("blackBox.storage.googleCloud.projectId is required for the GoogleCloud target.");
            if (google.Bucket == null) errors.Add("blackBox.storage.googleCloud.bucket is required for the GoogleCloud target.");
            if (google.CredentialsPath == null) errors.Add("blackBox.storage.googleCloud.credentialsPath is required for the GoogleCloud target.");
            else if (!File.Exists(google.CredentialsPath)) errors.Add("The file named by blackBox.storage.googleCloud.credentialsPath does not exist.");
            if (google.Endpoint != null && !TryParseEndpoint(google.Endpoint, true, out _))
                errors.Add("blackBox.storage.googleCloud.endpoint must be an http or https URL with a host.");
        }

        private static void ValidateCifs(BlackBoxCifsSettings cifs, List<string> errors)
        {
            if (cifs.Hostname == null) errors.Add("blackBox.storage.cifs.hostname is required for the Cifs target.");
            else if (Uri.CheckHostName(cifs.Hostname) == UriHostNameType.Unknown) errors.Add("blackBox.storage.cifs.hostname is not a valid host name or address.");
            if (cifs.Share == null) errors.Add("blackBox.storage.cifs.share is required for the Cifs target.");
            if (cifs.Username == null) errors.Add("blackBox.storage.cifs.username is required for the Cifs target.");
            if (cifs.Password == null) errors.Add("blackBox.storage.cifs.password is required for the Cifs target.");
        }

        private static void ValidateNfs(BlackBoxNfsSettings nfs, List<string> errors)
        {
            if (nfs.Hostname == null) errors.Add("blackBox.storage.nfs.hostname is required for the Nfs target.");
            else if (Uri.CheckHostName(nfs.Hostname) == UriHostNameType.Unknown) errors.Add("blackBox.storage.nfs.hostname is not a valid host name or address.");
            if (nfs.ExportPath == null) errors.Add("blackBox.storage.nfs.exportPath is required for the Nfs target.");
            else if (!nfs.ExportPath.StartsWith("/", StringComparison.Ordinal)) errors.Add("blackBox.storage.nfs.exportPath must be an absolute export path such as /export.");
        }

        internal static Dictionary<string, string> ParseConnectionString(string connectionString)
        {
            Dictionary<string, string> parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                parts[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
            }
            return parts;
        }

        #endregion
    }
}
