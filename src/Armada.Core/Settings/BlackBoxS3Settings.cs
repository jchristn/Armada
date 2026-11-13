namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box S3 and S3-compatible target settings (blackBox.storage.s3). AccessKey and SecretKey are secrets: masked on every settings read and kept when a masked value is written back.
    /// </summary>
    public class BlackBoxS3Settings
    {
        /// <summary>
        /// Endpoint URL for S3-compatible stores (Less3, MinIO), or null for AWS.
        /// </summary>
        public string? Endpoint
        {
            get => _Endpoint;
            set => _Endpoint = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Region. Default us-east-1.
        /// </summary>
        public string? Region
        {
            get => _Region;
            set => _Region = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Bucket (required for the S3 target).
        /// </summary>
        public string? Bucket
        {
            get => _Bucket;
            set => _Bucket = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Access key (secret).
        /// </summary>
        public string? AccessKey
        {
            get => _AccessKey;
            set => _AccessKey = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Secret key (secret).
        /// </summary>
        public string? SecretKey
        {
            get => _SecretKey;
            set => _SecretKey = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Use path-style addressing. Default true.
        /// </summary>
        public bool UsePathStyle { get; set; } = true;

        /// <summary>
        /// Use TLS. Default true.
        /// </summary>
        public bool UseSsl { get; set; } = true;

        private string? _Endpoint = null;
        private string? _Region = "us-east-1";
        private string? _Bucket = null;
        private string? _AccessKey = null;
        private string? _SecretKey = null;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxS3Settings()
        {
        }
    }
}
