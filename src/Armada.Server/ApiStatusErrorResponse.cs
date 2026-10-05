namespace Armada.Server
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// REST error body for the statuses that have no code in the framework's error set (422, 501, 503, 504). It has the
    /// same fields as the standard error body (Error, StatusCode, Description, Message, Data), and Error always names the
    /// HTTP status, so clients that branch on Error never see a code that disagrees with the status.
    /// </summary>
    public class ApiStatusErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Error code; matches <see cref="StatusCode"/>.
        /// </summary>
        public ApiStatusErrorCodeEnum Error { get; set; } = ApiStatusErrorCodeEnum.NotImplemented;

        /// <summary>
        /// HTTP status code of <see cref="Error"/>.
        /// </summary>
        public int StatusCode
        {
            get => StatusCodeFor(Error);
        }

        /// <summary>
        /// Standard description of the error code.
        /// </summary>
        public string Description
        {
            get => DescriptionFor(Error);
        }

        /// <summary>
        /// Human-readable detail. Not stable: do not parse it.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Optional structured detail.
        /// </summary>
        public object? Data { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ApiStatusErrorResponse()
        {
        }

        /// <summary>
        /// Instantiate with a code and message.
        /// </summary>
        /// <param name="error">Error code.</param>
        /// <param name="message">Detail message.</param>
        public ApiStatusErrorResponse(ApiStatusErrorCodeEnum error, string? message)
        {
            Error = error;
            Message = message;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// HTTP status code for an error code.
        /// </summary>
        /// <param name="error">Error code.</param>
        /// <returns>Status code.</returns>
        public static int StatusCodeFor(ApiStatusErrorCodeEnum error)
        {
            switch (error)
            {
                case ApiStatusErrorCodeEnum.UnprocessableEntity: return 422;
                case ApiStatusErrorCodeEnum.NotImplemented: return 501;
                case ApiStatusErrorCodeEnum.ServiceUnavailable: return 503;
                case ApiStatusErrorCodeEnum.GatewayTimeout: return 504;
                default: throw new ArgumentOutOfRangeException(nameof(error));
            }
        }

        /// <summary>
        /// Standard description for an error code.
        /// </summary>
        /// <param name="error">Error code.</param>
        /// <returns>Description.</returns>
        public static string DescriptionFor(ApiStatusErrorCodeEnum error)
        {
            switch (error)
            {
                case ApiStatusErrorCodeEnum.UnprocessableEntity: return "The request was understood but the operation was rejected.";
                case ApiStatusErrorCodeEnum.NotImplemented: return "The operation is not supported.";
                case ApiStatusErrorCodeEnum.ServiceUnavailable: return "A required service is not available.";
                case ApiStatusErrorCodeEnum.GatewayTimeout: return "The operation timed out.";
                default: throw new ArgumentOutOfRangeException(nameof(error));
            }
        }

        #endregion
    }
}
