namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Decides where a captain launch runs: on a connected, eligible Harbor (over its link) or on the Admiral host. One
    /// routing policy for every launch -- missions (through <see cref="AgentLifecycleHandler"/>) and interactive launches
    /// (captain chat, Ask Armada turns and narrations, planning sessions, objective refinement, vessel context): dock
    /// affinity, the vessel's preferred Harbor and required capabilities, the requested runtime as a capability, least
    /// load, and requireHarborForLaunch (only the launching user's Harbors are eligible, and a launch with none is
    /// refused instead of running on the Admiral host). With the policy off and no eligible Harbor, the launch runs on
    /// the Admiral host as in Local mode.
    /// </summary>
    public class CaptainLaunchRouter
    {
        #region Private-Members

        private readonly string _Header = "[CaptainLaunchRouter] ";
        private readonly ArmadaSettings _Settings;
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly HarborConnectionManager? _Harbors;
        private readonly Func<string, ModelEndpoint?>? _EndpointResolver;
        private readonly LoggingModule _Logging;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, RemoteAgentRuntime> _ActiveRemote =
            new System.Collections.Concurrent.ConcurrentDictionary<int, RemoteAgentRuntime>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Admiral settings (requireHarborForLaunch).</param>
        /// <param name="runtimeFactory">Factory for runtimes that run on the Admiral host.</param>
        /// <param name="harbors">Harbor connection manager, or null when Harbor delegation is not wired.</param>
        /// <param name="endpointResolver">Resolves an API-endpoint captain's model endpoint for a Harbor launch, or null.</param>
        /// <param name="logging">Logging module.</param>
        public CaptainLaunchRouter(ArmadaSettings settings, AgentRuntimeFactory runtimeFactory, HarborConnectionManager? harbors, Func<string, ModelEndpoint?>? endpointResolver, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _Harbors = harbors;
            _EndpointResolver = endpointResolver;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Decide where a launch runs without creating a runtime. Never throws for routing failures: a failure is a
        /// decision with no Harbor and its reason.
        /// </summary>
        /// <param name="context">Launch to route.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The decision.</returns>
        public async Task<CaptainLaunchDecision> DecideAsync(CaptainLaunchContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            CaptainLaunchDecision result = new CaptainLaunchDecision();
            result.HarborRequired = _Settings.RequireHarborForLaunch;
            string? pinnedHarborId = String.IsNullOrWhiteSpace(context.PinnedHarborId) ? null : context.PinnedHarborId;

            if (_Harbors == null)
            {
                result.Reason = "Harbor delegation is not available on this Admiral";
                return result;
            }

            if (!_Harbors.HasConnectedHarbor())
            {
                result.Reason = "no Harbor is connected";
                return result;
            }

            result.AnyHarborConnected = true;
            try
            {
                HarborRoutingRequest request = new HarborRoutingRequest
                {
                    ExistingHarborId = pinnedHarborId,
                    PreferredHarborId = context.Vessel?.PreferredHarborId,
                    RequestedRuntime = context.Captain.Runtime.ToString(),
                    RequiredCapabilities = SplitCapabilities(context.Vessel?.RequiredCapabilities),
                    RestrictToOwner = result.HarborRequired,
                    OwnerUserId = context.UserId
                };

                _Logging.Debug(_Header + "Harbor routing for " + context.Purpose + " (captain " + context.Captain.Id + "): tenant=" + (context.TenantId ?? "(none)")
                    + " runtime=" + request.RequestedRuntime + " dockHarbor=" + (request.ExistingHarborId ?? "(none)")
                    + " connected=[" + String.Join(",", _Harbors.ConnectedHarborIds) + "]");

                HarborRoutingDecision decision = await _Harbors.SelectHarborAsync(context.TenantId, request, token).ConfigureAwait(false);
                if (decision.Success && !String.IsNullOrWhiteSpace(decision.HarborId))
                {
                    result.HarborId = decision.HarborId;
                    result.Reason = decision.Reason ?? "Selected an eligible Harbor.";
                    return result;
                }

                result.Reason = decision.Reason ?? "no eligible Harbor";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Reason = "Harbor routing failed: " + ex.Message;
                _Logging.Warn(_Header + "Harbor routing failed: " + ex.ToString());
            }

            return result;
        }

        /// <summary>
        /// Route an interactive (non-mission) launch and create the runtime to start. A captain routed to a Harbor gets a
        /// remote runtime that runs it there; otherwise a local runtime. API-endpoint captains always run in-process on
        /// the Admiral: they have no CLI, and their tool activity and permission prompts need the in-process runtime.
        /// </summary>
        /// <param name="context">Launch to route.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target to start.</returns>
        /// <exception cref="HarborLaunchUnavailableException">requireHarborForLaunch is on and no eligible Harbor is
        /// connected; the launch must not run on the Admiral host.</exception>
        public async Task<CaptainLaunchTarget> SelectAsync(CaptainLaunchContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (context.Captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
                return new CaptainLaunchTarget(_RuntimeFactory.Create(context.Captain.Runtime), null, "API-endpoint captains run in-process on the Admiral");

            CaptainLaunchDecision decision = await DecideAsync(context, token).ConfigureAwait(false);
            if (decision.HarborId != null && _Harbors != null)
            {
                _Logging.Info(_Header + "running " + context.Purpose + " for captain " + context.Captain.Id + " on Harbor " + decision.HarborId + " (" + decision.Reason + ")");
                RemoteAgentRuntime remote = new RemoteAgentRuntime(_Harbors, decision.HarborId, context.Captain.Runtime, _EndpointResolver);
                remote.UseScratchWorkingDirectory = context.AllowScratchWorkingDirectory;
                remote.OnProcessStarted += processId => _ActiveRemote[processId] = remote;
                remote.OnProcessExited += (processId, exitCode) => _ActiveRemote.TryRemove(new KeyValuePair<int, RemoteAgentRuntime>(processId, remote));
                return new CaptainLaunchTarget(remote, decision.HarborId, decision.Reason);
            }

            if (decision.HarborRequired)
            {
                string message = "No Harbor is connected to run this captain. requireHarborForLaunch is on, so the "
                    + context.Purpose + " will not run on the Admiral host (" + decision.Reason + ").";
                _Logging.Warn(_Header + message);
                throw new HarborLaunchUnavailableException(context.PinnedHarborId, true, message);
            }

            _Logging.Debug(_Header + decision.Reason + "; running " + context.Purpose + " for captain " + context.Captain.Id + " on the Admiral host");
            return new CaptainLaunchTarget(_RuntimeFactory.Create(context.Captain.Runtime), null, decision.Reason);
        }

        /// <summary>
        /// Whether a process id belongs to a captain this Admiral is running on a Harbor.
        /// </summary>
        /// <param name="processId">Process id.</param>
        /// <returns>True when a Harbor job is tracked under that process id.</returns>
        public bool IsHarborProcess(int processId)
        {
            return _Harbors != null && _Harbors.TryGetHarborForProcess(processId, out string? harborId) && !String.IsNullOrWhiteSpace(harborId);
        }

        /// <summary>
        /// Stop a captain process: on its Harbor when the process id belongs to a Harbor job, otherwise on the Admiral
        /// host through the runtime's own stop.
        /// </summary>
        /// <param name="runtime">Captain runtime type.</param>
        /// <param name="processId">Process id.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task StopAsync(AgentRuntimeEnum runtime, int processId, CancellationToken token = default)
        {
            // A launch this router sent to a Harbor: stop it through its own runtime, which also reports the exit to
            // whoever is waiting for it.
            if (_ActiveRemote.TryGetValue(processId, out RemoteAgentRuntime? active))
            {
                await active.StopAsync(processId, token).ConfigureAwait(false);
                return;
            }

            if (_Harbors != null && _Harbors.TryGetHarborForProcess(processId, out string? harborId) && !String.IsNullOrWhiteSpace(harborId))
            {
                await _Harbors.KillByProcessIdAsync(processId, 10000, token).ConfigureAwait(false);
                return;
            }

            IAgentRuntime local = _RuntimeFactory.Create(runtime);
            await local.StopAsync(processId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Describe a launch failure a user can act on: no Harbor to run the captain (requireHarborForLaunch), the
        /// runtime's CLI missing on the Admiral host, or a Harbor that could not start the captain.
        /// </summary>
        /// <param name="failure">The failure.</param>
        /// <param name="message">User-facing message.</param>
        /// <param name="code">Machine-readable reason.</param>
        /// <returns>True when the failure is one of those; false for any other failure.</returns>
        public static bool TryDescribeFailure(Exception failure, out string message, out CaptainChatErrorCodeEnum code)
        {
            message = String.Empty;
            code = CaptainChatErrorCodeEnum.HarborLaunchFailed;
            if (failure == null) return false;

            if (failure is HarborLaunchUnavailableException unavailable)
            {
                message = unavailable.Message;
                code = CaptainChatErrorCodeEnum.HarborRequired;
                return true;
            }

            if (failure is AgentRuntimeNotInstalledException missing)
            {
                message = "The " + missing.Runtime + " CLI ('" + missing.Executable + "') is not installed on the Admiral host. "
                    + "Install it there, or connect a Harbor on a machine that has it (the captain then runs on that machine).";
                code = CaptainChatErrorCodeEnum.RuntimeNotInstalled;
                return true;
            }

            if (failure is HarborJobFailedException harborFailure)
            {
                message = harborFailure.Message;
                code = CaptainChatErrorCodeEnum.HarborLaunchFailed;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Rewrite a prompt that points the captain at a context file on the Admiral host ("Read `path` ...") for a
        /// captain on a Harbor, which cannot read that file: the reference is replaced and the file's content appended.
        /// </summary>
        /// <param name="prompt">Prompt that references the file.</param>
        /// <param name="promptFilePath">Context file on the Admiral host.</param>
        /// <returns>The prompt with the file inlined.</returns>
        public static string InlinePromptFile(string prompt, string promptFilePath)
        {
            if (String.IsNullOrEmpty(prompt) || String.IsNullOrEmpty(promptFilePath)) return prompt;
            string content = ReadPromptFile(promptFilePath);
            if (String.IsNullOrWhiteSpace(content)) return prompt;
            string rewritten = prompt.Replace("Read `" + promptFilePath + "`", "Read the session context at the end of this prompt", StringComparison.Ordinal);
            return rewritten + "\n\n## Session context\n\n" + content.Trim() + "\n";
        }

        /// <summary>
        /// Read a prompt file the Admiral wrote and return it inline. A captain on a Harbor cannot read files on the
        /// Admiral host, so a launch routed there gets the file's content in its prompt instead of its path.
        /// </summary>
        /// <param name="promptFilePath">Prompt file on the Admiral host.</param>
        /// <returns>The file content, or an empty string when it cannot be read.</returns>
        public static string ReadPromptFile(string promptFilePath)
        {
            if (String.IsNullOrEmpty(promptFilePath)) return String.Empty;
            try { return File.ReadAllText(promptFilePath); }
            catch (IOException) { return String.Empty; }
            catch (UnauthorizedAccessException) { return String.Empty; }
        }

        #endregion

        #region Private-Methods

        private static List<string> SplitCapabilities(string? capabilities)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(capabilities)) return result;
            string[] parts = capabilities.Split(new char[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0 && !result.Contains(trimmed)) result.Add(trimmed);
            }

            return result;
        }

        #endregion
    }
}
