namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// A stand-in Harbor host command seam: runs each command locally (so a temp repository plays the Harbor's
    /// repository) and records it, the way <see cref="RemoteHostCommandExecutor"/> carries commands to a Harbor. With
    /// <see cref="Unreachable"/> set it answers like a Harbor that did not reply (timed out, exit -1).
    /// </summary>
    public sealed class RecordingHostCommandExecutor : IHostCommandExecutor
    {
        /// <summary>
        /// Commands run, as "executable arg1 arg2 ...".
        /// </summary>
        public List<string> Commands { get; } = new List<string>();

        /// <summary>
        /// Answer every command as a Harbor that did not respond.
        /// </summary>
        public bool Unreachable { get; set; } = false;

        private readonly LocalHostCommandExecutor _Local = new LocalHostCommandExecutor();

        /// <inheritdoc />
        public async Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (Commands) Commands.Add(request.Executable + " " + String.Join(" ", request.Arguments));
            if (Unreachable) return new HostCommandResult { ExitCode = -1, TimedOut = true, StandardError = "Harbor did not respond within the timeout." };
            return await _Local.RunAsync(request, token).ConfigureAwait(false);
        }
    }
}
