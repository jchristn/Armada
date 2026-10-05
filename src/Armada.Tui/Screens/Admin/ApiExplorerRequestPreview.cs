namespace Armada.Tui.Screens.Admin
{
    using System.Collections.Generic;

    /// <summary>
    /// The request the explorer will send (the dashboard's <c>ExplorerRequestPreview</c>).
    /// </summary>
    public class ApiExplorerRequestPreview
    {
        #region Public-Members

        /// <summary>
        /// Lower-case method.
        /// </summary>
        public string Method { get; set; } = "get";

        /// <summary>
        /// Absolute URL.
        /// </summary>
        public string Url { get; set; } = "";

        /// <summary>
        /// Path and query relative to the server.
        /// </summary>
        public string PathAndQuery { get; set; } = "/";

        /// <summary>
        /// Headers in order (auth first).
        /// </summary>
        public List<KeyValuePair<string, string>> Headers { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Body, or empty.
        /// </summary>
        public string Body { get; set; } = "";

        /// <summary>
        /// Body content type.
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        #endregion
    }
}
