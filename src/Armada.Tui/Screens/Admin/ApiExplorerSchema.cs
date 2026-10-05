namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAPI schema object (the parts the API Explorer uses to build example bodies and initial values).
    /// </summary>
    public class ApiExplorerSchema
    {
        #region Public-Members

        /// <summary>
        /// Reference ("#/components/schemas/Name"), or null.
        /// </summary>
        [JsonPropertyName("$ref")]
        public string? Ref { get; set; } = null;

        /// <summary>
        /// Type: a string, or an array of strings in OpenAPI 3.1 (kept raw).
        /// </summary>
        public object? Type { get; set; } = null;

        /// <summary>
        /// Format, or null.
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Example value (raw), or null.
        /// </summary>
        public object? Example { get; set; } = null;

        /// <summary>
        /// Default value (raw), or null.
        /// </summary>
        public object? Default { get; set; } = null;

        /// <summary>
        /// Enumerated values (raw), or null.
        /// </summary>
        public List<object?>? Enum { get; set; } = null;

        /// <summary>
        /// Object properties, or null.
        /// </summary>
        public Dictionary<string, ApiExplorerSchema>? Properties { get; set; } = null;

        /// <summary>
        /// Array item schema, or null.
        /// </summary>
        public ApiExplorerSchema? Items { get; set; } = null;

        /// <summary>
        /// All-of composition, or null.
        /// </summary>
        public List<ApiExplorerSchema>? AllOf { get; set; } = null;

        /// <summary>
        /// One-of composition, or null.
        /// </summary>
        public List<ApiExplorerSchema>? OneOf { get; set; } = null;

        /// <summary>
        /// Any-of composition, or null.
        /// </summary>
        public List<ApiExplorerSchema>? AnyOf { get; set; } = null;

        #endregion
    }
}
