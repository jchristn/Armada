namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A canned reply of <see cref="RecordingAdmiralStub"/>.
    /// </summary>
    public sealed class StubAdmiralReply
    {
        #region Public-Members

        /// <summary>
        /// HTTP status.
        /// </summary>
        public int Status { get; set; } = 200;

        /// <summary>
        /// Body text.
        /// </summary>
        public string Body { get; set; } = "";

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        /// <summary>
        /// Extra response headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// A JSON reply.
        /// </summary>
        /// <param name="body">JSON.</param>
        /// <param name="status">Status.</param>
        /// <returns>Reply.</returns>
        public static StubAdmiralReply Json(string body, int status = 200)
        {
            return new StubAdmiralReply { Body = body ?? "", Status = status };
        }

        /// <summary>
        /// A text reply.
        /// </summary>
        /// <param name="body">Text.</param>
        /// <param name="contentType">Content type.</param>
        /// <returns>Reply.</returns>
        public static StubAdmiralReply Text(string body, string contentType = "text/markdown; charset=utf-8")
        {
            return new StubAdmiralReply { Body = body ?? "", ContentType = contentType };
        }

        #endregion
    }
}
