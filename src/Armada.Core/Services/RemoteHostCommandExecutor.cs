namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Harbor;

    /// <summary>
    /// Runs host commands on a specific Harbor by delegating them over its link. This is the Split-mode
    /// implementation of the host-command seam; each instance targets one Harbor (resolved by dock affinity
    /// or routing), so the identical <see cref="HostCommandRequest"/> a caller would run in-process is
    /// instead carried out on the Harbor host.
    /// </summary>
    public class RemoteHostCommandExecutor : IHostCommandExecutor
    {
        #region Private-Members

        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a specific target Harbor.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="harborId">Target Harbor identifier.</param>
        public RemoteHostCommandExecutor(HarborConnectionManager manager, string harborId)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            _HarborId = harborId;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            HarborGitRequest gitRequest = new HarborGitRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Executable = request.Executable,
                WorkingDirectory = request.WorkingDirectory,
                Arguments = request.Arguments
            };

            HarborGitResult? result = await _Manager.SendGitAsync(_HarborId, gitRequest, request.TimeoutMs, token).ConfigureAwait(false);
            if (result == null)
            {
                return new HostCommandResult
                {
                    ExitCode = -1,
                    TimedOut = true,
                    StandardError = "Harbor " + _HarborId + " did not respond within the timeout."
                };
            }

            return new HostCommandResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                TimedOut = false
            };
        }

        #endregion
    }
}
