namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Maps service exceptions to REST status codes by exception type, never by message text:
    /// <see cref="KeyNotFoundException"/> is 404 (entity missing or not visible to the caller),
    /// <see cref="ArgumentException"/> is 400, <see cref="UnauthorizedAccessException"/> is 403, and
    /// <see cref="InvalidOperationException"/> (a state conflict) is the status the route documents (400 or 409).
    /// </summary>
    public static class RouteErrorMapper
    {
        #region Public-Methods

        /// <summary>
        /// True when <paramref name="ex"/> is one of the exception types this mapper translates to a client error.
        /// Use as an exception filter so anything else still surfaces as a server error.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>True when mapped.</returns>
        public static bool IsMapped(Exception? ex)
        {
            if (ex == null) return false;
            return ex is KeyNotFoundException
                || ex is ArgumentException
                || ex is UnauthorizedAccessException
                || ex is InvalidOperationException;
        }

        /// <summary>
        /// Status code for a mapped exception.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <param name="invalidOperationStatusCode">Status for <see cref="InvalidOperationException"/> (400 or 409).</param>
        /// <returns>HTTP status code.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ex"/> is null.</exception>
        public static int StatusCodeFor(Exception ex, int invalidOperationStatusCode = 400)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            if (ex is KeyNotFoundException) return 404;
            if (ex is ArgumentException) return 400;
            if (ex is UnauthorizedAccessException) return 403;
            if (ex is InvalidOperationException) return invalidOperationStatusCode;
            return 500;
        }

        /// <summary>
        /// Result code for an HTTP status code.
        /// </summary>
        /// <param name="statusCode">HTTP status code.</param>
        /// <returns>Result code.</returns>
        public static ApiResultEnum ResultFor(int statusCode)
        {
            switch (statusCode)
            {
                case 400: return ApiResultEnum.BadRequest;
                case 403: return ApiResultEnum.Forbidden;
                case 404: return ApiResultEnum.NotFound;
                case 409: return ApiResultEnum.Conflict;
                default: return ApiResultEnum.InternalError;
            }
        }

        /// <summary>
        /// Set the response status for <paramref name="ex"/> and build the error body.
        /// </summary>
        /// <param name="req">Request.</param>
        /// <param name="ex">Exception.</param>
        /// <param name="invalidOperationStatusCode">Status for <see cref="InvalidOperationException"/> (400 or 409).</param>
        /// <returns>Error body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static ApiErrorResponse ToResponse(ApiRequest req, Exception ex, int invalidOperationStatusCode = 400)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            int statusCode = StatusCodeFor(ex, invalidOperationStatusCode);
            req.Http.Response.StatusCode = statusCode;
            return new ApiErrorResponse { Error = ResultFor(statusCode), Message = ex.Message };
        }

        #endregion
    }
}
