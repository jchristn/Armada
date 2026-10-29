namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of read_blackbox_object.
    /// </summary>
    public class BlackBoxObjectReadArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Object name (for example evidence/git-status.txt).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Byte offset (default 0).
        /// </summary>
        public long? Offset { get; set; } = null;

        /// <summary>
        /// Bytes to read (1 to 65536, default 65536).
        /// </summary>
        public int? Length { get; set; } = null;
    }
}
