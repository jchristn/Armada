namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of list_model_profiles.
    /// </summary>
    public class ModelProfileListArgs
    {
        /// <summary>
        /// Runtime filter (case-insensitive), or null.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Provider filter (case-insensitive), or null.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Only models whose name contains this text, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Page number, or null for 1.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size, or null for 25.
        /// </summary>
        public int? PageSize { get; set; } = null;
    }
}
