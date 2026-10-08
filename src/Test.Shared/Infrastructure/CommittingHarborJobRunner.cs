namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Services;

    /// <summary>
    /// A Harbor job runner that stands in for a captain: for each launch it commits one file in the launch's working
    /// directory (on the Harbor host, as a real captain would), then reports a clean exit. Records every launch.
    /// </summary>
    public sealed class CommittingHarborJobRunner : IHarborJobRunner
    {
        #region Public-Members

        /// <summary>
        /// Name of the file each job commits.
        /// </summary>
        public const string WorkFile = "harbor-work.txt";

        #endregion

        #region Private-Members

        private readonly List<HarborLaunchRequest> _Launches = new List<HarborLaunchRequest>();
        private int _NextProcessId = 42000;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task StartAsync(
            HarborLaunchRequest request,
            string? mcpBaseUrl,
            Action<int> onStarted,
            Action<HarborOutputStreamEnum, string> onOutput,
            Action<int> onExited,
            CancellationToken token)
        {
            lock (_Launches) _Launches.Add(request);
            int processId = Interlocked.Increment(ref _NextProcessId);
            onStarted(processId);
            _ = Task.Run(async () =>
            {
                int exitCode = 0;
                try
                {
                    await HarborDockGitFixture.CommitFileAsync(request.WorkingDirectory, WorkFile, "made on the Harbor\n", "Harbor captain work").ConfigureAwait(false);
                    onOutput(HarborOutputStreamEnum.Stdout, "committed " + WorkFile);
                }
                catch (InvalidOperationException ex)
                {
                    onOutput(HarborOutputStreamEnum.Stderr, ex.Message);
                    exitCode = 1;
                }

                onExited(exitCode);
            });
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(string jobId, int gracefulTimeoutMs, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public string? ResolveWorkingDirectory(HarborLaunchRequest request)
        {
            return request.WorkingDirectory;
        }

        /// <summary>
        /// Snapshot of the launches received.
        /// </summary>
        /// <returns>The launches.</returns>
        public List<HarborLaunchRequest> LaunchSnapshot()
        {
            lock (_Launches) return new List<HarborLaunchRequest>(_Launches);
        }

        #endregion
    }
}
