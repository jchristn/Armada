namespace Armada.Core.Services
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Runs the in-dock Definition-of-Done gate: the vessel's build and unit-test commands executed inside a
    /// mission's own checkout before acceptance. The gate is serialized host-wide (one build/test at a time)
    /// so concurrent captains cannot thrash CPU, memory, or shared build servers, and classifies any failure
    /// as Compile, TestFail, Timeout, or Infra.
    /// </summary>
    public class DefinitionOfDoneGate : IDefinitionOfDoneGate
    {
        #region Private-Members

        private readonly string _Header = "[DefinitionOfDoneGate] ";
        private readonly LoggingModule _Logging;

        // Host-wide serialization: only one Definition-of-Done gate runs at a time across the process.
        private static readonly SemaphoreSlim _HostGate = new SemaphoreSlim(1, 1);

        private const int MaxDetailChars = 2000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public DefinitionOfDoneGate(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<DefinitionOfDoneResult> EvaluateAsync(Vessel vessel, string worktreePath, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            if (!vessel.DefinitionOfDoneEnabled)
                return DefinitionOfDoneResult.SkippedResult("Definition-of-Done gate disabled for vessel");

            string? buildCommand = String.IsNullOrWhiteSpace(vessel.DefinitionOfDoneBuildCommand) ? null : vessel.DefinitionOfDoneBuildCommand!.Trim();
            string? testCommand = String.IsNullOrWhiteSpace(vessel.DefinitionOfDoneTestCommand) ? null : vessel.DefinitionOfDoneTestCommand!.Trim();

            if (buildCommand == null && testCommand == null)
                return DefinitionOfDoneResult.SkippedResult("Definition-of-Done gate enabled but no build or test command configured");

            if (String.IsNullOrWhiteSpace(worktreePath) || !Directory.Exists(worktreePath))
                return new DefinitionOfDoneResult(DefinitionOfDoneOutcomeEnum.Infra, "Mission checkout unavailable at " + (worktreePath ?? "<null>"));

            TimeSpan timeout = TimeSpan.FromSeconds(vessel.DefinitionOfDoneTimeoutSeconds);

            // Host-wide serialization: builds/tests are heavy, so only one gate runs at a time.
            await _HostGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (buildCommand != null)
                {
                    DefinitionOfDoneResult buildResult = await RunPhaseAsync(buildCommand, worktreePath, timeout, true, token).ConfigureAwait(false);
                    if (!buildResult.Passed) return buildResult;
                }

                if (testCommand != null)
                {
                    DefinitionOfDoneResult testResult = await RunPhaseAsync(testCommand, worktreePath, timeout, false, token).ConfigureAwait(false);
                    if (!testResult.Passed) return testResult;
                }

                return new DefinitionOfDoneResult(DefinitionOfDoneOutcomeEnum.Pass, "Definition-of-Done gate passed");
            }
            finally
            {
                _HostGate.Release();
            }
        }

        #endregion

        #region Private-Methods

        private async Task<DefinitionOfDoneResult> RunPhaseAsync(string command, string worktreePath, TimeSpan timeout, bool isBuildPhase, CancellationToken token)
        {
            string phase = isBuildPhase ? "build" : "test";
            bool started = false;
            bool timedOut = false;
            int exitCode = -1;
            string output = String.Empty;

            try
            {
                bool isWindows = OperatingSystem.IsWindows();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = isWindows ? "cmd.exe" : "/bin/sh",
                    WorkingDirectory = worktreePath,
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

                using Process process = new Process { StartInfo = startInfo };

                try
                {
                    started = process.Start();
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + phase + " command failed to start in " + worktreePath + ": " + ex.Message);
                    return new DefinitionOfDoneResult(DefinitionOfDoneOutcomeEnum.Infra, phase + " command failed to start: " + ex.Message);
                }

                if (!started)
                    return new DefinitionOfDoneResult(DefinitionOfDoneOutcomeEnum.Infra, phase + " command failed to start");

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
                    timedOut = true;
                    try { if (!process.HasExited) process.Kill(true); }
                    catch { }
                }

                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                output = (stdout + "\n" + stderr).Trim();
                if (!timedOut) exitCode = process.ExitCode;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + phase + " command errored in " + worktreePath + ": " + ex.Message);
                return new DefinitionOfDoneResult(DefinitionOfDoneOutcomeEnum.Infra, phase + " command errored: " + ex.Message);
            }

            DefinitionOfDoneOutcomeEnum outcome = DefinitionOfDoneClassifier.ClassifyPhase(started, timedOut, exitCode, isBuildPhase);
            string detail = BuildDetail(phase, outcome, exitCode, timedOut, output);

            if (outcome == DefinitionOfDoneOutcomeEnum.Pass)
                _Logging.Debug(_Header + phase + " phase passed in " + worktreePath);
            else
                _Logging.Warn(_Header + phase + " phase classified " + outcome + " in " + worktreePath);

            return new DefinitionOfDoneResult(outcome, detail);
        }

        private static string BuildDetail(string phase, DefinitionOfDoneOutcomeEnum outcome, int exitCode, bool timedOut, string output)
        {
            string header;
            if (timedOut) header = phase + " phase timed out";
            else if (outcome == DefinitionOfDoneOutcomeEnum.Pass) header = phase + " phase passed";
            else header = phase + " phase exited " + exitCode + " (" + outcome + ")";

            if (String.IsNullOrWhiteSpace(output)) return header;

            string tail = output.Length > MaxDetailChars
                ? output.Substring(output.Length - MaxDetailChars)
                : output;
            return header + ": " + tail;
        }

        #endregion
    }
}
