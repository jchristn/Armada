namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// The id and length-hint fields of an object in an MCP enumerate result when the heavy text fields are left out
    /// (includeDescription / includeContext not set).
    /// </summary>
    public class McpLengthHints
    {
        #region Public-Members

        /// <summary>
        /// Entity id.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Length of the omitted Description, or null when not reported.
        /// </summary>
        public int? DescriptionLength { get; set; } = null;

        /// <summary>
        /// Length of the omitted ProjectContext, or null when not reported.
        /// </summary>
        public int? ProjectContextLength { get; set; } = null;

        /// <summary>
        /// Length of the omitted StyleGuide, or null when not reported.
        /// </summary>
        public int? StyleGuideLength { get; set; } = null;

        #endregion
    }
}
