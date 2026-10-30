namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="RuntimeDetectionService"/>: PATH-based command probing,
    /// runtime enumeration, and per-runtime install hints. Positive cases cover successful
    /// detection and hint text; negative cases cover unavailable and empty command names. One POSIX
    /// case covers a probe that stays alive past the five-second limit. Windows skips that case.
    /// </summary>
    public sealed class RuntimeDetectionServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string PosixFixtureSkipReason = "This case uses a POSIX shell process fixture, so it does not run on Windows.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Runtime Detection Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("is_command_available_git_returns_true", "IsCommandAvailable Git ReturnsTrue", TestTags.Positive, () =>
            {
                bool result = RuntimeDetectionService.IsCommandAvailable("git");
                AssertTrue(result);
            }));

            cases.Add(Case("is_command_available_nonexistent_returns_false", "IsCommandAvailable NonExistentCommand ReturnsFalse", TestTags.Negative, () =>
            {
                bool result = RuntimeDetectionService.IsCommandAvailable("armada_definitely_nonexistent_cmd_xyz");
                AssertFalse(result);
            }));

            cases.Add(Case("is_command_available_empty_returns_false", "IsCommandAvailable EmptyCommand ReturnsFalse", TestTags.Negative, () =>
            {
                // An empty file name causes Process.Start to throw; the service swallows it and reports unavailable.
                bool result = RuntimeDetectionService.IsCommandAvailable("");
                AssertFalse(result);
            }));

            bool skipPosixFixture = OperatingSystem.IsWindows();
            string? posixSkipReason = skipPosixFixture ? PosixFixtureSkipReason : null;
            cases.Add(Case("is_command_available_long_probe_returns_false", "IsCommandAvailable rejects a probe that exceeds its limit", TestTags.Negative, () =>
            {
                string dir = TestTemp.NewDirectory("runtime-probe");
                string script = Path.Combine(dir, "slow-probe");
                string pidPath = Path.Combine(dir, "child.pid");
                string quotedPid = "'" + pidPath.Replace("'", "'\\''") + "'";
                File.WriteAllText(script, "#!/bin/sh\necho $$ > " + quotedPid + "\ncommand sleep 30\n");
                ProcessStartInfo chmodInfo = new ProcessStartInfo("chmod");
                chmodInfo.ArgumentList.Add("+x");
                chmodInfo.ArgumentList.Add(script);
                using (Process chmod = Process.Start(chmodInfo) ?? throw new InvalidOperationException("chmod did not start."))
                {
                    chmod.WaitForExit();
                    AssertEqual(0, chmod.ExitCode, "the probe script is executable");
                }

                try
                {
                    int processId = 0;
                    bool result = RuntimeDetectionService.IsCommandAvailable(script);
                    AssertTrue(TryReadProcessId(pidPath, out processId), "the probe process started");
                    bool stopped = WaitUntil(() => !ProcessIsRunning(processId), TimeSpan.FromSeconds(2));
                    AssertTrue(!result && stopped, "a probe that exceeds five seconds is unavailable and its process is stopped; available=" + result + " stopped=" + stopped);
                }
                finally
                {
                    StopRecordedProcess(pidPath);
                    TestTemp.TryDelete(dir);
                }
            }, skipPosixFixture, posixSkipReason));

            cases.Add(Case("detect_all_runtimes_does_not_throw", "DetectAllRuntimes DoesNotThrow", TestTags.Positive, () =>
            {
                List<AgentRuntimeEnum> runtimes = RuntimeDetectionService.DetectAllRuntimes();
                AssertNotNull(runtimes);
            }));

            cases.Add(Case("detect_default_runtime_does_not_throw", "DetectDefaultRuntime DoesNotThrow", TestTags.Positive, () =>
            {
                AgentRuntimeEnum? runtime = RuntimeDetectionService.DetectDefaultRuntime();
                // Result is environment-dependent, no assertion on value
            }));

            cases.Add(Case("get_install_hint_claude_code_returns_npm_command", "GetInstallHint ClaudeCode ReturnsNpmCommand", TestTags.Positive, () =>
            {
                string hint = RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.ClaudeCode);
                AssertContains("npm install", hint);
                AssertContains("claude-code", hint);
            }));

            cases.Add(Case("get_install_hint_codex_returns_npm_command", "GetInstallHint Codex ReturnsNpmCommand", TestTags.Positive, () =>
            {
                string hint = RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.Codex);
                AssertContains("npm install", hint);
                AssertContains("codex", hint);
            }));

            cases.Add(Case("get_install_hint_custom_returns_generic_message", "GetInstallHint Custom ReturnsGenericMessage", TestTags.Positive, () =>
            {
                string hint = RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.Custom);
                AssertContains("documentation", hint);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.RuntimeDetectionService",
                displayName: "Runtime Detection Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(timeout);
            while (!deadline.Passed)
            {
                if (condition()) return true;
                Thread.Sleep(50);
            }

            return condition();
        }

        private static bool TryReadProcessId(string path, out int processId)
        {
            processId = 0;
            try
            {
                if (!File.Exists(path)) return false;
                return Int32.TryParse(File.ReadAllText(path).Trim(), out processId) && processId > 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void StopRecordedProcess(string path)
        {
            if (TryReadProcessId(path, out int processId))
                StopProcessTree(processId);
        }

        private static bool ProcessIsRunning(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static void StopProcessTree(int processId)
        {
            if (processId <= 0) return;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
            }
            catch (ArgumentException)
            {
                // The process has already exited.
            }
            catch (InvalidOperationException)
            {
                // The process has already exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The process has already exited.
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body, bool skip = false, string? skipReason = null)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.RuntimeDetectionService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag },
                skip: skip,
                skipReason: skipReason);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.RuntimeDetectionService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
