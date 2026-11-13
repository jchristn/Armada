namespace Armada.Helm.Infrastructure
{
    using System;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Client;

    /// <summary>
    /// Builds the exception Helm commands throw for a non-success Admiral response. The exception carries the
    /// response status in <see cref="HttpRequestException.StatusCode"/>, so callers filter on the status code rather
    /// than on the message text.
    /// </summary>
    public static class HelmHttpError
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create the exception for a non-success response: the server's error message when the body is the REST
        /// error envelope, otherwise the response body.
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
            string where = "HTTP " + (int)response.StatusCode + " on " + (description ?? String.Empty);
            ApiErrorEnvelope? envelope = Envelope(body);
            if (!String.IsNullOrWhiteSpace(envelope?.Message))
            {
                // The server's own explanation first; the request and stable code after it, never the raw JSON body.
                string code = String.IsNullOrWhiteSpace(envelope!.Data?.Code) ? "" : ", " + envelope.Data!.Code;
                return new HttpRequestException(envelope.Message!.TrimEnd() + " (" + where + code + ")", null, response.StatusCode);
            }
            return new HttpRequestException(where + (String.IsNullOrWhiteSpace(body) ? "" : ": " + body), null, response.StatusCode);
        }

        #endregion

        #region Private-Methods

        private static ApiErrorEnvelope? Envelope(string body)
        {
            if (String.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith("{", StringComparison.Ordinal)) return null;
            try { return JsonSerializer.Deserialize<ApiErrorEnvelope>(body, _Options); }
            catch (JsonException) { return null; }
        }

        #endregion
    }
}
