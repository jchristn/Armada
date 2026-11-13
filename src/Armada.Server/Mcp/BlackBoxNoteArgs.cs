namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of annotate_blackbox.
    /// </summary>
    public class BlackBoxNoteArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Note text (1 to 8000 characters).
        /// </summary>
        public string? Body { get; set; } = null;
    }
}
