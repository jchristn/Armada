namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>type</c> of an OpenAPI schema: a single name, or (OpenAPI 3.1) an array of names. Read by token type in
    /// <see cref="ApiExplorerSchemaTypesConverter"/>.
    /// </summary>
    [JsonConverter(typeof(ApiExplorerSchemaTypesConverter))]
    public class ApiExplorerSchemaTypes
    {
        #region Public-Members

        /// <summary>
        /// Type names in document order. Never null.
        /// </summary>
        public List<string> Names { get; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ApiExplorerSchemaTypes()
        {
        }

        #endregion
    }
}
