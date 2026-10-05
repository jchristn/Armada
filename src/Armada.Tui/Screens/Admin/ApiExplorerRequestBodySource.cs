namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// An OpenAPI request body object.
    /// </summary>
    public class ApiExplorerRequestBodySource
    {
        #region Public-Members

        /// <summary>
        /// Content by media type, or null.
        /// </summary>
        public Dictionary<string, ApiExplorerMediaType>? Content { get; set; } = null;

        #endregion
    }
}
