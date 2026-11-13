namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// Ids and data seeded by the black box client contract suite: a failed mission and voyage with their entries, a
    /// search marker and the bytes of a seeded evidence object.
    /// </summary>
    public sealed class BlackBoxSeed
    {
        /// <summary>Mission id.</summary>
        public string MissionId { get; set; } = "";

        /// <summary>Voyage id.</summary>
        public string VoyageId { get; set; } = "";

        /// <summary>The mission's entry id.</summary>
        public string EntryId { get; set; } = "";

        /// <summary>The voyage's entry id.</summary>
        public string VoyageEntryId { get; set; } = "";

        /// <summary>Text in the reason, for search.</summary>
        public string Marker { get; set; } = "";

        /// <summary>Bytes of evidence/contract.log.</summary>
        public byte[] Log { get; set; } = Array.Empty<byte>();
    }
}
