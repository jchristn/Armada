namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;

    /// <summary>
    /// A real Harbor running in-process: a <see cref="HarborLinkClient"/> with a <see cref="LocalHarborJobRunner"/> (whose
    /// runtimes a test points at shim CLIs) linked to an Admiral-side <see cref="HarborConnectionManager"/> through a
    /// <see cref="LoopbackHarborTransport"/>. Launches the Admiral routes to it run on this machine, as they would on a
    /// developer's machine, and their lifecycle streams back over the link.
    /// </summary>
    public sealed class InProcessHarbor : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier.
        /// </summary>
        public string HarborId { get; }

        /// <summary>
        /// Recording runner wrapped around the Harbor's real job runner.
        /// </summary>
        public RecordingHarborJobRunner Runner { get; }

        /// <summary>
        /// Directory under which the Harbor creates its scratch directories and final-message files.
        /// </summary>
        public string ScratchRoot { get; }

        #endregion

        #region Private-Members

        private readonly CancellationTokenSource _Session = new CancellationTokenSource();
        private readonly LoopbackHarborTransport _Transport;
        private Task _SessionTask = Task.CompletedTask;

        #endregion

        #region Constructors-and-Factories

        private InProcessHarbor(string harborId, RecordingHarborJobRunner runner, string scratchRoot, LoopbackHarborTransport transport)
        {
            HarborId = harborId;
            Runner = runner;
            ScratchRoot = scratchRoot;
            _Transport = transport;
        }

        /// <summary>
        /// Start a Harbor and wait until the Admiral has accepted its handshake.
        /// </summary>
        /// <param name="manager">Admiral-side connection manager.</param>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="tenantId">Tenant the link authenticates as, or null.</param>
        /// <param name="userId">User the link authenticates as (the Harbor's owner), or null.</param>
        /// <param name="runtimes">Runtime factory the Harbor launches captains with.</param>
        /// <param name="capabilities">Advertised capabilities (runtime names and host tools).</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>The connected Harbor.</returns>
        public static async Task<InProcessHarbor> ConnectAsync(
            HarborConnectionManager manager,
            string harborId,
            string? tenantId,
            string? userId,
            AgentRuntimeFactory runtimes,
            IEnumerable<string> capabilities,
            LoggingModule logging)
        {
            string scratchRoot = TestTemp.NewDirectory("harbor_scratch");
            RecordingHarborJobRunner runner = new RecordingHarborJobRunner(new LocalHarborJobRunner(logging, runtimes, scratchRoot));
            List<HarborCapability> advertised = new List<HarborCapability>();
            foreach (string name in capabilities) advertised.Add(new HarborCapability { Name = name, Available = true });

            HarborLinkClient client = new HarborLinkClient(harborId, harborId, advertised, 4, new LocalHostCommandExecutor(), logging, 0, null, runner);
            LoopbackHarborTransport transport = new LoopbackHarborTransport(manager, harborId, tenantId, userId);
            InProcessHarbor harbor = new InProcessHarbor(harborId, runner, scratchRoot, transport);

            TaskCompletionSource<bool> connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harbor._SessionTask = Task.Run(() => client.RunSessionAsync(transport, harbor._Session.Token, () => connected.TrySetResult(true)));

            Task finished = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (finished != connected.Task) throw new TimeoutException("the in-process Harbor " + harborId + " did not connect");
            return harbor;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Drop the link (as when the Harbor host goes away) and wait for the session to end.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task DisconnectAsync()
        {
            _Transport.Complete();
            _Session.Cancel();
            try { await _SessionTask.ConfigureAwait(false); }
            catch { }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try { DisconnectAsync().GetAwaiter().GetResult(); }
            catch { }
            _Session.Dispose();
            TestTemp.TryDelete(ScratchRoot);
        }

        #endregion
    }
}
