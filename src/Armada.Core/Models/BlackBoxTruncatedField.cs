namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A field that was cut.
    /// </summary>
    public class BlackBoxTruncatedField
    {
        /// <summary>
        /// Field path (for example Conflict.ConflictedFiles).
        /// </summary>
        public string Field { get; set; } = "";

        /// <summary>
        /// Original size.
        /// </summary>
        public long OriginalSize { get; set; } = 0;

        /// <summary>
        /// Kept size.
        /// </summary>
        public long KeptSize { get; set; } = 0;

        /// <summary>
        /// Unit: chars, items or bytes.
        /// </summary>
        public string Unit { get; set; } = "chars";

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxTruncatedField()
        {
        }
    }
}
