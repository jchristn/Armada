namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// An OpenAPI operation object as found in the document.
    /// </summary>
    public class ApiExplorerOperationSource
    {
        #region Public-Members

        /// <summary>
        /// Operation id, or null.
        /// </summary>
        public string? OperationId { get; set; } = null;

        /// <summary>
        /// Summary, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Tags, or null.
        /// </summary>
        public List<string>? Tags { get; set; } = null;

        /// <summary>
        /// Parameters, or null.
        /// </summary>
        public List<ApiExplorerParameterSource>? Parameters { get; set; } = null;

        /// <summary>
        /// Request body, or null.
        /// </summary>
        public ApiExplorerRequestBodySource? RequestBody { get; set; } = null;

        #endregion
    }
}
