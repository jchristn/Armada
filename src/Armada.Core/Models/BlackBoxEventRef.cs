namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// An event referenced by a record.
    /// </summary>
    public class BlackBoxEventRef
    {
        /// <summary>
        /// Event identifier.
        /// </summary>
        public string? EventId { get; set; } = null;

        /// <summary>
        /// Event type.
        /// </summary>
        public string EventType { get; set; } = "";

        /// <summary>
        /// Message (at most 500 characters).
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// When (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEventRef()
        {
        }
    }
}
