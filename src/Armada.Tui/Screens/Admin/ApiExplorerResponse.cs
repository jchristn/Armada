namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// A response received by the explorer (the dashboard's <c>ExplorerResponse</c>).
    /// </summary>
    public class ApiExplorerResponse
    {
        #region Public-Members

        /// <summary>
        /// True for a 2xx status.
        /// </summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// Status code.
        /// </summary>
        public int Status { get; set; } = 0;

        /// <summary>
        /// Reason phrase.
        /// </summary>
        public string StatusText { get; set; } = "";

        /// <summary>
        /// Round-trip duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Response headers (lower-case names).
        /// </summary>
        public List<KeyValuePair<string, string>> Headers { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Content type, or empty.
        /// </summary>
        public string ContentType { get; set; } = "";

        /// <summary>
        /// Body text (or a binary summary).
        /// </summary>
        public string Body { get; set; } = "";

        /// <summary>
        /// Body size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// The request that produced it.
        /// </summary>
        public ApiExplorerRequestPreview Request { get; set; } = new ApiExplorerRequestPreview();

        #endregion
    }
}
