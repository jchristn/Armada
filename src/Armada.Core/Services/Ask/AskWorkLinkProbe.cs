namespace Armada.Core.Services.Ask
{
    /// <summary>
    /// The identifier-bearing fields of MCP tool arguments and results that work linking reads. Deserialized
    /// case-insensitively from the JSON text of a tool call; unknown fields are ignored.
    /// </summary>
    public class AskWorkLinkProbe
    {
        #region Public-Members

        /// <summary>
        /// Generic identifier (the created entity of most tools).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Mission identifier.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Fleet action run identifier.
        /// </summary>
        public string? RunId { get; set; } = null;

        /// <summary>
        /// Background job identifier.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// Vessel import batch identifier.
        /// </summary>
        public string? BatchId { get; set; } = null;

        /// <summary>
        /// Nested batch (discover_vessels result).
        /// </summary>
        public AskWorkLinkRef? Batch { get; set; } = null;

        /// <summary>
        /// Error text when the tool reported a failure.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
