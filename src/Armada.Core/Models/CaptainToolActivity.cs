namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One tool-activity event observed while a captain turn runs (a call starting or completing). Streamed to the
    /// dashboard as <c>ask.tool</c> and folded into the persisted tool calls of the assistant message.
    /// </summary>
    public class CaptainToolActivity
    {
        #region Public-Members

        /// <summary>
        /// Phase: <c>started</c> or <c>completed</c>.
        /// </summary>
        public string Phase { get; set; } = "started";

        /// <summary>
        /// Runtime-assigned call identifier, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Tool name as the runtime reported it, or null.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Arguments as JSON text, or null.
        /// </summary>
        public string? Arguments { get; set; } = null;

        /// <summary>
        /// Whether the call succeeded (completed phase), or null.
        /// </summary>
        public bool? Ok { get; set; } = null;

        /// <summary>
        /// Call duration in milliseconds (completed phase), or null.
        /// </summary>
        public double? ElapsedMs { get; set; } = null;

        /// <summary>
        /// Result text (completed phase), or null.
        /// </summary>
        public string? Result { get; set; } = null;

        /// <summary>
        /// True when the runtime reported the call as refused for lack of permission (completed phase), or null.
        /// </summary>
        public bool? PermissionDenied { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainToolActivity()
        {
        }

        #endregion
    }
}
