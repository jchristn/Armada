namespace Armada.Server.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// What a running Ask Armada turn has streamed so far: reply text, thinking, and tool calls. The reply and its tool
    /// calls are persisted only when the turn ends; until then this is the only server-side record of them, so thread
    /// detail reports it (<see cref="AskTurnProgress"/>) and a turn that is stopped or crashes still keeps its tool calls.
    /// </summary>
    /// <remarks>Thread safety: all members lock an internal object; runtime output callbacks may arrive on any thread.</remarks>
    public class AskTurnLiveState
    {
        #region Public-Members

        /// <summary>
        /// Most characters of streamed text (and of thinking) kept. Later text is dropped from the snapshot only; the
        /// persisted reply is unaffected.
        /// </summary>
        public const int MaxTextChars = 256 * 1024;

        /// <summary>
        /// The reply message reserved for the turn, or null before it is reserved.
        /// </summary>
        public string? MessageId
        {
            get { lock (_Lock) return _MessageId; }
            set { lock (_Lock) _MessageId = value; }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly StringBuilder _Text = new StringBuilder();
        private readonly StringBuilder _Thinking = new StringBuilder();
        private readonly ToolCallCollector _ToolCalls = new ToolCallCollector();
        private string? _MessageId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTurnLiveState()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a streamed reply chunk.
        /// </summary>
        /// <param name="delta">Chunk; ignored when null or empty.</param>
        public void AppendText(string? delta)
        {
            if (String.IsNullOrEmpty(delta)) return;
            lock (_Lock) Append(_Text, delta!);
        }

        /// <summary>
        /// Record a streamed thinking chunk.
        /// </summary>
        /// <param name="delta">Chunk; ignored when null or empty.</param>
        public void AppendThinking(string? delta)
        {
            if (String.IsNullOrEmpty(delta)) return;
            lock (_Lock) Append(_Thinking, delta!);
        }

        /// <summary>
        /// Record a tool activity event (merged by call id).
        /// </summary>
        /// <param name="activity">Activity; ignored when null.</param>
        public void ObserveTool(CaptainToolActivity? activity)
        {
            if (activity == null) return;
            _ToolCalls.Observe(activity);
        }

        /// <summary>
        /// The tool calls seen so far, as new records (first-seen order) safe to persist or serialize.
        /// </summary>
        /// <returns>Tool calls.</returns>
        public List<AskMessageToolCall> ToolCalls()
        {
            List<AskMessageToolCall> copies = new List<AskMessageToolCall>();
            foreach (AskMessageToolCall call in _ToolCalls.ToList())
            {
                AskMessageToolCall copy = new AskMessageToolCall();
                copy.CallId = call.CallId;
                copy.ToolName = call.ToolName;
                copy.ArgumentsText = call.ArgumentsText;
                copy.ResultText = call.ResultText;
                copy.Ok = call.Ok;
                copy.ElapsedMs = call.ElapsedMs;
                copy.PermissionDenied = call.PermissionDenied;
                copies.Add(copy);
            }

            return copies;
        }

        /// <summary>
        /// Snapshot of the turn's progress.
        /// </summary>
        /// <param name="handle">The running turn.</param>
        /// <returns>Progress.</returns>
        /// <exception cref="ArgumentNullException">Thrown when handle is null.</exception>
        public AskTurnProgress Snapshot(AskTurnHandle handle)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            AskTurnProgress progress = new AskTurnProgress();
            progress.TurnId = handle.TurnId;
            progress.StartedUtc = handle.StartedUtc;
            lock (_Lock)
            {
                progress.MessageId = _MessageId;
                progress.Text = _Text.ToString();
                progress.ThinkingText = _Thinking.ToString();
            }

            progress.ToolCalls = ToolCalls();
            progress.ToolCallCount = progress.ToolCalls.Count;
            return progress;
        }

        #endregion

        #region Private-Methods

        private static void Append(StringBuilder builder, string delta)
        {
            int room = MaxTextChars - builder.Length;
            if (room <= 0) return;
            builder.Append(delta.Length > room ? delta.Substring(0, room) : delta);
        }

        #endregion
    }
}
