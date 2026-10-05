namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A <c>command.error</c> reply on the WebSocket command route. <see cref="Code"/> is the machine-readable reason
    /// (added in 1.0 alongside the existing English <see cref="Error"/> text).
    /// </summary>
    public class WebSocketCommandError
    {
        #region Public-Members

        /// <summary>
        /// Message type, always <c>command.error</c>.
        /// </summary>
        public string Type { get; } = "command.error";

        /// <summary>
        /// The command action that failed, or null when it could not be read.
        /// </summary>
        public string? Action { get; set; } = null;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Error { get; set; } = "";

        /// <summary>
        /// Machine-readable reason.
        /// </summary>
        public WebSocketCommandErrorCodeEnum Code { get; set; } = WebSocketCommandErrorCodeEnum.InternalError;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build a command error reply.
        /// </summary>
        /// <param name="action">Command action, or null.</param>
        /// <param name="code">Machine-readable reason.</param>
        /// <param name="error">Human-readable message.</param>
        /// <returns>The reply.</returns>
        public static WebSocketCommandError Create(string? action, WebSocketCommandErrorCodeEnum code, string error)
        {
            WebSocketCommandError reply = new WebSocketCommandError();
            reply.Action = action;
            reply.Code = code;
            reply.Error = error ?? "";
            return reply;
        }

        /// <summary>
        /// Build a command error reply for an exception, choosing the code by exception type.
        /// </summary>
        /// <param name="action">Command action, or null.</param>
        /// <param name="ex">Exception.</param>
        /// <returns>The reply.</returns>
        public static WebSocketCommandError FromException(string? action, Exception ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            return Create(action, CodeFor(ex), ex.Message);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Machine-readable code for an exception type.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>Code.</returns>
        public static WebSocketCommandErrorCodeEnum CodeFor(Exception ex)
        {
            if (ex is KeyNotFoundException) return WebSocketCommandErrorCodeEnum.NotFound;
            if (ex is ArgumentException) return WebSocketCommandErrorCodeEnum.InvalidArgument;
            if (ex is UnauthorizedAccessException) return WebSocketCommandErrorCodeEnum.Forbidden;
            if (ex is NotSupportedException) return WebSocketCommandErrorCodeEnum.Unavailable;
            if (ex is InvalidOperationException) return WebSocketCommandErrorCodeEnum.Conflict;
            return WebSocketCommandErrorCodeEnum.InternalError;
        }

        #endregion
    }
}
