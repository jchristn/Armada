namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System;
    using Armada.Core.Services;

    /// <summary>
    /// The allowlisted rate-limit response headers one inference response carried, captured by
    /// <see cref="Armada.Core.Services.UsageRateLimitHandler"/> as soon as the response headers arrived and
    /// before any body was read. Only the rate-limit allowlist is kept: no request header, no credential,
    /// no body and no other response header ever reaches this object.
    /// </summary>
    public class UsageHeaderObservation
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code of the response the headers came from.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// UTC time the response headers were observed. Relative reset values (durations, Retry-After
        /// seconds) are converted from this time, never from a later polling time.
        /// </summary>
        public DateTime ObservedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Host name of the request, without path, query or credentials. Null when unknown.
        /// </summary>
        public string? RequestHost { get; set; } = null;

        /// <summary>
        /// Allowlisted headers keyed by lower-case name. Never null.
        /// </summary>
        [CollectionBound(32)]
        public Dictionary<string, string> Headers
        {
            get => _Headers;
            set => _Headers = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty observation.
        /// </summary>
        public UsageHeaderObservation()
        {
        }

        #endregion
    }
}
