namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// A JSON schema node of an MCP tool input schema, read for its descriptions: the node's description, its object
    /// properties, and its array item schema.
    /// </summary>
    public sealed class McpSchemaNode
    {
        #region Public-Members

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Object properties, or null.
        /// </summary>
        public Dictionary<string, McpSchemaNode>? Properties { get; set; } = null;

        /// <summary>
        /// Array item schema, or null.
        /// </summary>
        public McpSchemaNode? Items { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add this node's description and every nested description to a list, each prefixed with its path.
        /// </summary>
        /// <param name="path">Path of this node.</param>
        /// <param name="texts">Destination list.</param>
        public void CollectDescriptions(string path, List<string> texts)
        {
            if (!string.IsNullOrEmpty(Description)) texts.Add(path + ": " + Description);
            if (Properties != null)
            {
                foreach (KeyValuePair<string, McpSchemaNode> property in Properties)
                    property.Value?.CollectDescriptions(path + "." + property.Key, texts);
            }

            Items?.CollectDescriptions(path + "[]", texts);
        }

        #endregion
    }
}
