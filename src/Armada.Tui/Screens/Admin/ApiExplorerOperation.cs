namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// A normalized operation (the dashboard's <c>ExplorerOperation</c>): id, method, path, summary, description,
    /// first tag, merged path-level and operation parameters, and the preferred request body.
    /// </summary>
    public class ApiExplorerOperation
    {
        #region Public-Members

        /// <summary>
        /// Operation id (or "method:path").
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Lower-case method.
        /// </summary>
        public string Method { get; set; } = "get";

        /// <summary>
        /// Path template.
        /// </summary>
        public string Path { get; set; } = "/";

        /// <summary>
        /// Summary (defaults to "METHOD path").
        /// </summary>
        public string Summary { get; set; } = "";

        /// <summary>
        /// Description, or empty.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Category (first tag, or "General").
        /// </summary>
        public string Tag { get; set; } = "General";

        /// <summary>
        /// Parameters.
        /// </summary>
        public List<ApiExplorerParameterSource> Parameters { get; set; } = new List<ApiExplorerParameterSource>();

        /// <summary>
        /// True when the operation takes a request body.
        /// </summary>
        public bool HasBody { get; set; } = false;

        /// <summary>
        /// Body schema, or null.
        /// </summary>
        public ApiExplorerSchema? BodySchema { get; set; } = null;

        /// <summary>
        /// Body content type ("application/json" when offered), or empty.
        /// </summary>
        public string BodyContentType { get; set; } = "";

        #endregion
    }
}
