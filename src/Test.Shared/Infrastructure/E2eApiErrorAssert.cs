namespace Test.Shared.Infrastructure
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;

    /// <summary>
    /// Asserts that a REST response is a specific typed error: the exact HTTP status and the matching
    /// <see cref="ApiResultEnum"/> in the body. Replaces checks such as "status is not 200 or the body mentions Error"
    /// and "the body has an Error or a Message", which also pass for the wrong failure.
    /// </summary>
    public static class E2eApiErrorAssert
    {
        #region Public-Methods

        /// <summary>
        /// Assert the response has this status and an error body whose code matches the status
        /// (400 BadRequest, 401 NotAuthorized, 403 Forbidden, 404 NotFound, 409 Conflict).
        /// </summary>
        /// <param name="response">Response.</param>
        /// <param name="status">Expected status.</param>
        /// <param name="label">Failure label.</param>
        /// <returns>The deserialized error.</returns>
        public static Task<ApiErrorProbe> ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string label)
        {
            return ExpectAsync(response, status, CodeFor(status), label);
        }

        /// <summary>
        /// Assert the response has this status and this error code in the body.
        /// </summary>
        /// <param name="response">Response.</param>
        /// <param name="status">Expected status.</param>
        /// <param name="error">Expected error code.</param>
        /// <param name="label">Failure label.</param>
        /// <returns>The deserialized error.</returns>
        public static async Task<ApiErrorProbe> ExpectAsync(HttpResponseMessage response, HttpStatusCode status, ApiResultEnum error, string label)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode != status)
                throw new AssertionException(label + ": expected HTTP " + (int)status + " but got " + (int)response.StatusCode + ": " + body);
            ApiErrorProbe probe = ApiErrorProbe.From(body);
            if (probe.Error != error)
                throw new AssertionException(label + ": expected error code " + error + " but got " + (probe.Error?.ToString() ?? "none") + ": " + body);
            return probe;
        }

        #endregion

        #region Private-Methods

        private static ApiResultEnum CodeFor(HttpStatusCode status)
        {
            switch (status)
            {
                case HttpStatusCode.BadRequest: return ApiResultEnum.BadRequest;
                case HttpStatusCode.Unauthorized: return ApiResultEnum.NotAuthorized;
                case HttpStatusCode.Forbidden: return ApiResultEnum.Forbidden;
                case HttpStatusCode.NotFound: return ApiResultEnum.NotFound;
                case HttpStatusCode.Conflict: return ApiResultEnum.Conflict;
                default: throw new ArgumentException("No default error code for HTTP " + (int)status + "; pass the code explicitly.", nameof(status));
            }
        }

        #endregion
    }
}
