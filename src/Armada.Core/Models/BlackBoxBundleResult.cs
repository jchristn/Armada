namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A maintainer bundle.
    /// </summary>
    public class BlackBoxBundleResult
    {
        /// <summary>
        /// Format.
        /// </summary>
        public BlackBoxBundleFormatEnum Format { get; set; } = BlackBoxBundleFormatEnum.Markdown;

        /// <summary>
        /// Entries included.
        /// </summary>
        public int EntryCount { get; set; } = 0;

        /// <summary>
        /// Distinct fingerprints.
        /// </summary>
        public int FingerprintCount { get; set; } = 0;

        /// <summary>
        /// Bundle text (Markdown or JSON).
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Whether the bundle was cut to its size cap.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBundleResult()
        {
        }
    }
}
