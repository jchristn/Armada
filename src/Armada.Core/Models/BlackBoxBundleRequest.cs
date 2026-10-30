namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Build a maintainer bundle of up to 50 entries grouped by fingerprint.
    /// </summary>
    public class BlackBoxBundleRequest : BlackBoxEnumerateRequest
    {
        /// <summary>
        /// Most entries (1 to 50).
        /// </summary>
        public int Max { get; set; } = 50;

        /// <summary>
        /// Markdown or JSON.
        /// </summary>
        public BlackBoxBundleFormatEnum Format { get; set; } = BlackBoxBundleFormatEnum.Markdown;

        /// <summary>
        /// Strip absolute paths, host names and user names (for sharing outside the installation).
        /// </summary>
        public bool RedactHosts { get; set; } = false;

        /// <summary>
        /// Bundle every failure. When false (the default) and neither Triage nor ResilienceScope is set, the bundle holds
        /// the Armada resilience gaps: entries in handoff and landing scope triaged ArmadaDefect.
        /// </summary>
        public bool AllFailures { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBundleRequest()
        {
        }
    }
}
