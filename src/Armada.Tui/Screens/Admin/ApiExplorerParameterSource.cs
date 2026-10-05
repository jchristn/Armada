namespace Armada.Tui.Screens.Admin
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAPI parameter object.
    /// </summary>
    public class ApiExplorerParameterSource
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Location: path, query, header, or cookie.
        /// </summary>
        [JsonPropertyName("in")]
        public string In { get; set; } = "";

        /// <summary>
        /// Required.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Example (raw), or null.
        /// </summary>
        public ApiExplorerJsonValue? Example { get; set; } = null;

        /// <summary>
        /// Schema, or null.
        /// </summary>
        public ApiExplorerSchema? Schema { get; set; } = null;

        #endregion
    }
}
