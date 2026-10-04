namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One CLI command in the API surface.
    /// </summary>
    public sealed class ApiCliCommand
    {
        #region Public-Members

        /// <summary>
        /// Command path, for example mission list.
        /// </summary>
        public string Command { get; set; } = "";

        /// <summary>
        /// Command description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Arguments and options.
        /// </summary>
        public List<ApiCliParameter> Parameters { get; set; } = new List<ApiCliParameter>();

        #endregion
    }
}
