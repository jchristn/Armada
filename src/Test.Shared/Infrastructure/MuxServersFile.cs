namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The Mux mcp-servers.json file as written by ArmadaMcpConfigBuilder.
    /// </summary>
    public class MuxServersFile
    {
        #region Public-Members

        /// <summary>
        /// Servers, in order.
        /// </summary>
        [JsonPropertyName("servers")]
        public List<MuxServerEntry>? Servers { get; set; } = null;

        #endregion
    }
}
