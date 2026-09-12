namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for updating an existing memory by id. Only supplied fields are changed.
    /// </summary>
    public class MemoryUpdateArgs
    {
        /// <summary>
        /// Memory id (mem_ prefix). Required.
        /// </summary>
        public string MemoryId { get; set; } = "";

        /// <summary>
        /// New memory type (Episodic, Semantic, Procedural).
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// New topic/grouping.
        /// </summary>
        public string? Topic { get; set; }

        /// <summary>
        /// New idempotency key.
        /// </summary>
        public string? Key { get; set; }

        /// <summary>
        /// New one-line summary.
        /// </summary>
        public string? Summary { get; set; }

        /// <summary>
        /// New content.
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// New salience in [0.0, 1.0].
        /// </summary>
        public double? Salience { get; set; }

        /// <summary>
        /// Replacement tag list. When supplied, replaces all tags.
        /// </summary>
        public List<string>? Tags { get; set; }

        /// <summary>
        /// New vessel association.
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// New source detail.
        /// </summary>
        public string? SourceDetail { get; set; }

        /// <summary>
        /// New ownership scope (admins only): TenantWide or UserSpecific.
        /// </summary>
        public string? Scope { get; set; }
    }
}
