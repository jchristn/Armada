namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Blobject.AmazonS3;
    using Blobject.AzureBlob;
    using Blobject.CIFS;
    using Blobject.Core;
    using Blobject.Disk;
    using Blobject.GoogleCloud;
    using Blobject.NFS;

    /// <summary>
    /// Builds the Blobject client of an external black box target from validated settings. Thread-safe.
    /// </summary>
    public static class BlackBoxBlobClientFactory
    {
        /// <summary>
        /// Sub-directory of the data directory used by the Disk target when no directory is configured.
        /// </summary>
        public const string DefaultDiskDirectoryName = "blackbox";

        /// <summary>
        /// The Disk target's root directory: the configured directory (relative paths resolve against the data
        /// directory), or {dataDirectory}/blackbox.
        /// </summary>
        /// <param name="settings">Storage settings.</param>
        /// <param name="dataDirectory">Armada data directory, or null.</param>
        /// <returns>The absolute root directory.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ArgumentException">Thrown when neither a directory nor a data directory is available.</exception>
        public static string ResolveDiskDirectory(BlackBoxStorageSettings settings, string? dataDirectory)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string? configured = settings.Disk.Directory;
            if (configured != null)
            {
                if (Path.IsPathRooted(configured) || String.IsNullOrEmpty(dataDirectory)) return Path.GetFullPath(configured);
                return Path.GetFullPath(Path.Combine(dataDirectory, configured));
            }
            if (String.IsNullOrEmpty(dataDirectory)) throw new ArgumentException("blackBox.storage.disk.directory is not set and no data directory is known.");
            return Path.GetFullPath(Path.Combine(dataDirectory, DefaultDiskDirectoryName));
        }

        /// <summary>
        /// Create the client for an external target.
        /// </summary>
        /// <param name="target">Target (not Database).</param>
        /// <param name="settings">Storage settings.</param>
        /// <param name="dataDirectory">Armada data directory (Disk default), or null.</param>
        /// <returns>The client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the target's settings are invalid.</exception>
        /// <exception cref="NotSupportedException">Thrown for Database or an unknown target.</exception>
        public static BlobClientBase Create(BlackBoxStorageTargetEnum target, BlackBoxStorageSettings settings, string? dataDirectory)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            switch (target)
            {
                case BlackBoxStorageTargetEnum.Disk:
                    BlackBoxStorageSettingsValidator.Require(settings, target);
                    string directory = ResolveDiskDirectory(settings, dataDirectory);
                    Directory.CreateDirectory(directory);
                    return new DiskBlobClient(new DiskSettings(directory));
                case BlackBoxStorageTargetEnum.S3:
                    BlackBoxStorageSettingsValidator.Require(settings, target);
                    return CreateS3(settings.S3);
                case BlackBoxStorageTargetEnum.AzureBlob:
                    BlackBoxStorageSettingsValidator.Require(settings, target);
                    return CreateAzure(settings.AzureBlob);
                case BlackBoxStorageTargetEnum.GoogleCloud:
                    BlackBoxStorageSettingsValidator.Require(settings, target);
                    return CreateGoogle(settings.GoogleCloud);
                case BlackBoxStorageTargetEnum.Cifs:
                    BlackBoxStorageSettingsValidator.Require(settings, target);
                    return CreateCifs(settings.Cifs);
                case BlackBoxStorageTargetEnum.Nfs:
                    BlackBoxStorageSettingsValidator.Require(settings, target);
                    return CreateNfs(settings.Nfs);
                default:
                    throw new NotSupportedException("The black box " + target + " storage target has no Blobject client.");
            }
        }

        private static BlobClientBase CreateS3(BlackBoxS3Settings s3)
        {
            string region = s3.Region!;
            string bucket = s3.Bucket!;
            if (s3.Endpoint == null)
            {
                if (s3.AccessKey == null) return new AmazonS3BlobClient(new AwsSettings(null, null, region, bucket, s3.UseSsl));
                return new AmazonS3BlobClient(new AwsSettings(s3.AccessKey, s3.SecretKey, region, bucket, s3.UseSsl));
            }
            BlackBoxStorageSettingsValidator.TryParseEndpoint(s3.Endpoint, s3.UseSsl, out Uri? uri);
            string root = uri!.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            // Blobject forces path-style requests for a custom endpoint; the base URL only shapes generated object URLs.
            string baseUrl = s3.UsePathStyle
                ? root + "/" + bucket + "/{key}"
                : uri.Scheme + "://" + bucket + "." + uri.Authority + "/{key}";
            return new AmazonS3BlobClient(new AwsSettings(root + "/", s3.UseSsl, s3.AccessKey, s3.SecretKey, region, bucket, baseUrl));
        }

        private static BlobClientBase CreateAzure(BlackBoxAzureBlobSettings azure)
        {
            string? account = azure.AccountName;
            string? key = azure.AccessKey;
            string? endpoint = azure.Endpoint;
            if (azure.ConnectionString != null)
            {
                Dictionary<string, string> parts = BlackBoxStorageSettingsValidator.ParseConnectionString(azure.ConnectionString);
                account = parts["AccountName"];
                key = parts["AccountKey"];
                if (endpoint == null && parts.TryGetValue("BlobEndpoint", out string? blobEndpoint)) endpoint = blobEndpoint;
            }
            if (endpoint == null) endpoint = "https://" + account + ".blob.core.windows.net/";
            if (!endpoint.EndsWith("/", StringComparison.Ordinal)) endpoint += "/";
            AzureBlobSettings settings = new AzureBlobSettings(account!, key!, endpoint, azure.Container!);
            settings.Ssl = endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            return new AzureBlobClient(settings);
        }

        private static BlobClientBase CreateGoogle(BlackBoxGoogleCloudSettings google)
        {
            string json;
            try
            {
                json = File.ReadAllText(google.CredentialsPath!);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new ArgumentException("The file named by blackBox.storage.googleCloud.credentialsPath could not be read: " + e.Message, e);
            }
            if (google.Endpoint == null) return new GcpBlobClient(new GcpBlobSettings(google.ProjectId!, google.Bucket!, json));
            string endpoint = google.Endpoint.EndsWith("/", StringComparison.Ordinal) ? google.Endpoint : google.Endpoint + "/";
            return new GcpBlobClient(new GcpBlobSettings(google.ProjectId!, google.Bucket!, json, endpoint));
        }

        private static BlobClientBase CreateCifs(BlackBoxCifsSettings cifs)
        {
            CifsSettings settings = new CifsSettings(cifs.Hostname!, cifs.Port, cifs.Username!, cifs.Password!, cifs.Share!);
            if (cifs.Domain != null) settings.Domain = cifs.Domain;
            settings.PreferEncryption = cifs.PreferEncryption;
            return new CifsBlobClient(settings);
        }

        private static BlobClientBase CreateNfs(BlackBoxNfsSettings nfs)
        {
            NfsVersionEnum version = nfs.Version == 2 ? NfsVersionEnum.V2 : nfs.Version == 4 ? NfsVersionEnum.V4 : NfsVersionEnum.V3;
            NfsSettings settings = new NfsSettings(nfs.Hostname!, nfs.UserId, nfs.GroupId, nfs.ExportPath!, version);
            if (nfs.Port.HasValue) settings.Port = nfs.Port.Value;
            if (nfs.MountPort.HasValue) settings.MountPort = nfs.MountPort.Value;
            return new NfsBlobClient(settings);
        }
    }
}
