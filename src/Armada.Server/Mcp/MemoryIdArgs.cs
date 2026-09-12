namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments identifying a single memory by id.
    /// </summary>
    public class MemoryIdArgs
    {
        /// <summary>
        /// Memory id (mem_ prefix). Required.
        /// </summary>
        public string MemoryId { get; set; } = "";
    }
}
