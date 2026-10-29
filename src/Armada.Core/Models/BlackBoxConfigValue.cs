namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One effective configuration value and where it came from.
    /// </summary>
    public class BlackBoxConfigValue
    {
        /// <summary>
        /// Setting name (for example LandingMode).
        /// </summary>
        public string Setting { get; set; } = "";

        /// <summary>
        /// Value as text (redacted when secret), or null when unset.
        /// </summary>
        public string? Value { get; set; } = null;

        /// <summary>
        /// Where the value came from.
        /// </summary>
        public BlackBoxConfigSourceEnum Source { get; set; } = BlackBoxConfigSourceEnum.Default;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxConfigValue()
        {
        }
    }
}
