namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Covers what the Harbor app's windows rely on outside Avalonia: one menu structure for the menu bar and tray that
    /// opens the Status and Settings windows; which logs it offers (Harbor's own and the jobs run on its machine always,
    /// the Admiral's only when the Admiral is local); the per-job logs Harbor keeps; the job list (what runs, its runtime,
    /// and since when); and listing and restoring previous versions of a settings file.
    /// </summary>
    public sealed class HarborAppSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborApp";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("menus_open_the_same_windows", "The menu bar and tray open the same Status, Logs, and Settings windows; nothing opens a separate manage window", TestTags.Positive, () =>
            {
                foreach (bool isMacOS in new bool[] { true, false })
                {
                    List<HarborMenuEntry> bar = HarborMenuLayout.WindowMenu(isMacOS);
                    List<HarborMenuEntry> tray = HarborMenuLayout.TrayMenu();
                    List<HarborMenuCommandEnum> barCommands = Commands(bar);
                    if (isMacOS) barCommands.AddRange(Commands(HarborMenuLayout.ApplicationMenu()));
                    List<HarborMenuCommandEnum> trayCommands = Commands(tray);

                    foreach (HarborMenuCommandEnum window in new HarborMenuCommandEnum[] { HarborMenuCommandEnum.Status, HarborMenuCommandEnum.Logs, HarborMenuCommandEnum.Settings })
                    {
                        AssertEqual(1, barCommands.Count(c => c == window), window + " appears once in the menu bar (macOS " + isMacOS + ")");
                        AssertEqual(1, trayCommands.Count(c => c == window), window + " appears once in the tray");
                    }

                    // Every command is reachable from the menu bar or the tray; window controls only on macOS.
                    foreach (HarborMenuCommandEnum command in Enum.GetValues(typeof(HarborMenuCommandEnum)))
                    {
                        bool windowControl = command == HarborMenuCommandEnum.MinimizeWindow || command == HarborMenuCommandEnum.CloseWindow;
                        bool reachable = barCommands.Contains(command) || trayCommands.Contains(command);
                        if (windowControl && !isMacOS) AssertFalse(reachable, command + " is macOS-only");
                        else AssertTrue(reachable, command + " is reachable (macOS " + isMacOS + ")");
                    }

                    List<string> bars = bar.Select(entry => entry.Header).ToList();
                    AssertEqual(isMacOS ? "Harbor,View,Window,Help" : "Harbor,View,Help", String.Join(",", bars), "menu bar menus");
                    AssertTrue(bar.All(entry => entry.Children != null), "the menu bar holds only submenus");
                }
            }));

            cases.Add(Case("menu_shortcuts_and_platform_items", "Shortcuts are unique and absent from the tray; Settings and Quit move into the Harbor menu off macOS", TestTags.Positive, () =>
            {
                foreach (bool isMacOS in new bool[] { true, false })
                {
                    List<HarborMenuEntry> entries = Flatten(HarborMenuLayout.WindowMenu(isMacOS));
                    if (isMacOS) entries.AddRange(Flatten(HarborMenuLayout.ApplicationMenu()));
                    List<HarborMenuKeyEnum> keys = entries.Where(e => e.Key != HarborMenuKeyEnum.None).Select(e => e.Key).ToList();
                    AssertEqual(keys.Count, keys.Distinct().Count(), "no shortcut is used twice (macOS " + isMacOS + ")");
                    AssertEqual(HarborMenuKeyEnum.Comma, entries.First(e => e.Command == HarborMenuCommandEnum.Settings).Key, "Settings is Command/Control+,");
                    AssertEqual(HarborMenuKeyEnum.L, entries.First(e => e.Command == HarborMenuCommandEnum.Logs).Key, "Logs is Command/Control+L");

                    List<HarborMenuCommandEnum> harborMenu = Commands(HarborMenuLayout.WindowMenu(isMacOS)[0].Children!);
                    AssertEqual(!isMacOS, harborMenu.Contains(HarborMenuCommandEnum.Settings), "Settings in the Harbor menu only without an app menu");
                    AssertEqual(!isMacOS, harborMenu.Contains(HarborMenuCommandEnum.Quit), "Quit in the Harbor menu only without an app menu");
                }

                List<HarborMenuEntry> tray = HarborMenuLayout.TrayMenu();
                AssertTrue(tray[0].IsStatusLine, "the tray starts with the link status");
                AssertTrue(Flatten(tray).All(e => e.Key == HarborMenuKeyEnum.None), "tray items carry no shortcuts");
                AssertEqual(HarborMenuCommandEnum.Quit, tray[tray.Count - 1].Command, "Quit is last in the tray");
            }));

            cases.Add(CaseAsync("log_sources_follow_where_the_admiral_runs", "Harbor's log and its jobs are always offered; the Admiral's logs only when the Admiral is on this machine", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("harbor-app-logs");
                HarborLogPaths harbor = new HarborLogPaths(Path.Combine(root, "harbor-logs"));
                string dataDirectory = Path.Combine(root, "armada");
                Directory.CreateDirectory(dataDirectory);

                List<LogSource> unknown = LogSourceCatalog.Discover(harbor, null);
                AssertEqual("Harbor,HarborJobs", String.Join(",", unknown.Select(s => s.Kind)), "nothing known about the Admiral yet");

                LocalAdmiralInfo remote = await LocalAdmiralInfo.ResolveAsync("wss://armada.example.com/v1.0/harbor/connect", dataDirectory).ConfigureAwait(false);
                AssertFalse(remote.IsLocal, "an Admiral on another host is not local");
                List<LogSource> remoteSources = LogSourceCatalog.Discover(harbor, remote);
                AssertEqual("Harbor,HarborJobs", String.Join(",", remoteSources.Select(s => s.Kind)), "a remote Admiral still shows Harbor's logs");
                AssertEqual(harbor.JobsDirectory, remoteSources[1].Directory);

                LocalAdmiralInfo local = await LocalAdmiralInfo.ResolveAsync("ws://127.0.0.1:45999/v1.0/harbor/connect", dataDirectory).ConfigureAwait(false);
                AssertTrue(local.IsLocal, "a loopback Admiral with a data directory is local");
                List<LogSource> localSources = LogSourceCatalog.Discover(harbor, local);
                AssertEqual(2 + Enum.GetValues(typeof(LogCategoryEnum)).Length, localSources.Count, "every Admiral log group is added");
                AssertEqual(LogSourceEnum.Harbor, localSources[0].Kind, "Harbor's own log comes first");
                AssertEqual(LogCategoryEnum.Admiral, localSources[2].AdmiralCategory, "then the Admiral's server log");

                Directory.CreateDirectory(harbor.JobsDirectory);
                Touch(Path.Combine(harbor.LogDirectory, "harbor.log.20261007"), 1);
                Touch(Path.Combine(harbor.LogDirectory, "harbor.log.20261008"), 2);
                Touch(Path.Combine(harbor.LogDirectory, "settings.json"), 3);
                Touch(Path.Combine(harbor.JobsDirectory, "msn_a.log"), 4);
                Touch(Path.Combine(harbor.JobsDirectory, "notes.txt"), 5);
                AssertEqual("harbor.log.20261008,harbor.log.20261007", Names(LogSourceCatalog.List(unknown[0], harbor, null)), "Harbor's daily logs, newest first, nothing else");
                AssertEqual("msn_a.log", Names(LogSourceCatalog.List(unknown[1], harbor, remote)), "job logs only");
                AssertEqual(0, LogSourceCatalog.List(localSources[2], harbor, remote).Count, "an Admiral source lists nothing once the Admiral is not local");

                Directory.CreateDirectory(local.Logs.MissionsDirectory);
                File.WriteAllText(local.Logs.MissionLogPath("msn_a"), "admiral copy\n");
                AssertEqual(harbor.MissionLogPath("msn_a"), LogSourceCatalog.ResolveLog("msn_a", harbor, remote), "a remote Admiral: this machine's job log");
                AssertEqual(local.Logs.MissionLogPath("msn_a"), LogSourceCatalog.ResolveLog("msn_a", harbor, local), "a local Admiral: its full transcript");
                AssertEqual(harbor.MissionLogPath("msn_a"), LogSourceCatalog.ResolveLog("msn_a", harbor, null), "unknown Admiral: this machine's job log");
                AssertNull(LogSourceCatalog.ResolveLog("msn_none", harbor, remote), "no log anywhere");
                AssertNull(LogSourceCatalog.ResolveLog("cpt_a", harbor, remote), "captain logs are the Admiral's");
            }));

            cases.Add(Case("job_log_names_and_pruning", "A mission's runs share its log; other jobs get one each; old job logs are pruned newest-first", TestTags.Positive, () =>
            {
                HarborLogPaths paths = new HarborLogPaths(TestTemp.NewDirectory("harbor-app-jobs"));
                DateTime started = new DateTime(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc);
                HarborJobInfo mission = new HarborJobInfo { JobId = "j1", Kind = HarborJobKindEnum.Mission, MissionId = "msn_abc", StartedUtc = started };
                HarborJobInfo ask = new HarborJobInfo { JobId = "0123456789abcdef", Kind = HarborJobKindEnum.AskTurn, CaptainId = "cpt_x", StartedUtc = started };
                HarborJobInfo old = new HarborJobInfo { JobId = "0123456789abcdef", Kind = HarborJobKindEnum.Unknown, StartedUtc = started };
                AssertEqual(Path.Combine(paths.JobsDirectory, "msn_abc.log"), paths.JobLogPath(mission));
                AssertEqual(Path.Combine(paths.JobsDirectory, "ask-cpt_x-20261008T093000Z.log"), paths.JobLogPath(ask));
                AssertEqual(Path.Combine(paths.JobsDirectory, "job-01234567-20261008T093000Z.log"), paths.JobLogPath(old), "an older Admiral's job is named by its id");
                AssertEqual(Path.Combine(paths.JobsDirectory, "msn_a_b.log"), paths.MissionLogPath("msn_a/b"), "path characters are replaced");

                Directory.CreateDirectory(paths.JobsDirectory);
                for (int i = 0; i < 5; i++) Touch(Path.Combine(paths.JobsDirectory, "job-" + i + ".log"), i);
                AssertEqual(3, paths.PruneJobLogs(2), "three deleted");
                AssertEqual("job-4.log,job-3.log", Names(paths.ListJobLogs()), "the newest two are kept");
            }));

            cases.Add(Case("job_info_describes_what_runs", "A launch is described in plain terms with its runtime and elapsed time", TestTags.Positive, () =>
            {
                DateTime started = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
                HarborJobInfo ask = HarborJobInfo.FromLaunch(new HarborLaunchRequest { JobId = "j", Runtime = "Codex", JobKind = "askturn", CaptainId = "cpt_a" }, started);
                AssertEqual(HarborJobKindEnum.AskTurn, ask.Kind, "kind parses case-insensitively");
                AssertEqual("Ask turn", ask.Title());
                AssertEqual("Codex", ask.Runtime);
                AssertEqual("cpt_a", ask.CaptainId);

                HarborJobInfo mission = HarborJobInfo.FromLaunch(new HarborLaunchRequest { JobId = "j", Runtime = "ClaudeCode", MissionId = " msn_q " }, started);
                AssertEqual(HarborJobKindEnum.Mission, mission.Kind, "a mission id without a kind is a mission");
                AssertEqual("Mission msn_q", mission.Title());

                HarborJobInfo future = HarborJobInfo.FromLaunch(new HarborLaunchRequest { JobId = "j", Runtime = "ClaudeCode", JobKind = "SomethingNew" }, started);
                AssertEqual(HarborJobKindEnum.Unknown, future.Kind, "a kind this Harbor does not know is accepted as unknown");
                AssertEqual("Captain job", future.Title());

                AssertEqual("45s", HarborJobInfo.FormatElapsed(TimeSpan.FromSeconds(45)));
                AssertEqual("3m 05s", HarborJobInfo.FormatElapsed(TimeSpan.FromSeconds(185)));
                AssertEqual("1h 02m", HarborJobInfo.FormatElapsed(TimeSpan.FromMinutes(62)));
                AssertEqual("0s", HarborJobInfo.FormatElapsed(TimeSpan.FromSeconds(-5)), "a clock step back reads as zero");
                AssertEqual("12m 34s", mission.Elapsed(started.AddSeconds(754)));
            }));

            cases.Add(Case("launch_request_carries_job_kind", "The job kind, mission, and captain travel on the launch message and are optional", TestTags.Positive, () =>
            {
                HarborLaunchRequest sent = new HarborLaunchRequest { JobId = "j", Runtime = "ClaudeCode", JobKind = HarborJobKindEnum.Mission.ToString(), MissionId = "msn_m", CaptainId = "cpt_c" };
                HarborLaunchRequest? received = HarborProtocol.Deserialize(HarborProtocol.Serialize(sent)) as HarborLaunchRequest;
                AssertNotNull(received, "round trip");
                AssertEqual(HarborJobKindEnum.Mission, received!.JobKindType);
                AssertEqual("msn_m", received.MissionId);
                AssertEqual("cpt_c", received.CaptainId);

                HarborLaunchRequest? older = HarborProtocol.Deserialize("{\"type\":\"launch\",\"jobId\":\"j\",\"runtime\":\"ClaudeCode\",\"workingDirectory\":\"/w\"}") as HarborLaunchRequest;
                AssertNotNull(older, "a launch from an older Admiral still parses");
                AssertEqual(HarborJobKindEnum.Unknown, older!.JobKindType);
                AssertNull(older.MissionId);
            }));

            cases.Add(CaseAsync("link_client_lists_live_jobs", "While a job runs the link client reports what it is, its runtime, and when it started; it is gone after it exits", TestTags.Positive, async () =>
            {
                FakeTransport transport = new FakeTransport();
                transport.Enqueue(HarborProtocol.Serialize(new HarborLaunchRequest
                {
                    JobId = "job-live",
                    Runtime = "Codex",
                    WorkingDirectory = "/repo",
                    JobKind = HarborJobKindEnum.AskTurn.ToString(),
                    CaptainId = "cpt_live"
                }));
                SnapshotJobRunner runner = new SnapshotJobRunner();
                HarborLinkClient client = new HarborLinkClient("hbr_app1", "Rig", new List<HarborCapability>(), 4, new StubExecutor(), CreateLogging(), 0, null, runner);
                runner.Client = client;

                await client.RunSessionAsync(transport, CancellationToken.None).ConfigureAwait(false);

                AssertNotNull(runner.WhileRunning, "snapshot taken while the job ran");
                AssertEqual(1, runner.WhileRunning!.Count, "one live job");
                HarborJobInfo job = runner.WhileRunning[0];
                AssertEqual("job-live", job.JobId);
                AssertEqual(HarborJobKindEnum.AskTurn, job.Kind);
                AssertEqual("Codex", job.Runtime);
                AssertEqual("cpt_live", job.CaptainId);
                AssertTrue(job.StartedUtc > DateTime.UtcNow.AddMinutes(-5) && job.StartedUtc <= DateTime.UtcNow, "start time is now");
                AssertEqual(0, client.LiveJobs().Count, "nothing live after the exit");
            }));

            cases.Add(CaseAsync("job_runner_keeps_job_output_on_this_machine", "A job's output (stderr marked) and exit code are written to its log on the Harbor's machine", TestTags.Process, async () =>
            {
                string root = TestTemp.NewDirectory("harbor-app-joblog");
                string work = Path.Combine(root, "work");
                Directory.CreateDirectory(work);
                string shim = WriteShim(root);
                LoggingModule logging = CreateLogging();
                AgentRuntimeFactory factory = new AgentRuntimeFactory(logging);
                factory.Override(AgentRuntimeEnum.ClaudeCode, () => new ClaudeCodeRuntime(logging) { ExecutablePath = shim });
                LocalHarborJobRunner runner = new LocalHarborJobRunner(logging, factory, Path.Combine(root, "scratch"));
                runner.JobLogs = new HarborLogPaths(Path.Combine(root, "logs"));

                TaskCompletionSource<int> exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                HarborLaunchRequest request = new HarborLaunchRequest
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    Runtime = AgentRuntimeEnum.ClaudeCode.ToString(),
                    WorkingDirectory = work,
                    Prompt = "Respond with OK.",
                    JobKind = HarborJobKindEnum.Mission.ToString(),
                    MissionId = "msn_joblog",
                    CaptainId = "cpt_joblog"
                };

                await runner.StartAsync(request, null, _ => { }, (_, _) => { }, code => exited.TrySetResult(code), CancellationToken.None).ConfigureAwait(false);
                Task finished = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
                AssertTrue(finished == exited.Task, "the shim exits");

                string log = runner.JobLogs.MissionLogPath("msn_joblog");
                string contents = String.Empty;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(10));
                while (!deadline.Passed)
                {
                    contents = File.Exists(log) ? ReadShared(log) : String.Empty;
                    if (contents.Contains("exited with code", StringComparison.Ordinal) && contents.Contains("[stderr] oops", StringComparison.Ordinal)) break;
                    await Task.Delay(50).ConfigureAwait(false);
                }

                AssertContains("Mission msn_joblog started (runtime ClaudeCode, captain cpt_joblog", contents, "header names the job");
                AssertContains("hello", contents, "stdout is kept");
                AssertContains("[stderr] oops", contents, "stderr is kept and marked");
                AssertContains("exited with code 3", contents, "exit code is kept");
                AssertFalse(contents.Contains("Respond with OK.", StringComparison.Ordinal), "the prompt is not written to the log");
            }));

            cases.Add(Case("settings_backups_list_and_restore", "Backups list newest first with their times; restoring one keeps the current version as a new backup", TestTags.Positive, () =>
            {
                string directory = TestTemp.NewDirectory("harbor-app-backups");
                string file = Path.Combine(directory, "settings.json");
                DateTime first = new DateTime(2026, 10, 7, 18, 30, 0, 123, DateTimeKind.Utc);
                DateTime second = new DateTime(2026, 10, 8, 9, 15, 12, 456, DateTimeKind.Utc);
                SettingsFileStore.Save(file, "{ \"v\": 1 }", 5, first);
                SettingsFileStore.Save(file, "{ \"v\": 2 }", 5, first);
                SettingsFileStore.Save(file, "{ \"v\": 3 }", 5, second);
                string garbage = Path.Combine(directory, "settings.json.bak-garbage");
                File.WriteAllText(garbage, "{}");
                File.SetLastWriteTimeUtc(garbage, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

                List<SettingsBackupEntry> backups = SettingsFileStore.ListBackupEntries(file);
                AssertEqual(3, backups.Count, "two timestamped backups and one with an unreadable name");
                SettingsBackupEntry newest = backups.First(b => b.TakenUtc == second);
                AssertEqual(Path.GetFullPath(SettingsFileStore.BackupPath(file, second)), newest.Path);
                AssertEqual(DateTimeKind.Utc, newest.TakenUtc.Kind, "times are UTC");
                AssertEqual("{ \"v\": 2 }", File.ReadAllText(newest.Path), "the newest backup holds the version the last save replaced");
                SettingsBackupEntry oldest = backups.First(b => b.TakenUtc == first);
                AssertTrue(backups.IndexOf(newest) < backups.IndexOf(oldest), "newest first");
                AssertEqual(Path.GetFullPath(garbage), backups[2].Path, "a name without a time sorts by its file time");

                DateTime restoredAt = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
                string? kept = SettingsFileStore.Restore(file, oldest.Path, 5, restoredAt);
                AssertEqual("{ \"v\": 1 }", File.ReadAllText(file), "the chosen version is back");
                AssertNotNull(kept, "the replaced version is kept");
                AssertEqual("{ \"v\": 3 }", File.ReadAllText(kept!), "the version before the restore can be restored in turn");
                AssertEqual(restoredAt, SettingsFileStore.ListBackupEntries(file)[0].TakenUtc, "the restore's backup is the newest");

                AssertNull(SettingsFileStore.Restore(file, oldest.Path, 5, restoredAt.AddMinutes(1)), "restoring the content already in place writes nothing");

                string stranger = Path.Combine(directory, "other.json");
                File.WriteAllText(stranger, "{}");
                bool refused = false;
                try
                {
                    SettingsFileStore.Restore(file, stranger);
                }
                catch (ArgumentException)
                {
                    refused = true;
                }

                AssertTrue(refused, "a file that is not one of its backups is refused");
                AssertEqual("{ \"v\": 1 }", File.ReadAllText(file), "and nothing changed");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Harbor App (menus, logs, jobs, settings versions)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static List<HarborMenuCommandEnum> Commands(List<HarborMenuEntry> entries)
        {
            return Flatten(entries).Where(e => e.Command.HasValue).Select(e => e.Command!.Value).ToList();
        }

        private static List<HarborMenuEntry> Flatten(List<HarborMenuEntry> entries)
        {
            List<HarborMenuEntry> all = new List<HarborMenuEntry>();
            foreach (HarborMenuEntry entry in entries)
            {
                all.Add(entry);
                if (entry.Children != null) all.AddRange(Flatten(entry.Children));
            }

            return all;
        }

        private static string Names(List<LogFileEntry> entries)
        {
            return String.Join(",", entries.Select(e => e.Name));
        }

        private static void Touch(string path, int minutesAfterBase)
        {
            File.WriteAllText(path, "x\n");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutesAfterBase));
        }

        private static string ReadShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static string WriteShim(string directory)
        {
            if (OperatingSystem.IsWindows())
            {
                string cmd = Path.Combine(directory, "claude.cmd");
                File.WriteAllText(cmd, "@echo off\r\necho hello\r\necho oops 1>&2\r\nexit /b 3\r\n");
                return cmd;
            }

            string path = Path.Combine(directory, "claude");
            File.WriteAllText(path, "#!/usr/bin/env sh\necho hello\necho oops 1>&2\nexit 3\n");
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return path;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        private sealed class FakeTransport : IHarborTransport
        {
            private readonly Queue<string> _Inbound = new Queue<string>();

            public List<string> Sent { get; } = new List<string>();

            public void Enqueue(string message) => _Inbound.Enqueue(message);

            public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;

            public Task SendAsync(string text, CancellationToken token)
            {
                Sent.Add(text);
                return Task.CompletedTask;
            }

            public Task<string?> ReceiveAsync(CancellationToken token)
            {
                if (_Inbound.Count == 0) return Task.FromResult<string?>(null);
                return Task.FromResult<string?>(_Inbound.Dequeue());
            }

            public Task CloseAsync(CancellationToken token) => Task.CompletedTask;
        }

        private sealed class SnapshotJobRunner : IHarborJobRunner
        {
            public HarborLinkClient? Client { get; set; } = null;

            public List<HarborJobInfo>? WhileRunning { get; private set; } = null;

            public Task StartAsync(HarborLaunchRequest request, string? mcpBaseUrl, Action<int> onStarted, Action<HarborOutputStreamEnum, string> onOutput, Action<int> onExited, CancellationToken token)
            {
                onStarted(4243);
                WhileRunning = Client?.LiveJobs();
                onExited(0);
                return Task.CompletedTask;
            }

            public Task StopAsync(string jobId, int gracefulTimeoutMs, CancellationToken token) => Task.CompletedTask;

            public string? ResolveWorkingDirectory(HarborLaunchRequest request) => request.WorkingDirectory;
        }

        private sealed class StubExecutor : IHostCommandExecutor
        {
            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                return Task.FromResult(new HostCommandResult());
            }
        }

        #endregion
    }
}
