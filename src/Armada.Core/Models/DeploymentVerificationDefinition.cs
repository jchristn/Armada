namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Reusable HTTP verification definition for post-deploy and rollout-window checks.
    /// </summary>
    public class DeploymentVerificationDefinition
    {
        /// <summary>
        /// Stable identifier for the definition.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value.Trim();
            }
        }

        /// <summary>
        /// Human-facing name.
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
        /// HTTP method such as GET or POST.
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Relative path or absolute URL to request.
        /// </summary>
        public string Path
        {
            get => _Path;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Path));
                _Path = value.Trim();
            }
        }

        /// <summary>
        /// Optional request body.
        /// </summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>
        /// Optional request headers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Optional explicit expected status code.
        /// </summary>
        public int? ExpectedStatusCode { get; set; } = 200;

        /// <summary>
        /// Optional body substring that must appear in the response.
        /// </summary>
        public string? MustContainText { get; set; } = null;

        /// <summary>
        /// Whether this definition is active.
        /// </summary>
        public bool Active { get; set; } = true;

        private string _Id = Armada.Core.Constants.IdGenerator.GenerateKSortable("dvd_", 24);
        private string _Name = "Verification";
        private string _Path = "/";
    }
}
