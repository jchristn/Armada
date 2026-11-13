namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of read_blackbox_section.
    /// </summary>
    public class BlackBoxSectionArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Record section name (for example Workspace or Conflict).
        /// </summary>
        public string? Section { get; set; } = null;
    }
}
