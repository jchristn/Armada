namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// The OpenAPI document as the API Explorer reads it (paths and component schemas).
    /// </summary>
    public class ApiExplorerDocument
    {
        #region Public-Members

        /// <summary>
        /// Paths, or null.
        /// </summary>
        public Dictionary<string, ApiExplorerPathItem>? Paths { get; set; } = null;

        /// <summary>
        /// Components, or null.
        /// </summary>
        public ApiExplorerComponents? Components { get; set; } = null;

        #endregion
    }
}
