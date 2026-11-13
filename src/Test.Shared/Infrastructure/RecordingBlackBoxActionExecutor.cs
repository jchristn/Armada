namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Models;

    /// <summary>
    /// Test black box action executor: records every action it is asked to run (with the caller) and succeeds, except for
    /// the tools listed in <see cref="FailTools"/>.
    /// </summary>
    public sealed class RecordingBlackBoxActionExecutor : IBlackBoxActionExecutor
    {
        /// <summary>Actions run, in order.</summary>
        public ConcurrentQueue<BlackBoxAction> Calls { get; } = new ConcurrentQueue<BlackBoxAction>();

        /// <summary>Callers, in order.</summary>
        public ConcurrentQueue<AuthContext> Callers { get; } = new ConcurrentQueue<AuthContext>();

        /// <summary>Tools that fail.</summary>
        public HashSet<string> FailTools { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Tool names of the calls, in order.</summary>
        public List<string> Tools => Calls.Select(c => c.Tool).ToList();

        /// <inheritdoc />
        public Task<BlackBoxRecoveryStepResult> ExecuteAsync(AuthContext caller, BlackBoxAction action, CancellationToken token = default)
        {
            Calls.Enqueue(action);
            Callers.Enqueue(caller);
            bool fail = FailTools.Contains(action.Tool);
            return Task.FromResult(new BlackBoxRecoveryStepResult { Tool = action.Tool, Succeeded = !fail, Message = fail ? "fake failure" : "fake ok" });
        }
    }
}
