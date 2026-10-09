namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Executes structured check runs using workflow profiles and persists the results. A run executes where the vessel's
    /// checkout lives (see <see cref="VesselHostResolver"/>): in its working directory on the Admiral host, or, when the
    /// Admiral has none, in the checkout of a connected Harbor that can serve the vessel, with the command, its output,
    /// and the expected artifacts carried over the Harbor's link.
    /// </summary>
    public class CheckRunService
    {
        /// <summary>
        /// Optional callback invoked whenever a check run is created or updated.
        /// </summary>
        public Action<CheckRun>? OnCheckRunChanged { get; set; }

        /// <summary>
        /// Finds where a vessel's checkout lives. When null, runs use the working directory on the Admiral host only.
        /// </summary>
        public VesselHostResolver? Hosts { get; set; } = null;

        private readonly string _Header = "[CheckRunService] ";
        private readonly DatabaseDriver _Database;
        private readonly WorkflowProfileService _WorkflowProfiles;
        private readonly VesselReadinessService _Readiness;
        private readonly LoggingModule _Logging;
        private readonly TimeSpan _DefaultTimeout = TimeSpan.FromMinutes(30);
        private const long _MaxArtifactCopyBytes = 16L * 1024L * 1024L;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CheckRunService(
            DatabaseDriver database,
            WorkflowProfileService workflowProfiles,
            VesselReadinessService readiness,
            LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _WorkflowProfiles = workflowProfiles ?? throw new ArgumentNullException(nameof(workflowProfiles));
            _Readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Execute a check run synchronously and persist the result.
        /// </summary>
        public async Task<CheckRun> RunAsync(AuthContext auth, CheckRunRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.VesselId)) throw new ArgumentNullException(nameof(request.VesselId));

            Vessel vessel = await ReadAccessibleVesselAsync(auth, request.VesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Vessel not found or not accessible.");

            // Where the checkout is: the Admiral host, or a connected Harbor. Throws VesselCheckoutUnavailableException,
            // whose message says what to set, when there is none.
            VesselHost host = Hosts != null
                ? await Hosts.ResolveAsync(vessel, auth.UserId, token).ConfigureAwait(false)
                : VesselHostResolver.LocalOrThrow(vessel, _Logging);

            VesselReadinessResult readiness = await _Readiness.EvaluateAsync(
                auth,
                vessel,
                host,
                request.WorkflowProfileId,
                String.IsNullOrWhiteSpace(request.CommandOverride) ? request.Type : null,
                request.EnvironmentName,
                includeWorkflowRequirements: String.IsNullOrWhiteSpace(request.CommandOverride),
                token: token).ConfigureAwait(false);
            if (!readiness.IsReady)
            {
                string message = String.Join(" ", readiness.Issues
                    .Where(issue => issue.Severity == ReadinessSeverityEnum.Error)
                    .Select(issue => issue.Message)
                    .Distinct(StringComparer.Ordinal));
                throw new InvalidOperationException(String.IsNullOrWhiteSpace(message)
                    ? "This vessel is not ready for the requested check run."
                    : message);
            }

            WorkflowProfile? profile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, request.WorkflowProfileId, token).ConfigureAwait(false);
            if (profile == null && String.IsNullOrWhiteSpace(request.CommandOverride))
                throw new InvalidOperationException("No active workflow profile could be resolved for this vessel.");

            string command = !String.IsNullOrWhiteSpace(request.CommandOverride)
                ? request.CommandOverride!.Trim()
                : _WorkflowProfiles.ResolveCommand(profile!, request.Type, request.EnvironmentName)
                    ?? throw new InvalidOperationException("No command is configured for " + request.Type + ".");

            CheckRun run = new CheckRun
            {
                TenantId = vessel.TenantId,
                UserId = auth.UserId,
                WorkflowProfileId = profile?.Id,
                VesselId = vessel.Id,
                MissionId = request.MissionId,
                VoyageId = request.VoyageId,
                DeploymentId = request.DeploymentId,
                Label = request.Label,
                Type = request.Type,
                Source = CheckRunSourceEnum.Armada,
                Status = CheckRunStatusEnum.Running,
                EnvironmentName = request.EnvironmentName,
                Command = command,
                WorkingDirectory = host.WorkingDirectory,
                BranchName = request.BranchName,
                CommitHash = request.CommitHash,
                StartedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            run = await _Database.CheckRuns.CreateAsync(run, token).ConfigureAwait(false);
            OnCheckRunChanged?.Invoke(run);

            await CommandAudit.RecordAsync(_Database, new CommandAuditRecord
            {
                Source = "CheckRun",
                Command = command,
                WorkingDirectory = run.WorkingDirectory,
                Host = host.HostLabel,
                TenantId = run.TenantId,
                UserId = run.UserId,
                VesselId = run.VesselId,
                EntityType = "CheckRun",
                EntityId = run.Id
            }, _Logging, token).ConfigureAwait(false);

            Stopwatch sw = Stopwatch.StartNew();
            CommandExecutionResult execution;

            try
            {
                execution = host.IsHarbor
                    ? await ExecuteOnHostAsync(host, run.Command, _DefaultTimeout, token).ConfigureAwait(false)
                    : await ExecuteCommandAsync(run.Command, run.WorkingDirectory!, _DefaultTimeout, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                execution = new CommandExecutionResult
                {
                    ExitCode = -1,
                    Output = ex.Message
                };
            }

            sw.Stop();

            run.ExitCode = execution.ExitCode;
            run.Output = execution.Output;
            run.DurationMs = Convert.ToInt64(Math.Round(sw.Elapsed.TotalMilliseconds));
            run.CompletedUtc = DateTime.UtcNow;
            run.LastUpdateUtc = DateTime.UtcNow;
            run.Status = execution.ExitCode == 0 ? CheckRunStatusEnum.Passed : CheckRunStatusEnum.Failed;
            if (host.IsHarbor)
            {
                await ParseHarborResultsAsync(host, run, profile?.ExpectedArtifacts, token).ConfigureAwait(false);
            }
            else
            {
                run.Artifacts = CollectArtifacts(run.WorkingDirectory!, profile?.ExpectedArtifacts);
                run.TestSummary = CheckRunParsingService.ParseTestSummary(run.Output, run.WorkingDirectory, run.Artifacts);
                run.CoverageSummary = CheckRunParsingService.ParseCoverageSummary(run.WorkingDirectory, run.Artifacts);
            }

            run.Summary = BuildSummary(run, profile);

            run = await _Database.CheckRuns.UpdateAsync(run, token).ConfigureAwait(false);
            OnCheckRunChanged?.Invoke(run);
            return run;
        }

        /// <summary>
        /// Import an externally-executed check run into Armada history.
        /// </summary>
        public async Task<CheckRun> ImportAsync(AuthContext auth, CheckRunImportRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.VesselId)) throw new ArgumentNullException(nameof(request.VesselId));

            Vessel vessel = await ReadAccessibleVesselAsync(auth, request.VesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Vessel not found or not accessible.");

            WorkflowProfile? profile = await ResolveImportProfileAsync(auth, vessel, request.WorkflowProfileId, token).ConfigureAwait(false);
            CheckRun run = BuildImportedRun(auth, vessel, profile, request);
            run = await _Database.CheckRuns.CreateAsync(run, token).ConfigureAwait(false);
            OnCheckRunChanged?.Invoke(run);
            return run;
        }

        /// <summary>
        /// Import an externally-executed check run, updating an existing record when provider and external ID match.
        /// </summary>
        public async Task<CheckRun> ImportOrUpdateAsync(AuthContext auth, CheckRunImportRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.VesselId)) throw new ArgumentNullException(nameof(request.VesselId));

            Vessel vessel = await ReadAccessibleVesselAsync(auth, request.VesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Vessel not found or not accessible.");

            WorkflowProfile? profile = await ResolveImportProfileAsync(auth, vessel, request.WorkflowProfileId, token).ConfigureAwait(false);
            CheckRun run = BuildImportedRun(auth, vessel, profile, request);

            CheckRun? existing = await FindImportedRunAsync(auth, vessel.Id, run.ProviderName, run.ExternalId, token).ConfigureAwait(false);
            if (existing == null)
            {
                run = await _Database.CheckRuns.CreateAsync(run, token).ConfigureAwait(false);
            }
            else
            {
                run.Id = existing.Id;
                run.CreatedUtc = existing.CreatedUtc;
                run.LastUpdateUtc = DateTime.UtcNow;
                run = await _Database.CheckRuns.UpdateAsync(run, token).ConfigureAwait(false);
            }

            OnCheckRunChanged?.Invoke(run);
            return run;
        }

        /// <summary>
        /// Persist a completed Armada-generated check run without executing a shell command.
        /// </summary>
        public async Task<CheckRun> RecordCompletedAsync(CheckRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (String.IsNullOrWhiteSpace(run.VesselId))
                throw new ArgumentNullException(nameof(run.VesselId));

            if (run.StartedUtc == null)
                run.StartedUtc = DateTime.UtcNow;
            if (run.CompletedUtc == null)
                run.CompletedUtc = DateTime.UtcNow;
            if (!run.DurationMs.HasValue && run.CompletedUtc.HasValue && run.StartedUtc.HasValue)
            {
                run.DurationMs = Convert.ToInt64(Math.Round((run.CompletedUtc.Value - run.StartedUtc.Value).TotalMilliseconds));
            }

            run.CreatedUtc = run.CreatedUtc == default ? DateTime.UtcNow : run.CreatedUtc;
            run.LastUpdateUtc = DateTime.UtcNow;
            if (String.IsNullOrWhiteSpace(run.Summary))
                run.Summary = BuildSummary(run, null);

            CheckRun created = await _Database.CheckRuns.CreateAsync(run, token).ConfigureAwait(false);
            OnCheckRunChanged?.Invoke(created);
            return created;
        }

        /// <summary>
        /// Retry a previously completed check run.
        /// </summary>
        public async Task<CheckRun> RetryAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            CheckRunQuery scope = BuildScopeQuery(auth);
            CheckRun? prior = await _Database.CheckRuns.ReadAsync(id, scope, token).ConfigureAwait(false);
            if (prior == null) throw new KeyNotFoundException("Check run not found.");

            return await RunAsync(auth, new CheckRunRequest
            {
                VesselId = prior.VesselId ?? String.Empty,
                WorkflowProfileId = prior.WorkflowProfileId,
                MissionId = prior.MissionId,
                VoyageId = prior.VoyageId,
                DeploymentId = prior.DeploymentId,
                Type = prior.Type,
                EnvironmentName = prior.EnvironmentName,
                Label = prior.Label,
                BranchName = prior.BranchName,
                CommitHash = prior.CommitHash,
                CommandOverride = prior.Command
            }, token).ConfigureAwait(false);
        }

        private CheckRunQuery BuildScopeQuery(AuthContext auth)
        {
            return new CheckRunQuery
            {
                TenantId = auth.IsAdmin ? null : auth.TenantId,
                UserId = auth.IsAdmin || auth.IsTenantAdmin ? null : auth.UserId
            };
        }

        private async Task<WorkflowProfile?> ResolveImportProfileAsync(
            AuthContext auth,
            Vessel vessel,
            string? workflowProfileId,
            CancellationToken token)
        {
            WorkflowProfile? profile = null;
            if (!String.IsNullOrWhiteSpace(workflowProfileId))
            {
                profile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, workflowProfileId, token).ConfigureAwait(false);
                if (profile == null)
                    throw new KeyNotFoundException("The supplied workflow profile is not accessible for this vessel.");
            }

            return profile;
        }

        private CheckRun BuildImportedRun(AuthContext auth, Vessel vessel, WorkflowProfile? profile, CheckRunImportRequest request)
        {
            DateTime timestamp = request.CompletedUtc?.ToUniversalTime()
                ?? request.StartedUtc?.ToUniversalTime()
                ?? DateTime.UtcNow;
            string command = !String.IsNullOrWhiteSpace(request.Command)
                ? request.Command.Trim()
                : request.Type + " (external)";

            CheckRun run = new CheckRun
            {
                TenantId = vessel.TenantId,
                UserId = auth.UserId,
                WorkflowProfileId = profile?.Id,
                VesselId = vessel.Id,
                MissionId = request.MissionId,
                VoyageId = request.VoyageId,
                DeploymentId = request.DeploymentId,
                Label = request.Label,
                Type = request.Type,
                Source = CheckRunSourceEnum.External,
                Status = request.Status,
                ProviderName = NormalizeValue(request.ProviderName),
                ExternalId = NormalizeValue(request.ExternalId),
                ExternalUrl = NormalizeValue(request.ExternalUrl),
                EnvironmentName = NormalizeValue(request.EnvironmentName),
                Command = command,
                WorkingDirectory = vessel.WorkingDirectory,
                BranchName = NormalizeValue(request.BranchName),
                CommitHash = NormalizeValue(request.CommitHash),
                ExitCode = request.ExitCode,
                Output = request.Output,
                Summary = NormalizeValue(request.Summary),
                TestSummary = request.TestSummary ?? CheckRunParsingService.ParseTestSummary(request.Output),
                CoverageSummary = request.CoverageSummary,
                Artifacts = request.Artifacts ?? new List<CheckRunArtifact>(),
                DurationMs = request.DurationMs,
                StartedUtc = request.StartedUtc?.ToUniversalTime(),
                CompletedUtc = request.CompletedUtc?.ToUniversalTime(),
                CreatedUtc = timestamp,
                LastUpdateUtc = DateTime.UtcNow
            };

            if (String.IsNullOrWhiteSpace(run.Summary))
                run.Summary = BuildSummary(run, profile);

            return run;
        }

        private async Task<CheckRun?> FindImportedRunAsync(
            AuthContext auth,
            string vesselId,
            string? providerName,
            string? externalId,
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(providerName) || String.IsNullOrWhiteSpace(externalId))
                return null;

            CheckRunQuery query = BuildScopeQuery(auth);
            query.VesselId = vesselId;
            query.Source = CheckRunSourceEnum.External;
            query.ProviderName = providerName;
            query.ExternalId = externalId;
            query.PageNumber = 1;
            query.PageSize = 5;

            EnumerationResult<CheckRun> results = await _Database.CheckRuns.EnumerateAsync(query, token).ConfigureAwait(false);
            return results.Objects.FirstOrDefault();
        }

        private async Task<Vessel?> ReadAccessibleVesselAsync(AuthContext auth, string vesselId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Vessels.ReadAsync(auth.TenantId!, vesselId, token).ConfigureAwait(false);
            return await _Database.Vessels.ReadAsync(auth.TenantId!, auth.UserId!, vesselId, token).ConfigureAwait(false);
        }

        private async Task<CommandExecutionResult> ExecuteCommandAsync(string command, string workingDirectory, TimeSpan timeout, CancellationToken token)
        {
            bool isWindows = OperatingSystem.IsWindows();

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/sh",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (isWindows)
            {
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(command);
            }
            else
            {
                startInfo.ArgumentList.Add("-lc");
                startInfo.ArgumentList.Add(command);
            }

            using Process process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start check command.");

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
                catch
                {
                }

                throw new TimeoutException("Check command timed out after " + timeout.TotalMinutes.ToString("0") + " minutes.");
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            string output = CombineOutput(stdout, stderr);

            _Logging.Debug(_Header + "command exited with code " + process.ExitCode + ": " + FirstNonEmptyLine(stderr, stdout));

            return new CommandExecutionResult
            {
                ExitCode = process.ExitCode,
                Output = output
            };
        }

        /// <summary>
        /// Run the check command through the host's login shell in the checkout (a Harbor, over its link).
        /// </summary>
        private async Task<CommandExecutionResult> ExecuteOnHostAsync(VesselHost host, string command, TimeSpan timeout, CancellationToken token)
        {
            int timeoutMs = Convert.ToInt32(Math.Min(timeout.TotalMilliseconds, Int32.MaxValue));
            HostCommandResult result = await host.Commands.RunAsync(host.BuildShellCommand(command, timeoutMs, true), token).ConfigureAwait(false);
            if (result.TimedOut)
                throw new TimeoutException("Check command timed out after " + timeout.TotalMinutes.ToString("0") + " minutes on " + host.HostLabel + ".");

            _Logging.Debug(_Header + "command on " + host.HostLabel + " exited with code " + result.ExitCode + ": " + FirstNonEmptyLine(result.StandardError, result.StandardOutput));
            return new CommandExecutionResult
            {
                ExitCode = result.ExitCode,
                Output = CombineOutput(result.StandardOutput, result.StandardError)
            };
        }

        /// <summary>
        /// Collect the expected artifacts from the checkout on a Harbor and parse test and coverage results from them: the
        /// artifacts are stat'ed on the Harbor, copied (up to 16 MB each) into a temporary folder here, and parsed exactly as
        /// artifacts on the Admiral host are.
        /// </summary>
        private async Task ParseHarborResultsAsync(VesselHost host, CheckRun run, List<string>? expectedArtifacts, CancellationToken token)
        {
            List<CheckRunArtifact> artifacts = new List<CheckRunArtifact>();
            string copyRoot = Path.Combine(Path.GetTempPath(), "armada-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (string relativePath in (expectedArtifacts ?? new List<string>()).Where(path => !String.IsNullOrWhiteSpace(path)))
                {
                    try
                    {
                        VesselCheckoutFileInfo info = await host.Files.StatAsync(relativePath, token).ConfigureAwait(false);
                        if (!info.Exists || info.IsDirectory) continue;
                        string normalized = relativePath.Replace('\\', '/');
                        artifacts.Add(new CheckRunArtifact
                        {
                            Path = normalized,
                            SizeBytes = info.SizeBytes ?? 0,
                            LastWriteUtc = info.LastWriteUtc ?? DateTime.UtcNow
                        });

                        if ((info.SizeBytes ?? 0) > _MaxArtifactCopyBytes) continue;
                        string? text = await host.Files.ReadTextAsync(normalized, _MaxArtifactCopyBytes, token).ConfigureAwait(false);
                        if (text == null) continue;
                        string local = Path.GetFullPath(Path.Combine(copyRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                        if (!PathContainment.IsInside(copyRoot, local)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                        await File.WriteAllTextAsync(local, text, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is InvalidOperationException)
                    {
                        _Logging.Debug(_Header + "artifact " + relativePath + " on " + host.HostLabel + " skipped: " + ex.Message);
                    }
                }

                run.Artifacts = artifacts;
                string? parseRoot = Directory.Exists(copyRoot) ? copyRoot : null;
                run.TestSummary = CheckRunParsingService.ParseTestSummary(run.Output, parseRoot, run.Artifacts);
                run.CoverageSummary = CheckRunParsingService.ParseCoverageSummary(parseRoot, run.Artifacts);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(copyRoot)) Directory.Delete(copyRoot, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _Logging.Debug(_Header + "could not delete " + copyRoot + ": " + ex.Message);
                }
            }
        }

        private static string CombineOutput(string stdout, string stderr)
        {
            string trimmedStdout = stdout?.Trim() ?? String.Empty;
            string trimmedStderr = stderr?.Trim() ?? String.Empty;

            if (String.IsNullOrWhiteSpace(trimmedStderr)) return trimmedStdout;
            if (String.IsNullOrWhiteSpace(trimmedStdout)) return trimmedStderr;
            return trimmedStdout + Environment.NewLine + Environment.NewLine + "--- STDERR ---" + Environment.NewLine + trimmedStderr;
        }

        private static string FirstNonEmptyLine(string? primary, string? secondary)
        {
            foreach (string source in new[] { primary ?? String.Empty, secondary ?? String.Empty })
            {
                foreach (string line in source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (!String.IsNullOrWhiteSpace(trimmed))
                        return trimmed;
                }
            }

            return String.Empty;
        }

        private static List<CheckRunArtifact> CollectArtifacts(string workingDirectory, List<string>? expectedArtifacts)
        {
            List<CheckRunArtifact> results = new List<CheckRunArtifact>();
            if (String.IsNullOrWhiteSpace(workingDirectory) || expectedArtifacts == null) return results;

            string root = Path.GetFullPath(workingDirectory);
            foreach (string relativePath in expectedArtifacts.Where(path => !String.IsNullOrWhiteSpace(path)))
            {
                try
                {
                    string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
                    if (!PathContainment.IsInside(root, fullPath)) continue;
                    if (!File.Exists(fullPath)) continue;

                    FileInfo info = new FileInfo(fullPath);
                    results.Add(new CheckRunArtifact
                    {
                        Path = relativePath.Replace('\\', '/'),
                        SizeBytes = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc
                    });
                }
                catch
                {
                }
            }

            return results;
        }

        private static string BuildSummary(CheckRun run, WorkflowProfile? profile)
        {
            string label = !String.IsNullOrWhiteSpace(run.Label)
                ? run.Label!
                : run.Type.ToString();

            string? testDetails = BuildTestSummaryText(run.TestSummary);
            string? coverageDetails = BuildCoverageSummaryText(run.CoverageSummary);

            if (run.Status == CheckRunStatusEnum.Passed)
            {
                List<string> parts = new List<string>();
                if (!String.IsNullOrWhiteSpace(testDetails))
                    parts.Add(testDetails);
                if (run.Artifacts.Count > 0)
                    parts.Add("collected " + run.Artifacts.Count + " artifact(s)");
                if (!String.IsNullOrWhiteSpace(coverageDetails))
                    parts.Add(coverageDetails);

                if (parts.Count > 0)
                    return label + " passed. " + String.Join("; ", parts) + ".";
                return label + " passed.";
            }

            if (!String.IsNullOrWhiteSpace(testDetails))
            {
                if (!String.IsNullOrWhiteSpace(coverageDetails))
                    return label + " failed. " + testDetails + "; " + coverageDetails + ".";
                return label + " failed. " + testDetails + ".";
            }

            string details = FirstNonEmptyLine(run.Output, null);
            if (String.IsNullOrWhiteSpace(details))
                details = "Exit code " + (run.ExitCode?.ToString() ?? "unknown");

            return label + " failed. " + details;
        }

        private static string? BuildTestSummaryText(CheckRunTestSummary? summary)
        {
            if (summary == null)
                return null;

            List<string> parts = new List<string>();
            if (summary.Passed.HasValue)
                parts.Add(summary.Passed.Value + " passed");
            if (summary.Failed.HasValue)
                parts.Add(summary.Failed.Value + " failed");
            if (summary.Skipped.HasValue && summary.Skipped.Value > 0)
                parts.Add(summary.Skipped.Value + " skipped");
            if (summary.Total.HasValue)
                parts.Add(summary.Total.Value + " total");
            if (summary.DurationMs.HasValue)
                parts.Add("in " + FormatDuration(summary.DurationMs.Value));

            return parts.Count > 0 ? String.Join(", ", parts) : null;
        }

        private static string? BuildCoverageSummaryText(CheckRunCoverageSummary? summary)
        {
            if (summary == null)
                return null;

            if (summary.Lines?.Percentage.HasValue == true)
                return "line coverage " + summary.Lines.Percentage.Value.ToString("0.##") + "%";
            if (summary.Statements?.Percentage.HasValue == true)
                return "statement coverage " + summary.Statements.Percentage.Value.ToString("0.##") + "%";
            if (summary.Functions?.Percentage.HasValue == true)
                return "function coverage " + summary.Functions.Percentage.Value.ToString("0.##") + "%";
            if (summary.Branches?.Percentage.HasValue == true)
                return "branch coverage " + summary.Branches.Percentage.Value.ToString("0.##") + "%";
            return null;
        }

        private static string FormatDuration(long durationMs)
        {
            if (durationMs < 1000)
                return durationMs + " ms";

            TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);
            if (duration.TotalMinutes >= 1)
                return duration.TotalMinutes.ToString("0.##") + " min";

            return duration.TotalSeconds.ToString("0.##") + " s";
        }

        private static string? NormalizeValue(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class CommandExecutionResult
        {
            public int ExitCode { get; set; } = -1;
            public string Output { get; set; } = String.Empty;
        }
    }
}
