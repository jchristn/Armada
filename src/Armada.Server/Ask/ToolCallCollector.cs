namespace Armada.Server.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Folds the started/completed tool-activity events of one captain turn into tool-call records, merged by call id.
    /// </summary>
    /// <remarks>Thread safety: all members lock an internal object; runtime output callbacks may arrive on any thread.</remarks>
    public class ToolCallCollector
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<AskMessageToolCall> _Calls = new List<AskMessageToolCall>();
        private readonly Dictionary<string, AskMessageToolCall> _ById = new Dictionary<string, AskMessageToolCall>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _Started = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a tool activity event.
        /// </summary>
        /// <param name="activity">Activity event; ignored when null.</param>
        public void Observe(CaptainToolActivity activity)
        {
            if (activity == null) return;
            lock (_Lock)
            {
                AskMessageToolCall? call = null;
                if (!String.IsNullOrEmpty(activity.Id)) _ById.TryGetValue(activity.Id!, out call);

                if (call == null)
                {
                    call = new AskMessageToolCall();
                    call.CallId = activity.Id;
                    _Calls.Add(call);
                    if (!String.IsNullOrEmpty(activity.Id))
                    {
                        _ById[activity.Id!] = call;
                        _Started[activity.Id!] = DateTime.UtcNow;
                    }
                }

                if (!String.IsNullOrEmpty(activity.Name)) call.ToolName = activity.Name!;
                if (!String.IsNullOrEmpty(activity.Arguments)) call.ArgumentsText = activity.Arguments;
                if (activity.PermissionDenied == true)
                {
                    // A later typed report (Claude Code's permission_denials) marks a call already completed: keep its
                    // result text and timing.
                    call.PermissionDenied = true;
                    call.Ok = false;
                }
                else if (String.Equals(activity.Phase, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    call.Ok = activity.Ok;
                    call.ResultText = activity.Result;
                    if (activity.ElapsedMs.HasValue) call.ElapsedMs = (long)Math.Round(activity.ElapsedMs.Value);
                }
            }
        }

        /// <summary>
        /// Tool name of a call seen earlier in the turn.
        /// </summary>
        /// <param name="callId">Call identifier.</param>
        /// <returns>The tool name, or null.</returns>
        public string? NameOf(string? callId)
        {
            if (String.IsNullOrEmpty(callId)) return null;
            lock (_Lock)
            {
                return _ById.TryGetValue(callId!, out AskMessageToolCall? call) && !String.IsNullOrEmpty(call.ToolName) ? call.ToolName : null;
            }
        }

        /// <summary>
        /// Milliseconds since a call started.
        /// </summary>
        /// <param name="callId">Call identifier.</param>
        /// <returns>Elapsed milliseconds, or null when the call was not seen.</returns>
        public double? ElapsedSince(string? callId)
        {
            if (String.IsNullOrEmpty(callId)) return null;
            lock (_Lock)
            {
                return _Started.TryGetValue(callId!, out DateTime started) ? (DateTime.UtcNow - started).TotalMilliseconds : (double?)null;
            }
        }

        /// <summary>
        /// Snapshot of the collected calls in first-seen order.
        /// </summary>
        /// <returns>Tool calls.</returns>
        public List<AskMessageToolCall> ToList()
        {
            lock (_Lock) return _Calls.Where(c => !String.IsNullOrEmpty(c.ToolName)).ToList();
        }

        #endregion
    }
}
