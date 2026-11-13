namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Processing state of a black box entry.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxStateEnum
    {
        /// <summary>
        /// Captured at the failure site; enrichment pending.
        /// </summary>
        [EnumMember(Value = "Captured")]
        Captured,

        /// <summary>
        /// Enrichment finished.
        /// </summary>
        [EnumMember(Value = "Enriched")]
        Enriched,

        /// <summary>
        /// Analysis finished.
        /// </summary>
        [EnumMember(Value = "Analyzed")]
        Analyzed,

        /// <summary>
        /// Enrichment failed; the sweeper retries with backoff.
        /// </summary>
        [EnumMember(Value = "EnrichmentFailed")]
        EnrichmentFailed,

        /// <summary>
        /// Being deleted (two-phase delete); hidden from readers.
        /// </summary>
        [EnumMember(Value = "Deleting")]
        Deleting,

        /// <summary>
        /// One or more objects are missing from the store.
        /// </summary>
        [EnumMember(Value = "ObjectMissing")]
        ObjectMissing,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
