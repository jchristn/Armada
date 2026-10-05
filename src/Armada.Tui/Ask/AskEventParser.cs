namespace Armada.Tui.Ask
{
    using System;
    using System.Text.Json.Nodes;
    using Armada.Client.Socket;
    using Armada.Core.Models;

    /// <summary>
    /// Turns raw socket messages into <see cref="AskEvent"/> values (the dashboard's <c>parseAskEvent</c>): payloads are
    /// read case-insensitively, the thread id falls back to the nested entity's own thread id, and events missing what
    /// they need (a turn id, a delta, a thread) are dropped. Thread-safe (stateless).
    /// </summary>
    public static class AskEventParser
    {
        #region Public-Methods

        /// <summary>
        /// Parse a socket message, or return null when it is not a usable Ask event.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>Event or null.</returns>
        public static AskEvent? Parse(ArmadaSocketMessage? message)
        {
            if (message == null || String.IsNullOrEmpty(message.Type) || !message.Type.StartsWith("ask.", StringComparison.Ordinal)) return null;
            if (message.Data == null || !(message.Data is JsonObject)) return null;
            switch (message.Type)
            {
                case ArmadaEventTypes.AskChunk:
                case ArmadaEventTypes.AskThinking:
                    return ParseDelta(message);
                case ArmadaEventTypes.AskTool:
                    return ParseTool(message);
                case ArmadaEventTypes.AskTurn:
                    return ParseTurn(message);
                case ArmadaEventTypes.AskMessage:
                    return ParseMessage(message);
                case ArmadaEventTypes.AskProposal:
                    return ParseProposal(message);
                case ArmadaEventTypes.AskWork:
                    return ParseWork(message);
                case ArmadaEventTypes.AskThread:
                    return ParseThread(message);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Text of a JSON node: a string value as-is, anything else as compact JSON, null for null.
        /// </summary>
        /// <param name="node">Node.</param>
        /// <returns>Text or null.</returns>
        public static string? NodeText(JsonNode? node)
        {
            if (node == null) return null;
            if (node is JsonValue value && value.TryGetValue<string>(out string? s)) return s;
            return node.ToJsonString();
        }

        #endregion

        #region Private-Methods

        private static AskEvent? ParseDelta(ArmadaSocketMessage message)
        {
            AskDeltaEvent? data = message.GetData<AskDeltaEvent>();
            if (data == null || String.IsNullOrEmpty(data.ThreadId) || String.IsNullOrEmpty(data.TurnId) || String.IsNullOrEmpty(data.Delta)) return null;
            AskEvent e = new AskEvent();
            e.Type = message.Type;
            e.ThreadId = data.ThreadId!;
            e.TurnId = data.TurnId!;
            e.Delta = data.Delta!;
            return e;
        }

        private static AskEvent? ParseTool(ArmadaSocketMessage message)
        {
            AskToolEvent? data = message.GetData<AskToolEvent>();
            if (data == null || String.IsNullOrEmpty(data.ThreadId) || String.IsNullOrEmpty(data.TurnId)) return null;
            AskEvent e = new AskEvent();
            e.Type = message.Type;
            e.ThreadId = data.ThreadId!;
            e.TurnId = data.TurnId!;
            e.ToolPhase = data.Phase;
            e.ToolId = data.Id;
            e.ToolName = data.Name;
            e.ToolArguments = NodeText(data.Arguments);
            e.ToolResult = NodeText(data.Result);
            e.ToolOk = data.Ok;
            e.ToolElapsedMs = data.ElapsedMs;
            e.ToolPermissionDenied = data.PermissionDenied;
            return e;
        }

        private static AskEvent? ParseTurn(ArmadaSocketMessage message)
        {
            AskTurnEvent? data = message.GetData<AskTurnEvent>();
            if (data == null || String.IsNullOrEmpty(data.ThreadId) || String.IsNullOrEmpty(data.TurnId)) return null;
            AskEvent e = new AskEvent();
            e.Type = message.Type;
            e.ThreadId = data.ThreadId!;
            e.TurnId = data.TurnId!;
            e.State = data.State ?? AskTurnStateEnum.Started;
            e.MessageId = String.IsNullOrEmpty(data.MessageId) ? null : data.MessageId;
            e.Error = !String.IsNullOrEmpty(data.Error) ? data.Error : (String.IsNullOrEmpty(data.ErrorText) ? null : data.ErrorText);
            return e;
        }

        private static AskEvent? ParseMessage(ArmadaSocketMessage message)
        {
            AskMessageEvent? data = message.GetData<AskMessageEvent>();
            if (data == null || data.Message == null) return null;
            string threadId = First(data.ThreadId, data.Message.ThreadId);
            if (threadId.Length == 0) return null;
            if (String.IsNullOrEmpty(data.Message.ThreadId)) data.Message.ThreadId = threadId;
            AskEvent e = new AskEvent();
            e.Type = message.Type;
            e.ThreadId = threadId;
            e.Message = data.Message;
            return e;
        }

        private static AskEvent? ParseProposal(ArmadaSocketMessage message)
        {
            AskProposalEvent? data = message.GetData<AskProposalEvent>();
            if (data == null || data.Proposal == null) return null;
            string threadId = First(data.ThreadId, data.Proposal.ThreadId);
            if (threadId.Length == 0) return null;
            if (String.IsNullOrEmpty(data.Proposal.ThreadId)) data.Proposal.ThreadId = threadId;
            AskEvent e = new AskEvent();
            e.Type = message.Type;
            e.ThreadId = threadId;
            e.Proposal = data.Proposal;
            return e;
        }

        private static AskEvent? ParseWork(ArmadaSocketMessage message)
        {
            AskWorkEvent? data = message.GetData<AskWorkEvent>();
            if (data == null) return null;
            string threadId = First(data.ThreadId, data.TrackedWork?.ThreadId);
            if (threadId.Length == 0) threadId = First(data.Snapshot?.ThreadId, null);
            if (threadId.Length == 0) return null;
            string workId = First(data.TrackedWorkId, First(data.Snapshot?.TrackedWorkId, data.TrackedWork?.Id));
            if (workId.Length == 0) return null;
            AskEvent e = new AskEvent();
            e.Type = message.Type;
            e.ThreadId = threadId;
            e.TrackedWorkId = workId;
            e.Snapshot = data.Snapshot;
            e.TrackedWork = data.TrackedWork;
            return e;
        }

        private static AskEvent? ParseThread(ArmadaSocketMessage message)
        {
            AskThreadEvent? data = message.GetData<AskThreadEvent>();
            if (data == null || data.Thread == null) return null;
            string threadId = First(data.ThreadId, data.Thread.Id);
            if (threadId.Length == 0) return null;
            if (String.IsNullOrEmpty(data.Thread.Id)) data.Thread.Id = threadId;
            AskEvent e = new AskEvent();
            e.Type = message.Type;
            e.ThreadId = threadId;
            e.Thread = data.Thread;
            return e;
        }

        private static string First(string? a, string? b)
        {
            if (!String.IsNullOrEmpty(a)) return a!;
            if (!String.IsNullOrEmpty(b)) return b!;
            return "";
        }

        #endregion
    }
}
