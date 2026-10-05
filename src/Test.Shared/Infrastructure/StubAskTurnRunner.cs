namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Scriptable captain turn runner for Ask Armada tests: records every options object, streams a chunk, optionally
    /// waits on a gate (to keep a turn running), and returns a configurable reply and tool calls.
    /// </summary>
    public sealed class StubAskTurnRunner : IAskCaptainTurnRunner
    {
        /// <summary>
        /// Options of every turn, in order.
        /// </summary>
        public List<CaptainChatTurnOptions> Calls { get; } = new List<CaptainChatTurnOptions>();

        /// <summary>
        /// Reply text returned by turns. Default "Done.".
        /// </summary>
        public string Reply { get; set; } = "Done.";

        /// <summary>
        /// When set, the reply is a failure with this error.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// When set, every turn waits for this task before returning (or for cancellation).
        /// </summary>
        public TaskCompletionSource<bool>? Gate { get; set; } = null;

        /// <summary>
        /// Tool calls returned by every turn.
        /// </summary>
        public List<AskMessageToolCall> ToolCalls { get; set; } = new List<AskMessageToolCall>();

        /// <inheritdoc />
        public async Task<CaptainChatTurnResult> RunTurnAsync(CaptainChatTurnOptions options, CancellationToken token = default)
        {
            lock (Calls) Calls.Add(options);
            options.OnChunk?.Invoke("partial ");
            options.OnTool?.Invoke(new CaptainToolActivity { Phase = "started", Id = "call_1", Name = "mcp__armada__status" });
            if (Gate != null)
            {
                using (token.Register(() => Gate.TrySetCanceled()))
                {
                    await Gate.Task.ConfigureAwait(false);
                }
            }

            token.ThrowIfCancellationRequested();
            CaptainChatTurnResult result = new CaptainChatTurnResult();
            result.Response = Error == null
                ? new CaptainChatResponse { Success = true, Reply = Reply, Model = "stub" }
                : new CaptainChatResponse { Success = false, Error = Error };
            result.ToolCalls = new List<AskMessageToolCall>();
            foreach (AskMessageToolCall call in ToolCalls)
            {
                AskMessageToolCall copy = new AskMessageToolCall();
                copy.CallId = call.CallId;
                copy.ToolName = call.ToolName;
                copy.ArgumentsText = call.ArgumentsText;
                copy.ResultText = call.ResultText;
                copy.Ok = call.Ok;
                copy.PermissionDenied = call.PermissionDenied;
                result.ToolCalls.Add(copy);
            }

            return result;
        }
    }
}
