namespace Armada.Core.Models
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Who stopped a captain and through which surface, recorded on the mission the stop interrupted.
    /// </summary>
    public class OperatorStopContext
    {
        #region Public-Members

        /// <summary>
        /// Source used when a recall gives no context (an internal caller of the recall path).
        /// </summary>
        public const string SourceRecall = "recall";

        /// <summary>
        /// MCP stop_captain tool.
        /// </summary>
        public const string SourceMcpStopCaptain = "stop_captain (MCP)";

        /// <summary>
        /// MCP stop_all tool.
        /// </summary>
        public const string SourceMcpStopAll = "stop_all (MCP)";

        /// <summary>
        /// REST stop route (the dashboard, TUI and CLI stop a captain through it).
        /// </summary>
        public const string SourceRestStopCaptain = "stop_captain (REST)";

        /// <summary>
        /// REST stop-all route.
        /// </summary>
        public const string SourceRestStopAll = "stop_all (REST)";

        /// <summary>
        /// WebSocket stop_captain command.
        /// </summary>
        public const string SourceWebSocketStopCaptain = "stop_captain (WebSocket)";

        /// <summary>
        /// WebSocket stop_all command.
        /// </summary>
        public const string SourceWebSocketStopAll = "stop_all (WebSocket)";

        /// <summary>
        /// The surface the stop came through (for example "stop_captain (MCP)"). Never empty.
        /// </summary>
        public string Source
        {
            get => _Source;
            set => _Source = String.IsNullOrWhiteSpace(value) ? SourceRecall : value.Trim();
        }

        /// <summary>
        /// The user who asked for the stop, when the call path knows it.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// When the stop was requested (UTC).
        /// </summary>
        public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Source = SourceRecall;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the default source.
        /// </summary>
        public OperatorStopContext()
        {
        }

        /// <summary>
        /// Instantiate for a source and an optional user.
        /// </summary>
        /// <param name="source">The surface the stop came through.</param>
        /// <param name="userId">The user who asked for the stop, or null.</param>
        public OperatorStopContext(string source, string? userId = null)
        {
            Source = source;
            UserId = String.IsNullOrWhiteSpace(userId) ? null : userId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Describe the stop for a mission's failure reason, for example
        /// "Stopped by operator (usr_1) via stop_captain (MCP) at 2026-10-28T02:37:59Z".
        /// </summary>
        /// <returns>The description.</returns>
        public string Describe()
        {
            string who = String.IsNullOrEmpty(UserId) ? "" : " (" + UserId + ")";
            return "Stopped by operator" + who + " via " + Source + " at "
                + RequestedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
