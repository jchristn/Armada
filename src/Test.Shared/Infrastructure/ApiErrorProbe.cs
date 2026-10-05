namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver.Core;

    /// <summary>
    /// The fields of a REST error body (ApiErrorResponse) that tests branch on, deserialized from the body text.
    /// </summary>
    public class ApiErrorProbe
    {
        #region Public-Members

        /// <summary>
        /// Result code, or null when the body has none.
        /// </summary>
        public ApiResultEnum? Error { get; set; } = null;

        /// <summary>
        /// HTTP status the body reports.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// English message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Deserialize a REST error body.
        /// </summary>
        /// <param name="body">Response body.</param>
        /// <returns>Probe.</returns>
        /// <exception cref="ArgumentException">Thrown when the body is empty.</exception>
        public static ApiErrorProbe From(string body)
        {
            if (String.IsNullOrWhiteSpace(body)) throw new ArgumentException("Response body is empty.", nameof(body));
            return JsonSerializer.Deserialize<ApiErrorProbe>(body, _Options) ?? new ApiErrorProbe();
        }

        #endregion
    }
}
