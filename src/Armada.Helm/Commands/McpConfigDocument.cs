namespace Armada.Helm.Commands
{
    using System.Text.Json.Nodes;

    /// <summary>
    /// An MCP client config file as read for an install or remove: its root object and whether the text used JSONC
    /// syntax (comments or trailing commas) that a rewrite cannot keep.
    /// </summary>
    internal sealed class McpConfigDocument
    {
        /// <summary>
        /// Root object. A missing or empty file reads as an empty object.
        /// </summary>
        internal JsonObject Root { get; }

        /// <summary>
        /// True when the file parsed only with comments and trailing commas allowed, so rewriting it drops them.
        /// </summary>
        internal bool HasJsoncSyntax { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="root">Root object.</param>
        /// <param name="hasJsoncSyntax">Whether the file used comments or trailing commas.</param>
        /// <exception cref="ArgumentNullException">root is null.</exception>
        internal McpConfigDocument(JsonObject root, bool hasJsoncSyntax)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            HasJsoncSyntax = hasJsoncSyntax;
        }
    }
}
