namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Summary of one named tool visible from a captain runtime.
    /// </summary>
    public class CaptainToolSummary
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable tool description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Serialized JSON input schema, when available.
        /// </summary>
        public string? InputSchemaJson { get; set; } = null;

        /// <summary>
        /// Registration origin for the tool, such as an MCP server name or the internal runtime.
        /// </summary>
        public string RegistrationSource { get; set; } = "Internal";

        /// <summary>
        /// Source kind for the tool, such as MCP server or internal runtime support.
        /// </summary>
        public CaptainToolSourceKindEnum SourceKind { get; set; } = CaptainToolSourceKindEnum.Internal;
    }
}
