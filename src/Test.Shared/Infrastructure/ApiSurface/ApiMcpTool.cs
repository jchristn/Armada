namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One MCP tool in the API surface.
    /// </summary>
    public sealed class ApiMcpTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Required permission level.
        /// </summary>
        public string Auth { get; set; } = "";

        /// <summary>
        /// Declared resource and operation.
        /// </summary>
        public string Resource { get; set; } = "";

        /// <summary>
        /// Arguments (top-level input schema properties).
        /// </summary>
        public List<ApiMcpArgument> Arguments { get; set; } = new List<ApiMcpArgument>();

        /// <summary>
        /// Tool description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether the tool is experimental.
        /// </summary>
        public bool Experimental { get; set; } = false;

        #endregion
    }
}
