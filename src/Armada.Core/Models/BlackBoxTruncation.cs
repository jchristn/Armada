namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Truncation report: what was cut and why. Nothing is dropped silently.
    /// </summary>
    public class BlackBoxTruncation
    {
        /// <summary>
        /// Fields that were cut.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxTruncatedFields, Reason = "Capped; FieldTotal holds the real count")]
        public List<BlackBoxTruncatedField> Fields
        {
            get => _Fields;
            set => _Fields = value ?? new List<BlackBoxTruncatedField>();
        }

        /// <summary>
        /// Total truncated fields before capping.
        /// </summary>
        public int FieldTotal { get; set; } = 0;

        /// <summary>
        /// Whether capture ran out of its time budget and wrote a partial entry.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Capture notes (timeouts, lower-fidelity capture, missing facts).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxNotes, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> Notes
        {
            get => _Notes;
            set => _Notes = value ?? new List<string>();
        }

        private List<BlackBoxTruncatedField> _Fields = new List<BlackBoxTruncatedField>();
        private List<string> _Notes = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxTruncation()
        {
        }
    }
}
