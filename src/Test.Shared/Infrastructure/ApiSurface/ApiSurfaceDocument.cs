namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The public API surface of Armada at one version (docs/api-surface-1.0.json): REST routes, MCP tools, WebSocket contract, CLI commands, and settings keys.
    /// </summary>
    public sealed class ApiSurfaceDocument
    {
        #region Public-Members

        /// <summary>
        /// Version of the compatibility promise this surface describes.
        /// </summary>
        public string SurfaceVersion { get; set; } = "1.0";

        /// <summary>
        /// How the file is regenerated.
        /// </summary>
        public string Generator { get; set; } = "scripts/common/generate-api-surface.sh";

        /// <summary>
        /// REST routes.
        /// </summary>
        public List<ApiRestRoute> Rest { get; set; } = new List<ApiRestRoute>();

        /// <summary>
        /// MCP tools.
        /// </summary>
        public List<ApiMcpTool> Mcp { get; set; } = new List<ApiMcpTool>();

        /// <summary>
        /// WebSocket contract.
        /// </summary>
        public ApiWebSocketSurface WebSocket { get; set; } = new ApiWebSocketSurface();

        /// <summary>
        /// CLI commands (leaf commands only).
        /// </summary>
        public List<ApiCliCommand> Cli { get; set; } = new List<ApiCliCommand>();

        /// <summary>
        /// Settings keys (settings.json, camelCase dotted paths).
        /// </summary>
        public List<ApiSettingKey> Settings { get; set; } = new List<ApiSettingKey>();

        #endregion
    }
}
