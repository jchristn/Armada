namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for the armada_restore command.
    /// </summary>
    public class RestoreArgs
    {
        /// <summary>
        /// Path to the ZIP backup file to restore from.
        /// </summary>
        public string FilePath { get; set; } = "";
    }
}
