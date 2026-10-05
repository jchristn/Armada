namespace Armada.Helm.Infrastructure
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;

    /// <summary>
    /// Builds the exception Helm commands throw for a non-success Admiral response. The exception carries the
    /// response status in <see cref="HttpRequestException.StatusCode"/>, so callers filter on the status code rather
    /// than on the message text.
    /// </summary>
    public static class HelmHttpError
    {
        #region Public-Methods

        /// <summary>
        /// Create the exception for a non-success response, with the response body in the message.
        /// </summary>
        /// <param name="response">Non-success response.</param>
        /// <param name="description">Request description for the message, for example "GET /api/v1/fleets".</param>
        /// <returns>Exception with <see cref="HttpRequestException.StatusCode"/> set.</returns>
        public static async Task<HttpRequestException> FromResponseAsync(HttpResponseMessage response, string description)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            string body = response.Content == null
                ? String.Empty
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new HttpRequestException(
                "HTTP " + (int)response.StatusCode + " on " + (description ?? String.Empty) + ": " + body,
                null,
                response.StatusCode);
        }

        #endregion
    }
}
