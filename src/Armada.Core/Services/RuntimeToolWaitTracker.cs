namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Protocol;

    /// <summary>
    /// Tracks outstanding runtime tool calls, including parallel calls. Output alone never extends a call's deadline.
    /// A tracker belongs to one process launch and must be discarded on exit.
    /// </summary>
    public sealed class RuntimeToolWaitTracker
    {
        private readonly Dictionary<string, DateTime> _Started = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly object _Lock = new object();

        /// <summary>Oldest outstanding tool start, or null when no tool is running.</summary>
        public DateTime? StartedUtc
        {
            get { lock (_Lock) return _Started.Count == 0 ? null : _Started.Values.Min(); }
        }

        /// <summary>Read a raw protocol line; malformed, incomplete and unrelated events are ignored.</summary>
        /// <param name="runtime">Runtime that owns the stream.</param>
        /// <param name="line">Raw stdout, or API tool lifecycle diagnostic.</param>
        /// <param name="nowUtc">Observation time.</param>
        public void Observe(AgentRuntimeEnum runtime, string line, DateTime nowUtc)
        {
            lock (_Lock)
            {
                if (runtime == AgentRuntimeEnum.ClaudeCode && RuntimeStreamJson.TryDeserializeLine(line, out ClaudeStreamLine? claude))
                {
                    if (claude!.Type == ClaudeStreamLine.TypeResult) _Started.Clear();
                    if (claude.Message?.Content == null) return;
                    foreach (ClaudeStreamContentBlock block in claude.Message.Content)
                    {
                        if (claude.Type == ClaudeStreamLine.TypeAssistant && block.Type == "tool_use") Start(block.Id, nowUtc);
                        if (claude.Type == ClaudeStreamLine.TypeUser && block.Type == "tool_result") End(block.ToolUseId);
                    }
                }
                else if (runtime == AgentRuntimeEnum.Codex && RuntimeStreamJson.TryDeserializeLine(line, out CodexStreamEvent? codex))
                {
                    if (codex!.Type == CodexStreamEvent.TypeTurnCompleted || codex.Type == CodexStreamEvent.TypeTurnFailed) _Started.Clear();
                    if (codex.Item == null) return;
                    if (codex.Item.Type != CodexStreamItem.TypeCommandExecution && codex.Item.Type != CodexStreamItem.TypeMcpToolCall) return;
                    if (codex.Type == CodexStreamEvent.TypeItemStarted) Start(codex.Item.Id, nowUtc);
                    if (codex.Type == CodexStreamEvent.TypeItemCompleted) End(codex.Item.Id);
                }
                else if (runtime == AgentRuntimeEnum.Mux && MuxProtocolEvent.TryParse(line, out MuxProtocolEvent? mux))
                {
                    if (mux!.EventType == MuxProtocolEvent.RunCompleted) _Started.Clear();
                    if (mux.EventType == MuxProtocolEvent.ToolCallProposed) Start(mux.ToolCall?.Id, nowUtc);
                    if (mux.EventType == MuxProtocolEvent.ToolCallCompleted) End(mux.ToolCallId);
                }
                else if (runtime == AgentRuntimeEnum.Pi && PiStreamEvent.TryParse(line, out PiStreamEvent? pi))
                {
                    if (pi!.IsRunEnd) _Started.Clear();
                    if (pi.Type == PiStreamEvent.TypeToolExecutionStart) Start(pi.ToolCallId, nowUtc);
                    if (pi.Type == PiStreamEvent.TypeToolExecutionEnd) End(pi.ToolCallId);
                }
                else if (runtime == AgentRuntimeEnum.ApiEndpoint && line.StartsWith("[tool-lifecycle] ", StringComparison.Ordinal))
                {
                    try
                    {
                        ToolLifecycleLine? lifecycle = JsonSerializer.Deserialize<ToolLifecycleLine>(line.Substring(17));
                        if (lifecycle == null || lifecycle.Id == null || !lifecycle.Started.HasValue) return;
                        if (lifecycle.Started.Value) Start(lifecycle.Id, nowUtc);
                        else End(lifecycle.Id);
                    }
                    catch (JsonException) { }
                }
            }
        }

        /// <summary>Whether an old output heartbeat is a stall, allowing only a bounded outstanding tool wait.</summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="heartbeatUtc">Last real output, or launch time when no output has arrived.</param>
        /// <param name="toolStartedUtc">Oldest outstanding tool start, if any.</param>
        /// <param name="stallMinutes">Output silence limit.</param>
        /// <param name="toolWaitMinutes">Hard limit for a tool wait.</param>
        /// <returns>True when recovery should run.</returns>
        public static bool IsStalled(DateTime nowUtc, DateTime? heartbeatUtc, DateTime? toolStartedUtc, int stallMinutes, int toolWaitMinutes)
        {
            if (!heartbeatUtc.HasValue || (nowUtc - heartbeatUtc.Value).TotalMinutes <= stallMinutes) return false;
            return !toolStartedUtc.HasValue || toolStartedUtc.Value > nowUtc ||
                (nowUtc - toolStartedUtc.Value).TotalMinutes >= toolWaitMinutes;
        }

        private void Start(string? id, DateTime nowUtc)
        {
            if (!String.IsNullOrEmpty(id)) _Started.TryAdd(id!, nowUtc);
        }

        private void End(string? id)
        {
            if (!String.IsNullOrEmpty(id)) _Started.Remove(id!);
        }
    }
}
