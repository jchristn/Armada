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
    /// never returned) is always 409 with a <see cref="DuplicateEntityErrorDetail"/> in Data. A
    /// <see cref="VesselCheckoutUnavailableException"/> (the vessel has no checkout on the Admiral host or any connected
    /// Harbor) is always 409 with a <see cref="VesselCheckoutErrorDetail"/> in Data.
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
            if (ex is VesselCheckoutUnavailableException) return 409;
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
            if (ex is VesselCheckoutUnavailableException checkout) return CheckoutUnavailable(req, checkout);
            if (ex is CollectionLimitExceededException limit) return LimitExceeded(req, limit);
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
        /// Set a 409 status and build the error body for a vessel without a usable checkout: <c>Error</c> Conflict, the
        /// exception's message (which says what to set), and a <see cref="VesselCheckoutErrorDetail"/> (Code
        /// VesselCheckoutUnavailable) in <c>Data</c>.
        /// </summary>
        /// <param name="req">Request.</param>
        /// <param name="ex">Exception.</param>
        /// <returns>Error body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static ApiErrorResponse CheckoutUnavailable(ApiRequest req, VesselCheckoutUnavailableException ex)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            req.Http.Response.StatusCode = 409;
            return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = ex.Message, Data = VesselCheckoutErrorDetail.FromException(ex) };
        }

        /// <summary>
        /// Set a 400 status and build the error body for a collection over its limit: <c>Error</c> BadRequest, the
        /// exception's message (which names the field and the limit), and a <see cref="CollectionLimitErrorDetail"/>
        /// (Code CollectionLimitExceeded) in <c>Data</c>.
        /// </summary>
        /// <param name="req">Request.</param>
        /// <param name="ex">Exception.</param>
        /// <returns>Error body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static ApiErrorResponse LimitExceeded(ApiRequest req, CollectionLimitExceededException ex)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            req.Http.Response.StatusCode = 400;
            return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message, Data = CollectionLimitErrorDetail.FromException(ex) };
        }

        /// <summary>
        /// True when <paramref name="ex"/> is one the black box routes translate to a client or service error: every
        /// <see cref="Armada.Core.BlackBox.BlackBoxException"/>, <see cref="NotImplementedException"/> (a wave-two
        /// operation that is not merged yet), <see cref="NotSupportedException"/> (a storage target that is not
        /// implemented), and everything <see cref="IsMapped"/> accepts.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>True when mapped.</returns>
        public static bool IsBlackBoxMapped(Exception? ex)
        {
            if (ex == null) return false;
            return ex is Armada.Core.BlackBox.BlackBoxException
                || ex is NotImplementedException
                || ex is NotSupportedException
                || IsMapped(ex);
        }

        /// <summary>
        /// Set the response status for a black box error and build the body. Codes (in <c>Data.Code</c>, a
        /// <see cref="BlackBoxErrorDetail"/>): 404 BlackBoxNotFound (missing or not visible, never 403, so nothing leaks
        /// whether another tenant's entry exists), 409 BlackBoxDeleting, 409 BlackBoxObjectCorrupt, 409
        /// BlackBoxConfirmationRequired, 410 BlackBoxObjectMissing, 503 BlackBoxStoreUnavailable, 501 NotImplemented,
        /// 400 InvalidArgument (with the field) and 400 CollectionLimitExceeded (a <see cref="CollectionLimitErrorDetail"/>).
        /// Anything else follows <see cref="ToResponse"/>.
        /// </summary>
        /// <param name="req">Request.</param>
        /// <param name="ex">Exception.</param>
        /// <returns>Error body (an <see cref="ApiErrorResponse"/>, or an <see cref="Armada.Server.ApiStatusErrorResponse"/> for 501 and 503).</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static object ToBlackBoxResponse(ApiRequest req, Exception ex)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            if (ex is Armada.Core.BlackBox.BlackBoxNotFoundException notFound) return BlackBoxError(req, 404, ApiResultEnum.NotFound, notFound.Code, notFound.Message);
            if (ex is Armada.Core.BlackBox.BlackBoxObjectMissingException missing) return BlackBoxError(req, 410, ApiResultEnum.NotFound, missing.Code, missing.Message);
            if (ex is Armada.Core.BlackBox.BlackBoxStoreUnavailableException unavailable)
            {
                req.Http.Response.StatusCode = 503;
                return new Armada.Server.ApiStatusErrorResponse(Armada.Core.Enums.ApiStatusErrorCodeEnum.ServiceUnavailable, unavailable.Message) { Data = new BlackBoxErrorDetail(unavailable.Code) };
            }

            if (ex is Armada.Core.BlackBox.BlackBoxException conflict) return BlackBoxError(req, 409, ApiResultEnum.Conflict, conflict.Code, conflict.Message);
            if (ex is NotImplementedException notImplemented)
            {
                req.Http.Response.StatusCode = 501;
                return new Armada.Server.ApiStatusErrorResponse(Armada.Core.Enums.ApiStatusErrorCodeEnum.NotImplemented, notImplemented.Message) { Data = new BlackBoxErrorDetail("NotImplemented") };
            }

            if (ex is NotSupportedException notSupported)
            {
                req.Http.Response.StatusCode = 503;
                return new Armada.Server.ApiStatusErrorResponse(Armada.Core.Enums.ApiStatusErrorCodeEnum.ServiceUnavailable, notSupported.Message) { Data = new BlackBoxErrorDetail(Armada.Core.BlackBox.BlackBoxStoreUnavailableException.ErrorCode) };
            }

            if (ex is CollectionLimitExceededException limit) return LimitExceeded(req, limit);
            if (ex is ArgumentException argument && UniqueConstraintViolation.Translate(ex) == null)
            {
                string? field = String.IsNullOrEmpty(argument.ParamName) || String.Equals(argument.ParamName, "request", StringComparison.Ordinal) ? null : argument.ParamName;
                string message = argument.ParamName != null && argument.Message.EndsWith(" (Parameter '" + argument.ParamName + "')", StringComparison.Ordinal)
                    ? argument.Message.Substring(0, argument.Message.Length - (" (Parameter '" + argument.ParamName + "')").Length)
                    : argument.Message;
                req.Http.Response.StatusCode = 400;
                return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = message, Data = new BlackBoxErrorDetail("InvalidArgument", field) };
            }

            return ToResponse(req, ex, 409);
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
            catch (CollectionLimitExceededException limit)
            {
                // A collection over its limit escaping any API route is a typed 400, never a 500.
                WebserverException badRequest = new WebserverException(ApiResultEnum.BadRequest, limit.Message, limit);
                badRequest.Data = CollectionLimitErrorDetail.FromException(limit);
                throw badRequest;
            }
        }

        #endregion

        #region Private-Methods

        private static ApiErrorResponse BlackBoxError(ApiRequest req, int status, ApiResultEnum result, string code, string message)
        {
            req.Http.Response.StatusCode = status;
            return new ApiErrorResponse { Error = result, Message = message, Data = new BlackBoxErrorDetail(code) };
        }

        #endregion
    }
}
