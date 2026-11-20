namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Host command executor for branch listing tests: for-each-ref lists one branch named "topic", and rev-list returns
    /// scripted output or fails.
    /// </summary>
    internal sealed class BranchListCommandExecutor : IHostCommandExecutor
    {
        private readonly string? _Counts;

        /// <summary>Instantiate.</summary>
        /// <param name="counts">Output for rev-list, or null to make rev-list fail with exit code 128.</param>
        public BranchListCommandExecutor(string? counts)
        {
            _Counts = counts;
        }

        /// <summary>Return a fixed branch list, then the scripted rev-list result.</summary>
        /// <param name="request">Command to run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scripted command result.</returns>
        /// <exception cref="InvalidOperationException">Thrown for any other git command.</exception>
        public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            if (request.Arguments.Count > 0 && request.Arguments[0] == "for-each-ref")
            {
                string separator = "\x1f";
                return Task.FromResult(new HostCommandResult { StandardOutput = "refs/heads/topic" + separator + "" + separator + "abc123" + separator + "2026-01-01T00:00:00+00:00" + separator + "topic\n" });
            }

            if (request.Arguments.Count > 0 && request.Arguments[0] == "rev-list")
            {
                if (_Counts == null)
                    return Task.FromResult(new HostCommandResult { ExitCode = 128, StandardError = "missing ref" });
                return Task.FromResult(new HostCommandResult { StandardOutput = _Counts });
            }

            throw new InvalidOperationException("Unexpected git command in branch listing test.");
        }
    }
}
