namespace Armada.Tui.Screens.Activity
{
    /// <summary>
    /// Copyable detail blocks of a request-history entry.
    /// </summary>
    public enum RequestHistoryBlockEnum
    {
        /// <summary>
        /// Path parameters.
        /// </summary>
        PathParameters,

        /// <summary>
        /// Query parameters.
        /// </summary>
        QueryParameters,

        /// <summary>
        /// Request headers.
        /// </summary>
        RequestHeaders,

        /// <summary>
        /// Response headers.
        /// </summary>
        ResponseHeaders,

        /// <summary>
        /// Request body.
        /// </summary>
        RequestBody,

        /// <summary>
        /// Response body.
        /// </summary>
        ResponseBody
    }
}
