namespace Armada.Client
{
    using System;
    using System.Net;

    /// <summary>
    /// Raised for a non-success Armada API response. Carries the HTTP status, the machine-readable error code when the
    /// server supplies one (for example vessel import errors), the server's message, and the request id the client sent
    /// as <c>X-Request-Id</c> so the failing call can be found in API Requests.
    /// </summary>
    public class ArmadaApiException : Exception
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code of the response. 0 when the request never produced a response (timeout, connection failure).
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Machine-readable error code from the response (<c>Data.Code</c> or <c>Code</c>), or null.
        /// </summary>
        public string? Code { get; }

        /// <summary>
        /// Error category from the response body (<c>Error</c>), for example <c>BadRequest</c>, or null.
        /// </summary>
        public string? ErrorName { get; }

        /// <summary>
        /// Request id sent with the call (<c>X-Request-Id</c>), or the id the server returned. Never null.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// HTTP method of the failing call, for example <c>GET</c>.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Request path of the failing call, for example <c>/api/v1/missions</c>.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Raw response body text, or null when there was no response.
        /// </summary>
        public string? ResponseBody { get; }

        /// <summary>
        /// Structured error data from the response, or null.
        /// </summary>
        public Armada.Client.Models.ApiErrorData? ErrorData { get; }

        /// <summary>
        /// True when the response was 401 Unauthorized.
        /// </summary>
        public bool IsUnauthorized
        {
            get { return StatusCode == (int)HttpStatusCode.Unauthorized; }
        }

        /// <summary>
        /// True when the request timed out or never reached the server.
        /// </summary>
        public bool IsTransport
        {
            get { return StatusCode == 0; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Human-readable message (the server's message when available).</param>
        /// <param name="statusCode">HTTP status code, or 0 for transport failures.</param>
        /// <param name="code">Machine-readable error code, or null.</param>
        /// <param name="errorName">Error category, or null.</param>
        /// <param name="requestId">Request id; null becomes an empty string.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path.</param>
        /// <param name="responseBody">Raw response body, or null.</param>
        /// <param name="data">Structured error data, or null.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public ArmadaApiException(
            string message,
            int statusCode,
            string? code,
            string? errorName,
            string? requestId,
            string method,
            string path,
            string? responseBody,
            Armada.Client.Models.ApiErrorData? data,
            Exception? inner = null)
            : base(String.IsNullOrEmpty(message) ? "Armada API request failed" : message, inner)
        {
            StatusCode = statusCode;
            Code = code;
            ErrorName = errorName;
            RequestId = requestId ?? "";
            Method = method ?? "";
            Path = path ?? "";
            ResponseBody = responseBody;
            ErrorData = data;
        }

        #endregion
    }
}
