namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One REST route in the API surface.
    /// </summary>
    public sealed class ApiRestRoute
    {
        #region Public-Members

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = "";

        /// <summary>
        /// Route template.
        /// </summary>
        public string Route { get; set; } = "";

        /// <summary>
        /// Required permission level (NoAuthRequired, Authenticated, TenantAdmin, AdminOnly).
        /// </summary>
        public string Auth { get; set; } = "";

        /// <summary>
        /// Declared resource and operation, for example Fleet:Read.
        /// </summary>
        public string Resource { get; set; } = "";

        /// <summary>
        /// Request body type name, or null.
        /// </summary>
        public string? RequestType { get; set; } = null;

        /// <summary>
        /// Whether the request body is required.
        /// </summary>
        public bool RequestRequired { get; set; } = false;

        /// <summary>
        /// Response type names by status code.
        /// </summary>
        public SortedDictionary<string, string> Responses { get; set; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// OpenAPI tags.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// OpenAPI summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Whether the route is experimental (excluded from the compatibility promise).
        /// </summary>
        public bool Experimental { get; set; } = false;

        #endregion
    }
}
