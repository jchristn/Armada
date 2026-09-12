namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments for registering or updating a Harbor via MCP. Only operator-editable fields are exposed;
    /// runtime state (capabilities, connection status) is managed by the Harbor link.
    /// </summary>
    public class HarborUpsertArgs
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier (hbr_ prefix). Required for update, ignored for create.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Human-facing Harbor name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Maximum concurrent jobs the Harbor will accept.
        /// </summary>
        public int? MaxConcurrentJobs { get; set; } = null;

        /// <summary>
        /// Whether the Harbor is enabled for routing.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborUpsertArgs()
        {
        }

        #endregion
    }
}
