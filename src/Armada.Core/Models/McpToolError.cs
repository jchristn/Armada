namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// The error object an MCP tool returns. <see cref="ErrorCode"/> is the machine-readable category; <see cref="Error"/>
    /// is the English message for people and models. <see cref="Code"/> and <see cref="StatusCode"/> carry the
    /// feature-specific detail some tools already returned (for example vessel import codes).
    /// </summary>
    public class McpToolError
    {
        #region Public-Members

        /// <summary>
        /// English message.
        /// </summary>
        public string Error
        {
            get { return _Error; }
            set { _Error = value ?? String.Empty; }
        }

        /// <summary>
        /// Machine-readable category.
        /// </summary>
        public McpToolErrorCodeEnum ErrorCode { get; set; } = McpToolErrorCodeEnum.Failed;

        /// <summary>
        /// Feature-specific detail code (for example a <see cref="VesselImportCodes"/> value), or null.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// HTTP-equivalent status some tools reported before <see cref="ErrorCode"/> existed, or null.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Error = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public McpToolError()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="errorCode">Category.</param>
        /// <param name="error">English message.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        public McpToolError(McpToolErrorCodeEnum errorCode, string error, string? code = null)
        {
            ErrorCode = errorCode;
            Error = error;
            Code = code;
        }

        /// <summary>
        /// The referenced entity does not exist or is not visible to the caller.
        /// </summary>
        /// <param name="error">English message.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        /// <returns>Error.</returns>
        public static McpToolError NotFound(string error, string? code = null)
        {
            return new McpToolError(McpToolErrorCodeEnum.NotFound, error, code);
        }

        /// <summary>
        /// An argument is missing, malformed, or out of range.
        /// </summary>
        /// <param name="error">English message.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        /// <returns>Error.</returns>
        public static McpToolError InvalidArgument(string error, string? code = null)
        {
            return new McpToolError(McpToolErrorCodeEnum.InvalidArgument, error, code);
        }

        /// <summary>
        /// The entity's state does not allow the operation, or it already exists. For a value that must be unique and
        /// is taken, pass <see cref="DuplicateEntityException.ErrorCode"/> as the code (or use
        /// <see cref="FromException(Exception, string?)"/> with the <see cref="DuplicateEntityException"/>).
        /// </summary>
        /// <param name="error">English message.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        /// <returns>Error.</returns>
        public static McpToolError Conflict(string error, string? code = null)
        {
            return new McpToolError(McpToolErrorCodeEnum.Conflict, error, code);
        }

        /// <summary>
        /// The caller lacks the permission the operation needs.
        /// </summary>
        /// <param name="error">English message.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        /// <returns>Error.</returns>
        public static McpToolError Forbidden(string error, string? code = null)
        {
            return new McpToolError(McpToolErrorCodeEnum.Forbidden, error, code);
        }

        /// <summary>
        /// A service the operation needs is not configured or not available.
        /// </summary>
        /// <param name="error">English message.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        /// <returns>Error.</returns>
        public static McpToolError Unavailable(string error, string? code = null)
        {
            return new McpToolError(McpToolErrorCodeEnum.Unavailable, error, code);
        }

        /// <summary>
        /// The operation failed for another reason.
        /// </summary>
        /// <param name="error">English message.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        /// <returns>Error.</returns>
        public static McpToolError Failed(string error, string? code = null)
        {
            return new McpToolError(McpToolErrorCodeEnum.Failed, error, code);
        }

        /// <summary>
        /// Map an exception to an error by its type (never by its message): <see cref="KeyNotFoundException"/> is
        /// NotFound, <see cref="ArgumentException"/> InvalidArgument, <see cref="InvalidOperationException"/> Conflict,
        /// <see cref="UnauthorizedAccessException"/> Forbidden, <see cref="NotSupportedException"/> Unavailable, and
        /// anything else Failed. A <see cref="DuplicateEntityException"/>, or a provider unique-constraint violation
        /// (translated by <see cref="UniqueConstraintViolation"/> so its text is never returned), is Conflict with
        /// <see cref="Code"/> <see cref="DuplicateEntityException.ErrorCode"/> unless a code is given. A
        /// <see cref="VesselCheckoutUnavailableException"/> is Unavailable with <see cref="Code"/>
        /// "VesselCheckoutUnavailable.&lt;reason&gt;" (for example VesselCheckoutUnavailable.NoHarborCheckout) and the message
        /// that says what to set.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <param name="code">Feature-specific detail code, or null.</param>
        /// <returns>Error.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ex"/> is null.</exception>
        public static McpToolError FromException(Exception ex, string? code = null)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            DuplicateEntityException? duplicate = UniqueConstraintViolation.Translate(ex);
            if (duplicate != null) return new McpToolError(McpToolErrorCodeEnum.Conflict, duplicate.Message, code ?? DuplicateEntityException.ErrorCode);
            if (ex is VesselCheckoutUnavailableException checkout)
                return new McpToolError(McpToolErrorCodeEnum.Unavailable, checkout.Message, code ?? (VesselCheckoutUnavailableException.ErrorCode + "." + checkout.Code));
            McpToolErrorCodeEnum category = McpToolErrorCodeEnum.Failed;
            if (ex is KeyNotFoundException) category = McpToolErrorCodeEnum.NotFound;
            else if (ex is ArgumentException) category = McpToolErrorCodeEnum.InvalidArgument;
            else if (ex is UnauthorizedAccessException) category = McpToolErrorCodeEnum.Forbidden;
            else if (ex is NotSupportedException) category = McpToolErrorCodeEnum.Unavailable;
            else if (ex is InvalidOperationException) category = McpToolErrorCodeEnum.Conflict;
            return new McpToolError(category, ex.Message, code);
        }

        #endregion
    }
}
