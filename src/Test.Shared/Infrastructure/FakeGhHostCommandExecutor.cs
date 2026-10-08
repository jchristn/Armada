namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// A host command executor that runs git for real and answers gh itself, as a GitHub CLI that opens pull request
    /// <see cref="PullRequestUrl"/> would. Every gh call is recorded with the directory it ran in.
    /// </summary>
    public sealed class FakeGhHostCommandExecutor : IHostCommandExecutor
    {
        #region Public-Members

        /// <summary>
        /// The URL of the pull request gh reports.
        /// </summary>
        public const string PullRequestUrl = "https://github.com/example/app/pull/7";

        #endregion

        #region Private-Members

        private readonly LocalHostCommandExecutor _Inner = new LocalHostCommandExecutor();
        private readonly List<HostCommandRequest> _GhCalls = new List<HostCommandRequest>();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!String.Equals(request.Executable, "gh", StringComparison.Ordinal)) return _Inner.RunAsync(request, token);

            lock (_GhCalls) _GhCalls.Add(request);
            bool view = request.Arguments.Count > 1 && request.Arguments[0] == "pr" && request.Arguments[1] == "view";
            string output = view ? "{\"url\":\"" + PullRequestUrl + "\",\"state\":\"OPEN\"}\n" : PullRequestUrl + "\n";
            return Task.FromResult(new HostCommandResult { ExitCode = 0, StandardOutput = output });
        }

        /// <summary>
        /// Snapshot of the gh calls.
        /// </summary>
        /// <returns>The calls.</returns>
        public List<HostCommandRequest> GhCalls()
        {
            lock (_GhCalls) return new List<HostCommandRequest>(_GhCalls);
        }

        #endregion
    }
}
