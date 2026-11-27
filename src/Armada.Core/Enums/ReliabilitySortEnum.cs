namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Sort field of the reliability summary (GET /api/v1/reliability/summary).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReliabilitySortEnum
    {
        /// <summary>Attempts, most first (default).</summary>
        Attempts,

        /// <summary>Completion rate.</summary>
        CompletionRate,

        /// <summary>Unclassified rate.</summary>
        UnclassifiedRate,

        /// <summary>Repairs.</summary>
        Repairs,

        /// <summary>Escalations.</summary>
        Escalations,

        /// <summary>Failures.</summary>
        Failures,

        /// <summary>Model name.</summary>
        Model,

        /// <summary>Persona name.</summary>
        Persona
    }
}
