namespace Armada.Server.Mcp
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;
    /// <summary>Typed optional usage status filters.</summary>
    public class UsageStatusArgs : UsageQuery
    {
        /// <summary>Inclusive UTC start alias.</summary>
        [JsonPropertyName("from")]
        public DateTime? From { get => FromUtc; set => FromUtc = value; }
        /// <summary>Exclusive UTC end alias.</summary>
        [JsonPropertyName("to")]
        public DateTime? To { get => ToUtc; set => ToUtc = value; }
    }
}
