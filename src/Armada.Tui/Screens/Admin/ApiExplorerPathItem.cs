namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// An OpenAPI path item: one operation per HTTP method plus path-level parameters.
    /// </summary>
    public class ApiExplorerPathItem
    {
        #region Public-Members

        /// <summary>
        /// GET operation, or null.
        /// </summary>
        public ApiExplorerOperationSource? Get { get; set; } = null;

        /// <summary>
        /// POST operation, or null.
        /// </summary>
        public ApiExplorerOperationSource? Post { get; set; } = null;

        /// <summary>
        /// PUT operation, or null.
        /// </summary>
        public ApiExplorerOperationSource? Put { get; set; } = null;

        /// <summary>
        /// DELETE operation, or null.
        /// </summary>
        public ApiExplorerOperationSource? Delete { get; set; } = null;

        /// <summary>
        /// PATCH operation, or null.
        /// </summary>
        public ApiExplorerOperationSource? Patch { get; set; } = null;

        /// <summary>
        /// HEAD operation, or null.
        /// </summary>
        public ApiExplorerOperationSource? Head { get; set; } = null;

        /// <summary>
        /// Path-level parameters, or null.
        /// </summary>
        public List<ApiExplorerParameterSource>? Parameters { get; set; } = null;

        #endregion
    }
}
