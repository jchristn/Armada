namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Maps service exceptions to REST status codes by exception type, never by message text:
    /// <see cref="KeyNotFoundException"/> is 404 (entity missing or not visible to the caller),
    /// <see cref="ArgumentException"/> is 400, <see cref="UnauthorizedAccessException"/> is 403, and
    /// <see cref="InvalidOperationException"/> (a state conflict) is the status the route documents (400 or 409).
    /// A <see cref="DuplicateEntityException"/> (or a provider unique-constraint violation, translated so its text is
    /// never returned) is always 409 with a <see cref="DuplicateEntityErrorDetail"/> in Data.
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
            if (UniqueConstraintViolation.Translate(ex) != null) return true;
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
            if (UniqueConstraintViolation.Translate(ex) != null) return 409;
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
            DuplicateEntityException? duplicate = UniqueConstraintViolation.Translate(ex);
            if (duplicate != null) return Conflict(req, duplicate);
            int statusCode = StatusCodeFor(ex, invalidOperationStatusCode);
            req.Http.Response.StatusCode = statusCode;
            return new ApiErrorResponse { Error = ResultFor(statusCode), Message = ex.Message };
        }

        /// <summary>
        /// Set a 409 status and build the error body for a duplicate entity: <c>Error</c> Conflict, the exception's
        /// message, and a <see cref="DuplicateEntityErrorDetail"/> (Code DuplicateEntity) in <c>Data</c>.
        /// </summary>
        /// <param name="req">Request.</param>
        /// <param name="ex">Duplicate-entity exception.</param>
        /// <returns>Error body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static ApiErrorResponse Conflict(ApiRequest req, DuplicateEntityException ex)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            req.Http.Response.StatusCode = 409;
            return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message, Data = DuplicateEntityErrorDetail.FromException(ex) };
        }

        /// <summary>
        /// Watson middleware that turns a <see cref="DuplicateEntityException"/> (or a provider unique-constraint
        /// violation) escaping any API route into a 409 Conflict with a <see cref="DuplicateEntityErrorDetail"/>, instead
        /// of the generic 500 that would carry the exception's message. Anything else propagates unchanged.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="next">Next middleware or the route handler.</param>
        /// <returns>Task.</returns>
        /// <exception cref="WebserverException">Thrown with result Conflict for a duplicate entity.</exception>
        public static async Task DuplicateEntityMiddlewareAsync(HttpContextBase ctx, Func<Task> next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            try
            {
                await next().ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is WebserverException) && UniqueConstraintViolation.Translate(ex) != null)
            {
                DuplicateEntityException duplicate = UniqueConstraintViolation.Translate(ex)!;
                WebserverException conflict = new WebserverException(ApiResultEnum.Conflict, duplicate.Message, duplicate);
                conflict.Data = DuplicateEntityErrorDetail.FromException(duplicate);
                throw conflict;
            }
        }

        #endregion
    }
}
