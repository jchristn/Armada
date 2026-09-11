namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for creating or upserting a durable memory. When a stable <see cref="Key"/> is
    /// supplied and a memory with that key already exists in the caller's tenant, it is updated in place.
    /// </summary>
    public class MemoryUpsertArgs
    {
        /// <summary>
        /// Memory type: Episodic, Semantic, or Procedural. Defaults to Semantic when omitted or invalid.
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Agent-chosen grouping within the type (the sub-structure), e.g. "code-style".
        /// </summary>
        public string? Topic { get; set; }

        /// <summary>
        /// Stable idempotency key (slug). When set, re-upserting the same key updates the memory in place.
        /// </summary>
        public string? Key { get; set; }

        /// <summary>
        /// One-line recall hook shown in listings and search.
        /// </summary>
        public string? Summary { get; set; }

        /// <summary>
        /// The memory content. Required.
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Importance in [0.0, 1.0]; higher recalls first. Defaults to 0.5 when omitted.
        /// </summary>
        public double? Salience { get; set; }

        /// <summary>
        /// Free-form tags for retrieval and consolidation.
        /// </summary>
        public List<string>? Tags { get; set; }

        /// <summary>
        /// Where the memory came from: Voyage, Mission, Vessel, Conversation, Manual, or Other.
        /// </summary>
        public string? SourceKind { get; set; }

        /// <summary>
        /// Originating voyage id (provenance).
        /// </summary>
        public string? SourceVoyageId { get; set; }

        /// <summary>
        /// Originating mission id (provenance).
        /// </summary>
        public string? SourceMissionId { get; set; }

        /// <summary>
        /// Originating vessel id (provenance).
        /// </summary>
        public string? SourceVesselId { get; set; }

        /// <summary>
        /// Free-text note describing where the memory came from.
        /// </summary>
        public string? SourceDetail { get; set; }

        /// <summary>
        /// Vessel (repository) this memory is about, so it can be recalled per-repo.
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// Ownership scope: TenantWide or UserSpecific. Admins may choose; regular users are forced to
        /// UserSpecific.
        /// </summary>
        public string? Scope { get; set; }
    }
}
