namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// A scripted HTTP response for <see cref="StubExpoHandler"/>.
    /// </summary>
    public sealed class StubExpoResponse
    {
        #region Public-Members

        /// <summary>Status code.</summary>
        public int StatusCode { get; }

        /// <summary>Body.</summary>
        public string Body { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="statusCode">Status code.</param>
        /// <param name="body">Body.</param>
        public StubExpoResponse(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? String.Empty;
        }

        #endregion
    }
}
