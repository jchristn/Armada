namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// OpenAPI components (schemas only).
    /// </summary>
    public class ApiExplorerComponents
    {
        #region Public-Members

        /// <summary>
        /// Named schemas, or null.
        /// </summary>
        public Dictionary<string, ApiExplorerSchema>? Schemas { get; set; } = null;

        #endregion
    }
}
