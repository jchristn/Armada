namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of list_model_alerts.
    /// </summary>
    public class ModelAlertListArgs
    {
        /// <summary>
        /// Open or Resolved, or null for both.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Model profile ID (mdl_ prefix), or null.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Persona name, or null.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Whether alerts hidden by Ignore are listed (default true).
        /// </summary>
        public bool? IncludeIgnored { get; set; } = null;

        /// <summary>
        /// Page number (default 1).
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size (1-1000, default 25).
        /// </summary>
        public int? PageSize { get; set; } = null;
    }
}
