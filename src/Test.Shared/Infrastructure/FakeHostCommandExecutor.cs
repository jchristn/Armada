namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Host command executor double for dependency-tool tests: every request is recorded and answered by a handler,
    /// so suites can feed fixture JSON, exit codes, timeouts, or a missing executable without running dotnet or npm.
    /// </summary>
    public sealed class FakeHostCommandExecutor : IHostCommandExecutor
    {
        #region Public-Members

        /// <summary>
        /// Requests received, in order.
        /// </summary>
        public List<HostCommandRequest> Requests { get; } = new List<HostCommandRequest>();

        /// <summary>
        /// Handler producing the result for a request. Return null to simulate a missing executable.
        /// </summary>
        public Func<HostCommandRequest, HostCommandResult?> Handler { get; set; } = request => new HostCommandResult();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the request and answer it with the handler.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The handler's result.</returns>
        /// <exception cref="Win32Exception">Thrown when the handler returns null (missing executable).</exception>
        public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            lock (Requests) Requests.Add(request);
            HostCommandResult? result = Handler(request);
            if (result == null) throw new Win32Exception(2, "No such file or directory: " + request.Executable);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Build a completed result.
        /// </summary>
        /// <param name="exitCode">Exit code.</param>
        /// <param name="stdout">Standard output.</param>
        /// <param name="stderr">Standard error.</param>
        /// <returns>The result.</returns>
        public static HostCommandResult Result(int exitCode, string stdout, string stderr = "")
        {
            return new HostCommandResult { ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };
        }

        #endregion
    }
}
