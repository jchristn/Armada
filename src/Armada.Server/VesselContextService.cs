namespace Armada.Server
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Builds or refines a vessel's Model Context. Launches the chosen captain in a fresh worktree of the
    /// vessel's repository, prompts it (via the editable <c>vessel.build_context</c> template plus any
    /// operator guidance) to analyze the repository and produce a durable context document, and saves the
    /// result to <see cref="Vessel.ModelContext"/>. When the vessel already has a Model Context the prompt
    /// asks the captain to refine it rather than start over.
    /// </summary>
    public class VesselContextService
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly IDockService _Docks;
        private readonly IPromptTemplateService _PromptTemplates;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[VesselContextService] ";

        // Analyzing a repository (reading many files, possibly running commands) can take a while, so allow a
        // generous window before giving up.
        private const int _DefaultTimeoutMs = 900000;
        private const int _MaxOutputChars = 400000;

        private const string _FallbackPrompt =
            "Analyze the git repository in your current working directory (read-only) and produce a concise " +
            "\"Model Context\" document describing the project purpose, architecture, key files, build/test/run " +
            "commands, dependencies, and conventions that a future AI coding agent must know. Do not modify any " +
            "files. Output only the Model Context document.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="runtimeFactory">Agent runtime factory used to launch the captain's CLI headlessly.</param>
        /// <param name="docks">Dock service used to provision and reclaim the analysis worktree.</param>
        /// <param name="promptTemplates">Prompt template service resolving the editable build-context prompt.</param>
        /// <param name="logging">Logging module.</param>
        public VesselContextService(
            DatabaseDriver database,
            AgentRuntimeFactory runtimeFactory,
            IDockService docks,
            IPromptTemplateService promptTemplates,
            LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _PromptTemplates = promptTemplates ?? throw new ArgumentNullException(nameof(promptTemplates));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build (or refine, when one already exists) the vessel's Model Context using the given captain.
        /// </summary>
        /// <param name="vesselId">Vessel identifier (vsl_ prefix).</param>
        /// <param name="captainId">Captain identifier (cpt_ prefix) whose runtime performs the analysis.</param>
        /// <param name="notes">Optional operator guidance for the captain to focus on.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated vessel with its new Model Context.</returns>
        public async Task<Vessel> BuildAsync(string vesselId, string captainId, string? notes, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));

            Vessel vessel = await _Database.Vessels.ReadAsync(vesselId).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Vessel not found.");
            Captain captain = await _Database.Captains.ReadAsync(captainId).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Captain not found.");

            bool refine = !String.IsNullOrWhiteSpace(vessel.ModelContext);
            string prompt = await BuildPromptAsync(vessel, notes, refine, token).ConfigureAwait(false);

            // Provision a worktree so the captain has the repository available to inspect. A unique branch keeps
            // this analysis isolated from any real work; the dock is always reclaimed in the finally block.
            string branchName = "armada/context/" + Guid.NewGuid().ToString("N").Substring(0, 12);
            Dock dock = await _Docks.ProvisionAsync(vessel, captain, branchName, null, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Could not provision a worktree for this vessel.");

            if (String.IsNullOrWhiteSpace(dock.WorktreePath) || !Directory.Exists(dock.WorktreePath))
            {
                try { await _Docks.ReclaimAsync(dock.Id, vessel.TenantId, CancellationToken.None).ConfigureAwait(false); } catch { }
                throw new InvalidOperationException("The provisioned worktree is missing on disk.");
            }

            string finalMessageFilePath = Path.Combine(Path.GetTempPath(), "armada-context-" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                string context = await RunCaptainAsync(captain, dock.WorktreePath!, prompt, finalMessageFilePath, token).ConfigureAwait(false);

                vessel.ModelContext = context;
                vessel.EnableModelContext = true;
                vessel = await _Database.Vessels.UpdateAsync(vessel).ConfigureAwait(false);

                _Logging.Debug(_Header + (refine ? "refined" : "built") + " Model Context for vessel " + vesselId +
                    " using captain " + captainId + " (" + context.Length + " chars)");
                return vessel;
            }
            finally
            {
                try { if (File.Exists(finalMessageFilePath)) File.Delete(finalMessageFilePath); } catch { }
                try { await _Docks.ReclaimAsync(dock.Id, vessel.TenantId, CancellationToken.None).ConfigureAwait(false); } catch (Exception ex) { _Logging.Warn(_Header + "failed to reclaim context dock " + dock.Id + ": " + ex.ToString()); }
            }
        }

        #endregion

        #region Private-Methods

        private async Task<string> BuildPromptAsync(Vessel vessel, string? notes, bool refine, CancellationToken token)
        {
            string basePrompt = _FallbackPrompt;
            try
            {
                PromptTemplate? template = await _PromptTemplates.ResolveAsync("vessel.build_context", token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(template?.Content)) basePrompt = template!.Content;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not resolve vessel.build_context template; using fallback: " + ex.ToString());
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(basePrompt.Trim());

            if (refine && !String.IsNullOrWhiteSpace(vessel.ModelContext))
            {
                builder.AppendLine();
                builder.AppendLine("## Existing Model Context");
                builder.AppendLine("Refine and improve the document below rather than starting from scratch; correct anything stale and fill gaps.");
                builder.AppendLine();
                builder.AppendLine(vessel.ModelContext!.Trim());
            }

            if (!String.IsNullOrWhiteSpace(vessel.ProjectContext) || !String.IsNullOrWhiteSpace(vessel.StyleGuide))
            {
                builder.AppendLine();
                builder.AppendLine("## Operator-provided project notes (background; the repository is the source of truth)");
                if (!String.IsNullOrWhiteSpace(vessel.ProjectContext)) builder.AppendLine(vessel.ProjectContext!.Trim());
                if (!String.IsNullOrWhiteSpace(vessel.StyleGuide)) builder.AppendLine(vessel.StyleGuide!.Trim());
            }

            if (!String.IsNullOrWhiteSpace(notes))
            {
                builder.AppendLine();
                builder.AppendLine("## Focus for this run (operator guidance)");
                builder.AppendLine(notes!.Trim());
            }

            return builder.ToString();
        }

        private async Task<string> RunCaptainAsync(Captain captain, string workingDirectory, string prompt, string finalMessageFilePath, CancellationToken token)
        {
            IAgentRuntime runtime;
            try
            {
                runtime = _RuntimeFactory.Create(captain.Runtime);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("This captain's runtime (" + captain.Runtime + ") could not be launched: " + ex.Message);
            }

            TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            object outputLock = new object();
            StringBuilder output = new StringBuilder();

            // Consume stdout only so CLI stderr banners never leak into the captured context.
            runtime.OnStdoutReceived += (pid, line) =>
            {
                lock (outputLock)
                {
                    if (output.Length < _MaxOutputChars)
                    {
                        output.Append(line);
                        output.Append('\n');
                    }
                }
            };
            runtime.OnProcessExited += (pid, code) => exitSource.TrySetResult(code);

            int processId = await runtime.StartAsync(
                workingDirectory,
                prompt,
                finalMessageFilePath: finalMessageFilePath,
                model: captain.Model,
                captain: captain,
                token: token).ConfigureAwait(false);

            using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeoutCts.CancelAfter(_DefaultTimeoutMs);
                Task finished = await Task.WhenAny(
                    exitSource.Task,
                    Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);

                if (finished != exitSource.Task)
                {
                    try { await runtime.StopAsync(processId, CancellationToken.None).ConfigureAwait(false); } catch { }
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    throw new TimeoutException("Building the Model Context timed out before the captain finished.");
                }
            }

            int? exitCode = await exitSource.Task.ConfigureAwait(false);

            // Prefer the runtime's final-message artifact (Mux/Codex write it); fall back to accumulated stdout
            // (Claude Code prints the answer to stdout in --print mode).
            string context = String.Empty;
            try
            {
                if (File.Exists(finalMessageFilePath))
                {
                    string artifact = await File.ReadAllTextAsync(finalMessageFilePath).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(artifact)) context = artifact.Trim();
                }
            }
            catch { }

            if (String.IsNullOrWhiteSpace(context))
            {
                lock (outputLock) context = output.ToString().Trim();
            }

            if (String.IsNullOrWhiteSpace(context))
            {
                throw new InvalidOperationException(exitCode.HasValue && exitCode.Value != 0
                    ? "The captain exited with code " + exitCode.Value + " before producing any context."
                    : "The captain produced no context.");
            }

            return context;
        }

        #endregion
    }
}
