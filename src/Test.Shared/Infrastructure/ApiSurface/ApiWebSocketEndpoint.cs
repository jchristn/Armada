namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One WebSocket endpoint.
    /// </summary>
    public sealed class ApiWebSocketEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Surface name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Path.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Whether the endpoint is experimental.
        /// </summary>
        public bool Experimental { get; set; } = false;

        #endregion
    }
}
