namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Services;

    /// <summary>
    /// A Harbor job runner that records what the Admiral asked for (launch requests, started process ids, stops) and
    /// delegates to a real runner, so a test can check both the request and its effect on the Harbor host.
    /// </summary>
    public sealed class RecordingHarborJobRunner : IHarborJobRunner
    {
        #region Public-Members

        /// <summary>
        /// Launch requests received, in order.
        /// </summary>
        public List<HarborLaunchRequest> Launches { get; } = new List<HarborLaunchRequest>();

        /// <summary>
        /// Host process ids of started jobs, in order.
        /// </summary>
        public List<int> StartedProcessIds { get; } = new List<int>();

        /// <summary>
        /// Job ids the Admiral asked to stop, in order.
        /// </summary>
        public List<string> Stops { get; } = new List<string>();

        #endregion

        #region Private-Members

        private readonly IHarborJobRunner _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Runner that carries out the jobs.</param>
        public RecordingHarborJobRunner(IHarborJobRunner inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

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
            lock (Launches) Launches.Add(request);
            return _Inner.StartAsync(
                request,
                mcpBaseUrl,
                processId =>
                {
                    lock (StartedProcessIds) StartedProcessIds.Add(processId);
                    onStarted(processId);
                },
                onOutput,
                onExited,
                token);
        }

        /// <inheritdoc />
        public Task StopAsync(string jobId, int gracefulTimeoutMs, CancellationToken token)
        {
            lock (Stops) Stops.Add(jobId);
            return _Inner.StopAsync(jobId, gracefulTimeoutMs, token);
        }

        /// <inheritdoc />
        public string? ResolveWorkingDirectory(HarborLaunchRequest request)
        {
            return _Inner.ResolveWorkingDirectory(request);
        }

        /// <summary>
        /// Snapshot of the launch requests received.
        /// </summary>
        /// <returns>The requests.</returns>
        public List<HarborLaunchRequest> LaunchSnapshot()
        {
            lock (Launches) return new List<HarborLaunchRequest>(Launches);
        }

        /// <summary>
        /// Snapshot of the started process ids.
        /// </summary>
        /// <returns>The process ids.</returns>
        public List<int> StartedSnapshot()
        {
            lock (StartedProcessIds) return new List<int>(StartedProcessIds);
        }

        /// <summary>
        /// Snapshot of the stopped job ids.
        /// </summary>
        /// <returns>The job ids.</returns>
        public List<string> StopSnapshot()
        {
            lock (Stops) return new List<string>(Stops);
        }

        #endregion
    }
}
