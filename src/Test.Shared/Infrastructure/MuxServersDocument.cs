namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// Typed view of a Mux mcp-servers.json document.
    /// </summary>
    public sealed class MuxServersDocument
    {
        /// <summary>
        /// Configured servers.
        /// </summary>
        public List<MuxServerEntry>? Servers { get; set; } = null;
    }
}
