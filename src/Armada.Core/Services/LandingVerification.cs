namespace Armada.Core.Services
{
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>Checks the integrated commit on its owning host and records an immutable verification receipt.</summary>
    public static class LandingVerification
    {
        /// <summary>The smoke test gate for Armada itself (src/Test.Smoke), run in addition to its configured checks.</summary>
        public const string ArmadaCommand = "dotnet test \"src/Test.Smoke/Test.Smoke.csproj\" -c Release -f net10.0";

        /// <summary>Resolve landing checks without changing the user's workflow profile.</summary>
        public static List<string> Commands(WorkflowProfile? profile, Vessel? vessel)
        {
            List<string> commands = new();
            foreach (string? command in new[] { profile?.BuildCommand, profile?.UnitTestCommand, profile?.IntegrationTestCommand })
                if (!String.IsNullOrWhiteSpace(command) && !commands.Contains(command.Trim())) commands.Add(command.Trim());
            string repository = (vessel?.RepoUrl ?? "").TrimEnd('/');
            if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repository = repository[..^4];
            if (repository.Equals("https://github.com/jchristn/Armada", StringComparison.OrdinalIgnoreCase)
                || repository.Equals("git@github.com:jchristn/Armada", StringComparison.OrdinalIgnoreCase))
                if (!commands.Contains(ArmadaCommand)) commands.Add(ArmadaCommand);
            return commands;
        }

        /// <summary>Verify exactly HEAD, refuse timeouts, failures or mutations, and persist every result before publishing.</summary>
        public static async Task<string> VerifyAsync(DatabaseDriver database, IGitService git, IHostCommandExecutor executor,
            string path, bool windows, IReadOnlyList<string> commands, int timeoutMinutes, Mission? mission = null,
            MergeEntry? entry = null, CancellationToken token = default)
        {
            string sha = mission?.CommitHash ?? "unresolved";
            bool initiallyClean;
            try
            {
                sha = await git.GetHeadCommitHashAsync(path, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Landing verification cannot resolve the integration commit.");
                initiallyClean = (await git.GetWorkingTreeStatusAsync(path, token).ConfigureAwait(false)).IsClean;
            }
            catch (Exception ex)
            {
                await RecordAsync("CouldNotRun", null, null, ex.Message, DateTime.UtcNow).ConfigureAwait(false);
                throw;
            }
            if (!initiallyClean)
            {
                await RecordAsync("Failed", null, null, "The integration dock has uncommitted changes; they are not included in the commit to publish.", DateTime.UtcNow).ConfigureAwait(false);
                throw new InvalidOperationException("The integration dock has uncommitted changes. Commit or resolve them before verifying and landing.");
            }
            DateTime deadline = DateTime.UtcNow.AddMinutes(timeoutMinutes);
            if (commands.Count == 0)
                await RecordAsync("Skipped", null, null, "No landing checks configured.", DateTime.UtcNow).ConfigureAwait(false);
            foreach (string command in commands)
            {
                DateTime started = DateTime.UtcNow;
                int remaining = (int)Math.Max(1, (deadline - started).TotalMilliseconds);
                HostCommandResult result;
                await CommandAudit.RecordAsync(database, new CommandAuditRecord
                {
                    Source = entry == null ? "LandingVerification" : "MergeQueueTest", Command = command, WorkingDirectory = path,
                    TenantId = mission?.TenantId ?? entry?.TenantId, UserId = mission?.UserId ?? entry?.UserId,
                    VesselId = mission?.VesselId ?? entry?.VesselId, EntityType = entry == null ? "Mission" : "MergeEntry", EntityId = entry?.Id ?? mission?.Id
                }, token: token).ConfigureAwait(false);
                try
                {
                    if (started >= deadline) throw new TimeoutException("Landing verification budget exhausted.");
                    result = await executor.RunAsync(new HostCommandRequest
                    {
                        Executable = windows ? "cmd.exe" : "/bin/sh",
                        Arguments = windows ? new() { "/c", command } : new() { "-lc", command },
                        WorkingDirectory = path, TimeoutMs = remaining, Kind = HostCommandKindEnum.CheckRun
                    }, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await RecordAsync("CouldNotRun", command, null, ex.Message, started).ConfigureAwait(false);
                    throw;
                }
                string output = SecretRedactor.Redact(result.StandardOutput + result.StandardError);
                bool unchanged = String.Equals(sha, await git.GetHeadCommitHashAsync(path, token).ConfigureAwait(false), StringComparison.Ordinal);
                bool clean = (await git.GetWorkingTreeStatusAsync(path, token).ConfigureAwait(false)).IsClean;
                bool passed = !result.TimedOut && result.ExitCode == 0 && unchanged && clean;
                string status = result.TimedOut ? "TimedOut" : passed ? "Passed" : "Failed";
                await RecordAsync(status, command, result.ExitCode, output, started).ConfigureAwait(false);
                if (!passed) throw new LandingVerificationException("Landing verification " + status + " for commit " + sha + ": " + command
                    + (!unchanged || !clean ? ". The check changed the commit or working tree; preserve and review the changes before retrying." : ". " + output[^Math.Min(4000, output.Length)..]),
                    status, command, result.TimedOut ? null : result.ExitCode, sha, !unchanged || !clean);
            }
            return sha;

            async Task RecordAsync(string status, string? command, int? exitCode, string output, DateTime started)
            {
                output = SecretRedactor.Redact(output);
                string receipt = JsonSerializer.Serialize(new { Status = status, CommitHash = sha, Command = command == null ? null : SecretRedactor.Redact(command), ExitCode = exitCode,
                    StartedUtc = started, CompletedUtc = DateTime.UtcNow, Output = output[..Math.Min(16000, output.Length)] });
                await database.Events.CreateAsync(new ArmadaEvent("mission.landing_verification", "Landing verification " + status + " at " + sha)
                {
                    TenantId = mission?.TenantId ?? entry?.TenantId, UserId = mission?.UserId ?? entry?.UserId,
                    EntityType = entry == null ? "mission" : "merge_entry", EntityId = entry?.Id ?? mission?.Id,
                    MissionId = mission?.Id ?? entry?.MissionId, VesselId = mission?.VesselId ?? entry?.VesselId,
                    VoyageId = mission?.VoyageId,
                    Payload = receipt
                }, token).ConfigureAwait(false);
                if (entry != null)
                {
                    entry.TestExitCode = exitCode;
                    entry.TestOutput = receipt;
                    if (!await database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false))
                        throw new OperationCanceledException("The merge entry changed during verification; publication was refused.");
                }
            }
        }
    }
}
