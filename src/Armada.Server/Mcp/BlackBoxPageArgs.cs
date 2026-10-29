namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of the black box tools that page one entry's children (list_blackbox_objects, list_blackbox_notes).
    /// </summary>
    public class BlackBoxPageArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Page number (default 1).
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size (1 to 1000, default 10).
        /// </summary>
        public int? PageSize { get; set; } = null;

        /// <summary>
        /// CreatedDescending (default) or CreatedAscending.
        /// </summary>
        public string? Order { get; set; } = null;
    }
}
