namespace Armada.Tui.Screens.Admin
{
    /// <summary>
    /// An OpenAPI media type object (request body content entry).
    /// </summary>
    public class ApiExplorerMediaType
    {
        #region Public-Members

        /// <summary>
        /// Schema, or null.
        /// </summary>
        public ApiExplorerSchema? Schema { get; set; } = null;

        #endregion
    }
}
