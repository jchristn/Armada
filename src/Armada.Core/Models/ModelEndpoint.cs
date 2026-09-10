namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A configured external model endpoint (an embedding or inference/completion model behind a provider
    /// API). Armada manages and monitors these: it health-checks the base URL, lets an operator validate the
    /// model with a real request, and never returns the stored API key.
    /// </summary>
    public class ModelEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (mep_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Human-facing endpoint name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value.Trim();
            }
        }

        /// <summary>
        /// Whether this endpoint serves embeddings or inference (chat/completion). Selects how it is
        /// validated.
        /// </summary>
        public ModelEndpointKindEnum Kind { get; set; } = ModelEndpointKindEnum.Inference;

        /// <summary>
        /// Ownership scope: a tenant-wide endpoint is visible to everyone in the tenant but editable only by
        /// tenant/global admins; a user-specific endpoint is owned by <see cref="UserId"/>. Defaults to
        /// tenant-wide (existing rows and admin-created rows); regular users create user-specific endpoints.
        /// </summary>
        public ScopeEnum Scope { get; set; } = ScopeEnum.TenantWide;

        /// <summary>
        /// The provider/wire-format the endpoint speaks.
        /// </summary>
        public ModelProviderEnum Provider { get; set; } = ModelProviderEnum.OpenAI;

        /// <summary>
        /// Base URL of the endpoint (scheme + host [+ port], no trailing path required).
        /// </summary>
        public string BaseUrl
        {
            get => _BaseUrl;
            set => _BaseUrl = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// The model identifier to request (e.g. "text-embedding-3-small", "gpt-4o-mini", "voyage-3.5"). For
        /// Azure OpenAI this is the deployment name; for Bedrock, the Bedrock model id.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Cloud region for providers that require one (Vertex AI, AWS Bedrock). Null/unused for others.
        /// </summary>
        public string? Region
        {
            get => _Region;
            set => _Region = Normalize(value);
        }

        /// <summary>
        /// GCP project id for Vertex AI. Null/unused for other providers.
        /// </summary>
        public string? Project
        {
            get => _Project;
            set => _Project = Normalize(value);
        }

        /// <summary>
        /// API version for Azure OpenAI (defaults to the provider's current GA version when omitted).
        /// Null/unused for other providers.
        /// </summary>
        public string? ApiVersion
        {
            get => _ApiVersion;
            set => _ApiVersion = Normalize(value);
        }

        /// <summary>
        /// AWS access key id for Bedrock. The paired secret access key is stored in <see cref="ApiKey"/>.
        /// This is an identifier rather than a secret, so it is returned on reads. Null/unused for other
        /// providers.
        /// </summary>
        public string? AccessKeyId
        {
            get => _AccessKeyId;
            set => _AccessKeyId = Normalize(value);
        }

        /// <summary>
        /// Optional embedding dimensionality hint (embedding endpoints). Clamped to non-negative.
        /// </summary>
        public int Dimensionality
        {
            get => _Dimensionality;
            set => _Dimensionality = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Per-request timeout in milliseconds. Clamped to [1000, 600000]; defaults to 120000.
        /// </summary>
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = value < 1000 ? 1000 : (value > 600000 ? 600000 : value);
        }

        /// <summary>
        /// Whether the endpoint is enabled for use and monitoring.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// API key/credential for the endpoint. Accepted on create/update, stored, and used for calls, but
        /// never serialized in read responses.
        /// </summary>
        [JsonIgnore]
        public string? ApiKey
        {
            get => _ApiKey;
            set
            {
                _ApiKey = value;
                _HasApiKey = null;
            }
        }

        /// <summary>
        /// Whether an API key is configured for this endpoint. Serialized in read responses; the key itself
        /// is not.
        /// </summary>
        [JsonInclude]
        public bool HasApiKey
        {
            get => _HasApiKey ?? !String.IsNullOrWhiteSpace(ApiKey);
            private set => _HasApiKey = value;
        }

        /// <summary>
        /// Write-only JSON input for the API key. Lets create/update supply the key without Armada ever
        /// returning it.
        /// </summary>
        [JsonPropertyName("apiKey")]
        public string? ApiKeyInput
        {
            set
            {
                ApiKeySpecified = true;
                ApiKey = value;
            }
        }

        /// <summary>
        /// Whether the API key was explicitly supplied during JSON deserialization.
        /// </summary>
        [JsonIgnore]
        public bool ApiKeySpecified { get; private set; } = false;

        /// <summary>
        /// Result of the most recent health check for this endpoint's base URL.
        /// </summary>
        public EndpointHealthStatusEnum HealthStatus { get; set; } = EndpointHealthStatusEnum.Unknown;

        /// <summary>
        /// When this endpoint's base URL was last health-checked (UTC), or null if never.
        /// </summary>
        public DateTime? LastHealthCheckUtc { get; set; } = null;

        /// <summary>
        /// Human-readable reason the last health check failed, or null when healthy/unknown.
        /// </summary>
        public string? LastHealthError { get; set; } = null;

        /// <summary>
        /// Latency in milliseconds observed by the last successful health check, or null.
        /// </summary>
        public long? LastLatencyMs { get; set; } = null;

        /// <summary>
        /// Rolling series of recent health-check probes (oldest first), capped in size. Drives the
        /// dashboard health-history bar.
        /// </summary>
        public List<ModelEndpointHealthRecord> HealthHistory
        {
            get => _HealthHistory;
            set => _HealthHistory = value ?? new List<ModelEndpointHealthRecord>();
        }

        /// <summary>
        /// Percentage of retained probes that succeeded (0-100). Derived from <see cref="HealthHistory"/>.
        /// </summary>
        [JsonInclude]
        public double UptimePercentage
        {
            get
            {
                if (_HealthHistory.Count == 0) return 0.0;
                int successes = 0;
                foreach (ModelEndpointHealthRecord record in _HealthHistory)
                    if (record.Success) successes++;
                return Math.Round((double)successes / _HealthHistory.Count * 100.0, 2);
            }
        }

        /// <summary>
        /// Number of consecutive successful probes at the tail of the history. Derived.
        /// </summary>
        [JsonInclude]
        public int ConsecutiveSuccesses
        {
            get
            {
                int count = 0;
                for (int i = _HealthHistory.Count - 1; i >= 0; i--)
                {
                    if (!_HealthHistory[i].Success) break;
                    count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Number of consecutive failed probes at the tail of the history. Derived.
        /// </summary>
        [JsonInclude]
        public int ConsecutiveFailures
        {
            get
            {
                int count = 0;
                for (int i = _HealthHistory.Count - 1; i >= 0; i--)
                {
                    if (_HealthHistory[i].Success) break;
                    count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Timestamp of the earliest retained probe, or null when no history exists. Derived.
        /// </summary>
        [JsonInclude]
        public DateTime? FirstHealthCheckUtc
        {
            get => _HealthHistory.Count == 0 ? (DateTime?)null : _HealthHistory[0].TimestampUtc;
        }

        /// <summary>
        /// Timestamp of the most recent successful probe, or null. Derived.
        /// </summary>
        [JsonInclude]
        public DateTime? LastHealthyUtc
        {
            get
            {
                for (int i = _HealthHistory.Count - 1; i >= 0; i--)
                    if (_HealthHistory[i].Success) return _HealthHistory[i].TimestampUtc;
                return null;
            }
        }

        /// <summary>
        /// Timestamp of the most recent failed probe, or null. Derived.
        /// </summary>
        [JsonInclude]
        public DateTime? LastUnhealthyUtc
        {
            get
            {
                for (int i = _HealthHistory.Count - 1; i >= 0; i--)
                    if (!_HealthHistory[i].Success) return _HealthHistory[i].TimestampUtc;
                return null;
            }
        }

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.ModelEndpointIdPrefix, 24);
        private string _Name = "New Endpoint";
        private string _BaseUrl = String.Empty;
        private string? _ApiKey = null;
        private bool? _HasApiKey = null;
        private int _Dimensionality = 0;
        private int _TimeoutMs = 120000;
        private string? _Region = null;
        private string? _Project = null;
        private string? _ApiVersion = null;
        private string? _AccessKeyId = null;
        private List<ModelEndpointHealthRecord> _HealthHistory = new List<ModelEndpointHealthRecord>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEndpoint()
        {
        }

        #endregion

        #region Private-Methods

        private static string? Normalize(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return value.Trim();
        }

        #endregion
    }
}
