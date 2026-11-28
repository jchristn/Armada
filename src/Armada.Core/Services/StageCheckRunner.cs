namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Runs stage verification commands and git checks in a dock and commits dock work.
    /// </summary>
    internal sealed class StageCheckRunner
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly DatabaseDriver _Database;
        private readonly IGitService? _Git;
        private readonly string _Header;
        private readonly MissionFlagSet _LeftoverCommitted;
        private readonly LoggingModule _Logging;
        private readonly IPromptTemplateService? _PromptTemplates;
        private readonly ArmadaSettings _Settings;

        // Vessel, base commit and command to whether the command already fails on the base commit.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _BaseCommitCheckFailures = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _StageCheckGates = new();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal StageCheckRunner(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Git = context.Git;
            _Header = context.Header;
            _LeftoverCommitted = context.LeftoverCommitted;
            _Logging = context.Logging;
            _PromptTemplates = context.PromptTemplates;
            _Settings = context.Settings;
        }

        #endregion

        #region Internal-Methods

        /// <inheritdoc />
        internal async Task<string?> VerifyStageWorkAsync(Mission mission, Captain captain, CancellationToken token = default)
        {
            StageCheckResult result = await CheckStageWorkAsync(mission, captain, token).ConfigureAwait(false);
            return result.IsBlocking ? result.FailureMessage : null;
        }

        /// <inheritdoc />
        internal async Task<StageCheckResult> CheckStageWorkAsync(Mission mission, Captain captain, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            DateTime started = DateTime.UtcNow;
            Dock? dock = String.IsNullOrEmpty(mission.DockId) ? null : await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
            SemaphoreSlim gate = _StageCheckGates.GetOrAdd(dock?.HarborId ?? "local", _ => new SemaphoreSlim(_Settings.StageCheckConcurrencyPerHarbor));
            await gate.WaitAsync(token).ConfigureAwait(false);
            StageCheckResult result;
            try
            {
                // Queue time does not consume the execution budget; head and baseline share it.
                DateTime deadline = DateTime.UtcNow.AddMinutes(_Settings.StageCheckTimeoutMinutes);
                result = await RunStageCheckAsync(mission, captain, deadline, token).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
            result.CheckedUtc = DateTime.UtcNow;
            result.DurationMs = (long)(result.CheckedUtc - started).TotalMilliseconds;
            return result;
        }

        /// <inheritdoc />
        internal Task CommitDockWorkAsync(Mission mission, Captain captain, Dock dock, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            return CommitLeftoverWorkAsync(mission, captain, dock, token);
        }

        internal async Task CommitLeftoverWorkAsync(Mission mission, Captain captain, Dock dock, CancellationToken token)
        {
            // A judge reports through its verdict and an architect through its plan: neither should change the branch.
            if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect)) return;
            if (String.IsNullOrEmpty(dock.WorktreePath)) return;

            DockHost? harborHost = HarborHostFor(dock);
            IGitService? git = harborHost != null ? harborHost.Git : _Git;
            if (git == null) return;

            try
            {
                if (!await DockDirectoryExistsAsync(harborHost, dock.WorktreePath!, token).ConfigureAwait(false)) return;

                // The rescue is for a captain that never commits. A captain that committed its work this attempt has said
                // what belongs on the branch; what it left behind is typically build output (regenerated XML docs, lock
                // files), and committing that breaks the mission's file scope and sends the work back to rework.
                string? startCommit = _Context.MissionScopeGuard.TryReadDockStartCommit(dock, mission);
                string? headCommit = await git.GetHeadCommitHashAsync(dock.WorktreePath!, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(startCommit) && !String.IsNullOrEmpty(headCommit)
                    && !String.Equals(startCommit, headCommit, StringComparison.OrdinalIgnoreCase))
                {
                    _Logging.Info(_Header + "mission " + mission.Id + ": captain " + captain.Id + " committed its work; leaving any uncommitted changes in dock " + dock.Id + " (typically build output) off the branch");
                    return;
                }

                string message = "Armada: commit work left uncommitted by " + captain.Name + "\n\n"
                    + mission.Title + "\n\nMission " + mission.Id + (String.IsNullOrEmpty(mission.Persona) ? "" : " (" + mission.Persona + ")")
                    + ". The captain finished without committing these changes, so Armada committed them.";
                if (await git.CommitAllAsync(dock.WorktreePath!, message, token).ConfigureAwait(false))
                {
                    _LeftoverCommitted.Set(mission.Id);
                    _Logging.Info(_Header + "mission " + mission.Id + ": captain " + captain.Id + " left uncommitted changes in dock " + dock.Id + "; committed them");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is TimeoutException || ex is IOException)
            {
                _Logging.Warn(_Header + "mission " + mission.Id + ": could not commit leftover changes in dock " + dock.Id + ": " + ex.Message);
            }
        }

        internal DockHost? HarborHostFor(Dock? dock)
        {
            if (_Service.DockHosts == null || !DockHostResolver.IsHarborDock(dock)) return null;
            return _Service.DockHosts.ForDock(dock);
        }

        /// <summary>
        /// Whether a dock's worktree folder exists on its host. A Harbor that is offline or does not answer counts as
        /// "not there", so callers fall back to what the Admiral already has (the diff snapshot).
        /// </summary>
        internal async Task<bool> DockDirectoryExistsAsync(DockHost? harborHost, string path, CancellationToken token)
        {
            if (harborHost == null) return Directory.Exists(path);
            try
            {
                return await harborHost.Files.DirectoryExistsAsync(path, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is TimeoutException || ex is IOException)
            {
                _Logging.Debug(_Header + "could not check dock " + path + " on Harbor " + harborHost.HarborId + ": " + ex.Message);
                return false;
            }
        }

        internal async Task HideTrackedInstructionsFileAsync(IHostCommandExecutor commands, string worktreePath, string instructionsFileName, CancellationToken token)
        {
            // git ignores exclude rules for files it already tracks. When the repository commits its own
            // instruction file (for example CLAUDE.md), mark it skip-worktree in this dock so the generated
            // mission instructions can never be staged or committed by the captain.
            // ls-files --error-unmatch exits 1 for a file git does not track: an answer, not a failure.
            int tracked = await RunGitExitCodeAsync(commands, worktreePath, new int[] { 1 }, token, "ls-files", "--error-unmatch", "--", instructionsFileName).ConfigureAwait(false);
            if (tracked != 0) return;

            int marked = await RunGitExitCodeAsync(commands, worktreePath, null, token, "update-index", "--skip-worktree", "--", instructionsFileName).ConfigureAwait(false);
            if (marked != 0)
            {
                _Logging.Warn(_Header + "could not mark " + instructionsFileName + " skip-worktree in " + worktreePath + " (git exit code " + marked + ")");
            }
        }

        #endregion

        #region Private-Methods

        private async Task<HostCommandResult> RunStageCommandAsync(IHostCommandExecutor executor, bool windows, string workingDirectory, string command, DateTime deadline, CancellationToken token)
        {
            if (DateTime.UtcNow >= deadline) return new HostCommandResult { TimedOut = true };
            return await executor.RunAsync(new HostCommandRequest
            {
                Executable = windows ? "cmd.exe" : "/bin/sh",
                WorkingDirectory = workingDirectory,
                Arguments = windows ? new List<string> { "/c", command } : new List<string> { "-lc", command },
                TimeoutMs = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds),
                Kind = HostCommandKindEnum.CheckRun,
                Label = "Stage check: " + command
            }, token).ConfigureAwait(false);
        }

        private static async Task<HashSet<string>> ReadModifiedTrackedFilesAsync(IHostCommandExecutor executor, bool windows, string worktree, CancellationToken token)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);
            HostCommandResult status = await RunGitAsync(executor, windows, worktree, "git status --porcelain=v1 --untracked-files=no", token).ConfigureAwait(false);
            if (!status.Success) return files;
            foreach (string line in (status.StandardOutput ?? String.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Length > 3) files.Add(line.Substring(3).Trim());
            }
            return files;
        }

        private async Task RevertCheckArtifactsAsync(IHostCommandExecutor executor, bool windows, string worktree, HashSet<string> dirtyBefore, string missionId)
        {
            try
            {
                HashSet<string> dirtyAfter = await ReadModifiedTrackedFilesAsync(executor, windows, worktree, CancellationToken.None).ConfigureAwait(false);
                List<string> regenerated = dirtyAfter.Where(f => !dirtyBefore.Contains(f) && !f.Contains("->", StringComparison.Ordinal)).ToList();
                if (regenerated.Count == 0) return;
                string quoted = String.Join(" ", regenerated.Select(f => "\"" + f.Replace("\"", "") + "\""));
                await RunGitAsync(executor, windows, worktree, "git checkout -- " + quoted, CancellationToken.None).ConfigureAwait(false);
                _Logging.Info(_Header + "stage check for mission " + missionId + " regenerated " + regenerated.Count + " tracked file(s); restored them: " + String.Join(", ", regenerated));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is TimeoutException)
            {
                _Logging.Warn(_Header + "could not restore files the stage check changed for mission " + missionId + ": " + ex.Message);
            }
        }

        private async Task<bool?> FailsOnBaseCommitAsync(IHostCommandExecutor executor, bool windows, string vesselId, string worktree, string baseCommit, string command, DateTime deadline, CancellationToken token)
        {
            string key = vesselId + "|" + baseCommit + "|" + command;
            if (_BaseCommitCheckFailures.TryGetValue(key, out bool known)) return known;

            // Run the same command on the base commit in a throwaway detached worktree next to the dock.
            string baseline = worktree.TrimEnd('/', '\\') + ".base-check";
            bool fails = false;
            try
            {
                await RunGitAsync(executor, windows, worktree, "git worktree remove --force \"" + baseline + "\"", token).ConfigureAwait(false);
                HostCommandResult added = await RunGitAsync(executor, windows, worktree, "git worktree add --detach \"" + baseline + "\" " + baseCommit, token).ConfigureAwait(false);
                if (!added.Success)
                {
                    _Logging.Warn(_Header + "could not create a base-commit worktree to classify a stage check failure: " + (added.StandardError ?? "").Trim());
                    return null;
                }

                HostCommandResult result = await RunStageCommandAsync(executor, windows, baseline, command, deadline, token).ConfigureAwait(false);
                if (result.TimedOut || result.ExitCode == -1 || result.ExitCode == 126 || result.ExitCode == 127) return null;
                fails = result.ExitCode != 0;
            }
            finally
            {
                await RunGitAsync(executor, windows, worktree, "git worktree remove --force \"" + baseline + "\"", CancellationToken.None).ConfigureAwait(false);
            }

            _BaseCommitCheckFailures[key] = fails;
            return fails;
        }

        private static async Task<HostCommandResult> RunGitAsync(IHostCommandExecutor executor, bool windows, string worktree, string command, CancellationToken token)
        {
            return await executor.RunAsync(new HostCommandRequest
            {
                Executable = windows ? "cmd.exe" : "/bin/sh",
                WorkingDirectory = worktree,
                Arguments = windows ? new List<string> { "/c", command } : new List<string> { "-c", command },
                TimeoutMs = 120000,
                Kind = HostCommandKindEnum.CheckRun,
                Label = "Stage check git"
            }, token).ConfigureAwait(false);
        }

        private async Task<StageCheckResult> RunStageCheckAsync(Mission mission, Captain captain, DateTime deadline, CancellationToken token)
        {
            bool checkedRuntime = captain.Runtime == AgentRuntimeEnum.Mux || captain.Runtime == AgentRuntimeEnum.ApiEndpoint;
            if (!checkedRuntime && !_Settings.VerifyAllStageRuntimes) return StageCheckResult.Skip(StageCheckReasonEnum.RuntimeNotChecked);
            if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect)
                || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.ProductManager) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Recorder))
                return StageCheckResult.Skip(StageCheckReasonEnum.ReviewPersona);
            if (String.IsNullOrEmpty(mission.DockId) || String.IsNullOrEmpty(mission.VesselId)) return StageCheckResult.Skip(StageCheckReasonEnum.NoDock);
            // A verifier that reported a blocking product defect is not checked: a failing check is its evidence, and its
            // outcome goes to the Worker as rework rather than back to the verifier to "fix" (HF-25).
            if (VerifierContract.Applies(mission.Persona) && _Service.OnPeekMissionOutput != null && VerifierContract.IsBlocked(_Service.OnPeekMissionOutput(mission.Id)))
            {
                _Logging.Info(_Header + mission.Persona + " mission " + mission.Id + " reported a blocking defect; skipping the stage check");
                return StageCheckResult.Skip(StageCheckReasonEnum.VerifierReportedBlocker);
            }

            StageCheckResult result = new StageCheckResult();
            try
            {
                Dock? dock = await _Database.Docks.ReadAsync(mission.DockId!, token).ConfigureAwait(false);
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId!, token).ConfigureAwait(false);
                if (dock == null || vessel == null || String.IsNullOrEmpty(dock.WorktreePath)) return StageCheckResult.Skip(StageCheckReasonEnum.NoDock);

                // A Harbor whose link is down for a moment (an Admiral restart, a network blip) is waited for: the check
                // not being able to run is an infrastructure condition, not something to send the captain back over.
                if (!await WaitForDockHarborAsync(dock, token).ConfigureAwait(false))
                {
                    result.Status = StageCheckStatusEnum.CouldNotRun;
                    result.Reason = StageCheckReasonEnum.HarborDisconnected;
                    result.FailureMessage = "The stage check could not run because Harbor " + dock.HarborId + " did not reconnect within "
                        + (int)_Service.HarborReconnectWait.TotalSeconds + " seconds. Its success has not been verified; rerun the configured checks once the Harbor is back.";
                    return result;
                }

                AuthContext system = new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = vessel.TenantId };
                WorkflowProfile? profile = await new WorkflowProfileService(_Database, _Logging).ResolveForVesselAsync(system, vessel, null, token).ConfigureAwait(false);
                if (profile == null) return StageCheckResult.Skip(StageCheckReasonEnum.NoWorkflowProfile);
                List<string> commands = new List<string>();
                if (!String.IsNullOrWhiteSpace(profile.BuildCommand)) commands.Add(profile.BuildCommand!.Trim());
                UnitTestScopeEnum unitTestScope = await ResolveStageUnitTestScopeAsync(mission, token).ConfigureAwait(false);
                string? unitTestCommand = UnitTestScopeDirective.SelectCommand(profile, unitTestScope);
                if (unitTestCommand != null)
                {
                    commands.Add(unitTestCommand);
                    result.UnitTestScope = unitTestScope;
                }
                if (commands.Count == 0) return StageCheckResult.Skip(StageCheckReasonEnum.NoCommands);
                result.Commands = commands;

                // A stage that committed nothing has nothing new to verify (a frontier stage that only reviewed). Local
                // models are still checked either way, because they often leave edits uncommitted.
                string? startCommit = _Context.MissionScopeGuard.TryReadDockStartCommit(dock, mission);
                if (!checkedRuntime)
                {
                    string? head = await _Context.StageCompletionHandler.ReadDockHeadAsync(dock, token).ConfigureAwait(false);
                    if (String.IsNullOrEmpty(startCommit) || String.IsNullOrEmpty(head) || String.Equals(startCommit, head, StringComparison.OrdinalIgnoreCase))
                    {
                        StageCheckResult skipped = StageCheckResult.Skip(StageCheckReasonEnum.NoNewCommit);
                        skipped.Commands = commands;
                        return skipped;
                    }
                }

                DockHost? harborHost = HarborHostFor(dock);
                IHostCommandExecutor executor = harborHost != null ? harborHost.Commands : _Context.LocalCommands;
                bool windows = OperatingSystem.IsWindows();
                if (harborHost != null && !String.IsNullOrEmpty(dock.HarborId))
                {
                    Harbor? harbor = await _Database.Harbors.ReadAsync(dock.HarborId!, token).ConfigureAwait(false);
                    windows = FleetActionShellCommandBuilder.IsWindowsPlatform(harbor?.OsPlatform);
                }

                HashSet<string> dirtyBefore = await ReadModifiedTrackedFilesAsync(executor, windows, dock.WorktreePath!, token).ConfigureAwait(false);
                try
                {
                    foreach (string command in commands)
                    {
                        HostCommandResult commandResult = await RunStageCommandAsync(executor, windows, dock.WorktreePath!, command, deadline, token).ConfigureAwait(false);
                        if (commandResult.TimedOut || commandResult.ExitCode == -1 || commandResult.ExitCode == 126 || commandResult.ExitCode == 127)
                        {
                            _Logging.Warn(_Header + "stage check for mission " + mission.Id + " timed out running " + command);
                            result.Status = StageCheckStatusEnum.CouldNotRun;
                            result.Reason = commandResult.TimedOut ? StageCheckReasonEnum.TimedOut : StageCheckReasonEnum.Error;
                            result.FailedCommand = command;
                            result.FailureMessage = !commandResult.TimedOut
                                ? "The stage check command could not start (exit " + commandResult.ExitCode + "): " + command + ". Its success has not been verified."
                                : "The stage check timed out after " + _Settings.StageCheckTimeoutMinutes + " minutes: " + command + ". Its success has not been verified. "
                                + "Investigate and rerun the check. If the command is simply slower than that, an operator can raise stageCheckTimeoutMinutes or give the vessel's workflow profile a narrower test command.";
                            return result;
                        }

                        if (commandResult.ExitCode != 0)
                        {
                            // A check that fails on the stage's starting commit as well is a pre-existing failure, not
                            // something this stage broke: report it in the log and do not send the captain back over it.
                            // With no recorded start commit there is no baseline to compare, so the head failure stands;
                            // only a baseline that was attempted but could not finish is unverified.
                            bool? baseFails = String.IsNullOrEmpty(startCommit) ? false
                                : await FailsOnBaseCommitAsync(executor, windows, vessel.Id, dock.WorktreePath!, startCommit!, command, deadline, token).ConfigureAwait(false);
                            if (baseFails == true)
                            {
                                _Logging.Warn(_Header + "stage check for mission " + mission.Id + " fails on its base commit too: " + command);
                                result.PreExistingFailures.Add(command);
                                continue;
                            }
                            if (baseFails == null)
                            {
                                result.Status = StageCheckStatusEnum.CouldNotRun;
                                result.FailedCommand = command;
                                result.Reason = StageCheckReasonEnum.Error;
                                result.FailureMessage = "The head check failed, but its base could not be verified within the shared budget. Rerun the checks; this is not a confirmed regression.";
                                return result;
                            }

                            string output = StageCheckOutputSummarizer.Summarize((commandResult.StandardOutput ?? "") + "\n" + (commandResult.StandardError ?? ""));
                            _Logging.Warn(_Header + "stage check for mission " + mission.Id + " failed: " + command + " exited " + commandResult.ExitCode);
                            result.Status = StageCheckStatusEnum.Failed;
                            result.FailedCommand = command;
                            result.ExitCode = commandResult.ExitCode;
                            result.FailureMessage = "The stage check failed: `" + command + "` exited with code " + commandResult.ExitCode + " in the working tree. Its output:\n```\n" + output + "\n```\nFix the cause, run the command again until it succeeds, and commit.";
                            return result;
                        }
                    }
                }
                finally
                {
                    // The check's own build can rewrite tracked files (generated XML docs, lock files). Put those back so
                    // the check never adds changes the stage did not make.
                    await RevertCheckArtifactsAsync(executor, windows, dock.WorktreePath!, dirtyBefore, mission.Id).ConfigureAwait(false);
                }

                _Logging.Info(_Header + "stage check for mission " + mission.Id + " passed (" + String.Join("; ", commands) + ")");
                result.Status = StageCheckStatusEnum.Passed;
                return result;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is TimeoutException || ex is System.ComponentModel.Win32Exception)
            {
                _Logging.Warn(_Header + "stage check for mission " + mission.Id + " could not run: " + ex.Message);
                result.Status = StageCheckStatusEnum.CouldNotRun;
                result.Reason = StageCheckReasonEnum.Error;
                result.FailureMessage = "The stage verification could not run: " + ex.Message + ". Restore dependencies and rerun the configured checks.";
                return result;
            }
        }

        private async Task<UnitTestScopeEnum> ResolveStageUnitTestScopeAsync(Mission mission, CancellationToken token)
        {
            // The operator's choice in the mission description wins; otherwise the stage persona's prompt decides.
            UnitTestScopeEnum? fromDescription = UnitTestScopeDirective.Parse(mission.Description);
            if (fromDescription != null) return fromDescription.Value;
            if (String.IsNullOrWhiteSpace(mission.Persona)) return UnitTestScopeEnum.Full;
            try
            {
                Persona? persona = await _Database.Personas.ReadByNameAsync(mission.Persona!, token).ConfigureAwait(false);
                string prompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                    mission.Persona, persona?.PromptTemplateName, new Dictionary<string, string>(), _PromptTemplates, null, token).ConfigureAwait(false);
                return UnitTestScopeDirective.Resolve(null, prompt);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not read the persona prompt for mission " + mission.Id + " to choose its unit-test command; running the full one: " + ex.Message);
                return UnitTestScopeEnum.Full;
            }
        }

        private async Task<bool> WaitForDockHarborAsync(Dock dock, CancellationToken token)
        {
            if (_Service.DockHosts?.Harbors == null || !DockHostResolver.IsHarborDock(dock)) return true;
            DateTime deadline = DateTime.UtcNow + _Service.HarborReconnectWait;
            bool logged = false;
            while (!_Service.DockHosts.Harbors.IsConnected(dock.HarborId!))
            {
                if (DateTime.UtcNow >= deadline) return false;
                if (!logged)
                {
                    _Logging.Info(_Header + "waiting for Harbor " + dock.HarborId + " to reconnect before checking dock " + dock.Id);
                    logged = true;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            }
            return true;
        }

        private static async Task<int> RunGitExitCodeAsync(IHostCommandExecutor commands, string workingDirectory, int[]? expectedExitCodes, CancellationToken token, params string[] args)
        {
            HostCommandResult result = await commands.RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string>(args),
                TimeoutMs = 30000,
                ExpectedExitCodes = expectedExitCodes != null ? new List<int>(expectedExitCodes) : new List<int>()
            }, token).ConfigureAwait(false);
            return result.ExitCode;
        }

        #endregion
    }
}
