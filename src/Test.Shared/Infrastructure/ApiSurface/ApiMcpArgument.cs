namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One MCP tool argument.
    /// </summary>
    public sealed class ApiMcpArgument
    {
        #region Public-Members

        /// <summary>
        /// Argument name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// JSON schema type (array element types as array<T>).
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Whether the argument is required.
        /// </summary>
        public bool Required { get; set; } = false;

        #endregion
    }
}
