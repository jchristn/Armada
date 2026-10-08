namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Covers the services Harbor's management window (and the CLI's log commands) use on the Admiral's files: the
    /// log directory layout and mission/captain log resolution, tailing and following a growing log, severity
    /// filtering, atomic settings saves with backups, settings validation, disk usage, and the REST URL and local API
    /// key taken from the link URL and settings file.
    /// </summary>
    public sealed class LocalAdmiralFilesSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LocalAdmiralFiles";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("log_paths_layout_and_resolution", "Mission logs fall back to the captain's .current pointer, then its .log", TestTags.Positive, () =>
            {
                string logs = TestTemp.NewDirectory("admiral-files");
                ArmadaLogPaths paths = new ArmadaLogPaths(logs);
                AssertEqual(Path.Combine(logs, "missions"), paths.MissionsDirectory);
                AssertEqual(Path.Combine(logs, "captains"), paths.CaptainsDirectory);
                AssertEqual(Path.Combine(logs, "final-messages"), paths.DirectoryFor(LogCategoryEnum.FinalMessages));
                AssertEqual(Path.Combine(logs, "missions", "msn_a.log"), paths.MissionLogPath(" msn_a "));

                Directory.CreateDirectory(paths.MissionsDirectory);
                Directory.CreateDirectory(paths.CaptainsDirectory);
                AssertNull(paths.ResolveMissionLog("msn_a", "cpt_x"), "nothing exists yet");

                string captainLog = Path.Combine(paths.CaptainsDirectory, "cpt_x.log");
                File.WriteAllText(captainLog, "captain\n");
                AssertEqual(captainLog, paths.ResolveMissionLog("msn_a", "cpt_x"), "captain's own log");

                string sessionLog = Path.Combine(logs, "session-7.log");
                File.WriteAllText(sessionLog, "session\n");
                File.WriteAllText(Path.Combine(paths.CaptainsDirectory, "cpt_x.current"), sessionLog + "\n");
                AssertEqual(sessionLog, paths.ResolveCaptainLog("cpt_x"), ".current pointer wins");

                File.WriteAllText(Path.Combine(paths.CaptainsDirectory, "cpt_y.current"), Path.Combine(logs, "gone.log"));
                AssertNull(paths.ResolveCaptainLog("cpt_y"), "a dangling pointer with no captain log");

                File.WriteAllText(paths.MissionLogPath("msn_a"), "mission\n");
                AssertEqual(paths.MissionLogPath("msn_a"), paths.ResolveMissionLog("msn_a", "cpt_x"), "the mission's own log wins");
                AssertNull(paths.ResolveMissionLog("msn_b"), "no captain to fall back to");
            }));

            cases.Add(Case("log_paths_list", "Listing is newest first, filtered, limited, and skips pointers and non-admiral files", TestTags.Positive, () =>
            {
                string logs = TestTemp.NewDirectory("admiral-files");
                ArmadaLogPaths paths = new ArmadaLogPaths(logs);
                AssertEqual(0, paths.List(LogCategoryEnum.Missions).Count, "missing directory lists nothing");

                Directory.CreateDirectory(paths.CaptainsDirectory);
                Touch(Path.Combine(paths.CaptainsDirectory, "cpt_a.log"), 1);
                Touch(Path.Combine(paths.CaptainsDirectory, "cpt_b.log"), 3);
                Touch(Path.Combine(paths.CaptainsDirectory, "cpt_b.current"), 4);
                List<LogFileEntry> captains = paths.List(LogCategoryEnum.Captains);
                AssertEqual(2, captains.Count, ".current pointers are not logs");
                AssertEqual("cpt_b.log", captains[0].Name, "newest first");
                AssertEqual(LogCategoryEnum.Captains, captains[0].Category);
                AssertEqual(1, paths.List(LogCategoryEnum.Captains, "A.LOG").Count, "case-insensitive name filter");
                AssertEqual(1, paths.List(LogCategoryEnum.Captains, null, 1).Count, "limit");

                Touch(Path.Combine(logs, "admiral.log.20261006"), 1);
                Touch(Path.Combine(logs, "admiral.log"), 2);
                Touch(Path.Combine(logs, "other.log"), 3);
                List<LogFileEntry> admiral = paths.List(LogCategoryEnum.Admiral);
                AssertEqual(2, admiral.Count, "only admiral.log files in the top directory");
                AssertEqual("admiral.log", admiral[0].Name);
            }));

            cases.Add(Case("tail_reads_end_on_line_boundary", "Tail reads the end of a file starting at a whole line", TestTags.Positive, () =>
            {
                string file = Path.Combine(TestTemp.NewDirectory("admiral-files"), "tail.log");
                File.WriteAllText(file, "line one\nline two\nline three\n");

                LogReadResult all = LogTailReader.ReadTail(file);
                AssertEqual("line one\nline two\nline three\n", all.Text);
                AssertEqual("", all.PartialLine);
                AssertFalse(all.StartedMidFile);
                AssertEqual(new FileInfo(file).Length, all.EndOffset);

                LogReadResult end = LogTailReader.ReadTail(file, 15);
                AssertTrue(end.StartedMidFile);
                AssertEqual("line three\n", end.Text, "the partial first line is dropped");
                AssertEqual(all.EndOffset, end.EndOffset);
            }));

            cases.Add(Case("follow_appends_and_partial_lines", "Follow returns appended lines and re-reads an unfinished line once complete", TestTags.Positive, () =>
            {
                string file = Path.Combine(TestTemp.NewDirectory("admiral-files"), "follow.log");
                File.WriteAllText(file, "first\nsecond-par");

                LogReadResult start = LogTailReader.ReadTail(file);
                AssertEqual("first\n", start.Text);
                AssertEqual("second-par", start.PartialLine, "unterminated last line is reported separately");
                AssertEqual(6, start.EndOffset, "offset stops before the partial line");

                LogReadResult nothing = LogTailReader.ReadFrom(file, start.EndOffset);
                AssertEqual("", nothing.Text);
                AssertEqual("second-par", nothing.PartialLine);

                // A writer holding the file open does not block the reader.
                using (FileStream writer = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    byte[] more = Encoding.UTF8.GetBytes("tial\nthird\n");
                    writer.Write(more, 0, more.Length);
                    writer.Flush();

                    LogReadResult next = LogTailReader.ReadFrom(file, start.EndOffset);
                    AssertEqual("second-partial\nthird\n", next.Text);
                    AssertEqual("", next.PartialLine);
                    AssertFalse(next.WasReset);
                    AssertEqual(writer.Length, next.EndOffset);
                }
            }));

            cases.Add(Case("follow_detects_truncation_and_skips_ahead", "A shorter file resets; a burst larger than the window skips to the newest", TestTags.Positive, () =>
            {
                string file = Path.Combine(TestTemp.NewDirectory("admiral-files"), "rotate.log");
                File.WriteAllText(file, "aaaaaaaaaa\nbbbbbbbbbb\n");
                long offset = LogTailReader.ReadTail(file).EndOffset;

                File.WriteAllText(file, "new\n");
                LogReadResult reset = LogTailReader.ReadFrom(file, offset);
                AssertTrue(reset.WasReset, "file shrank below the offset");
                AssertEqual("new\n", reset.Text);

                StringBuilder burst = new StringBuilder("new\n");
                for (int i = 0; i < 100; i++) burst.Append("line ").Append(i.ToString("D3")).Append('\n');
                File.WriteAllText(file, burst.ToString());
                LogReadResult skipped = LogTailReader.ReadFrom(file, reset.EndOffset, 40);
                AssertTrue(skipped.StartedMidFile, "skipped ahead");
                AssertTrue(skipped.Text.EndsWith("line 099\n"), skipped.Text);
                AssertFalse(skipped.Text.Contains("line 000"), "older lines skipped");
                AssertTrue(skipped.Text.StartsWith("line "), "starts on a whole line: " + skipped.Text);
            }));

            cases.Add(Case("tail_long_line_and_utf8", "A line longer than the window still shows; multi-byte text survives", TestTags.Positive, () =>
            {
                string dir = TestTemp.NewDirectory("admiral-files");
                string longFile = Path.Combine(dir, "long.log");
                File.WriteAllText(longFile, new string('x', 100));
                LogReadResult longRead = LogTailReader.ReadTail(longFile, 10);
                AssertEqual("", longRead.Text);
                AssertEqual(new string('x', 10), longRead.PartialLine, "no newline at all: the window is shown");

                string utf = Path.Combine(dir, "utf8.log");
                File.WriteAllText(utf, "café ✓\nnaïve\n", new UTF8Encoding(false));
                AssertEqual("café ✓\nnaïve\n", LogTailReader.ReadTail(utf).Text);

                AssertThrows<FileNotFoundException>(() => LogTailReader.ReadTail(Path.Combine(dir, "missing.log")));
                AssertThrows<ArgumentOutOfRangeException>(() => LogTailReader.ReadFrom(utf, -1));
            }));

            cases.Add(Case("severity_filter", "Severity filter keeps lines at or above the level with their continuations", TestTags.Positive, () =>
            {
                AssertEqual(0, LogSeverityFilter.Rank("debug"));
                AssertEqual(2, LogSeverityFilter.Rank("Warn"));
                AssertEqual(-1, LogSeverityFilter.Rank("at"));
                AssertEqual(1, LogSeverityFilter.LineRank("2026-10-07 07:00:01 joels-mbp Info [ArmadaServer] started"));
                AssertEqual(-1, LogSeverityFilter.LineRank("   at Armada.Core.Foo()"));
                AssertEqual(-1, LogSeverityFilter.LineRank("Warn but not a log line"));

                List<string> lines = new List<string>
                {
                    "2026-10-07 07:00:01 host Debug [A] noise",
                    "2026-10-07 07:00:02 host Warn [A] careful",
                    "   at Frame.One()",
                    "2026-10-07 07:00:03 host Info [A] fine",
                    "2026-10-07 07:00:04 host Error [A] broken",
                    "   at Frame.Two()"
                };

                List<string> warn = LogSeverityFilter.Filter(lines, LogSeverityFilter.Rank("Warn"));
                AssertEqual(4, warn.Count);
                AssertContains("careful", warn[0]);
                AssertEqual("   at Frame.One()", warn[1], "continuation follows its line");
                AssertContains("broken", warn[2]);
                AssertEqual(6, LogSeverityFilter.Filter(lines, 0).Count, "0 keeps everything");
                AssertEqual(0, LogSeverityFilter.Filter(new List<string> { "   at X()" }, 2, false).Count, "continuation of a dropped line");
            }));

            cases.Add(Case("settings_store_atomic_with_backups", "Saves replace the file, keep timestamped backups, prune, and skip identical content", TestTags.Positive, () =>
            {
                string dir = TestTemp.NewDirectory("admiral-files");
                string file = Path.Combine(dir, "settings.json");

                AssertNull(SettingsFileStore.Save(file, "{ \"v\": 1 }", 2, new DateTime(2026, 10, 7, 1, 0, 0, DateTimeKind.Utc)), "new file: no backup");
                AssertEqual("{ \"v\": 1 }", File.ReadAllText(file));

                string? first = SettingsFileStore.Save(file, "{ \"v\": 2 }", 2, new DateTime(2026, 10, 7, 2, 0, 0, DateTimeKind.Utc));
                AssertNotNull(first);
                AssertEqual("{ \"v\": 1 }", File.ReadAllText(first!), "backup holds the previous version");
                AssertEqual(SettingsFileStore.BackupPath(file, new DateTime(2026, 10, 7, 2, 0, 0, DateTimeKind.Utc)), first);
                AssertEndsWith(".bak-20261007T020000000Z", first!);

                AssertNull(SettingsFileStore.Save(file, "{ \"v\": 2 }", 2, new DateTime(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc)), "identical content writes nothing");
                SettingsFileStore.Save(file, "{ \"v\": 3 }", 2, new DateTime(2026, 10, 7, 4, 0, 0, DateTimeKind.Utc));
                SettingsFileStore.Save(file, "{ \"v\": 4 }", 2, new DateTime(2026, 10, 7, 5, 0, 0, DateTimeKind.Utc));

                List<string> backups = SettingsFileStore.ListBackups(file);
                AssertEqual(2, backups.Count, "pruned to two");
                AssertEndsWith("T050000000Z", backups[0]);
                AssertEqual("{ \"v\": 3 }", File.ReadAllText(backups[0]));
                AssertEqual("{ \"v\": 4 }", File.ReadAllText(file));
                AssertFalse(Directory.EnumerateFiles(dir, ".settings.json.tmp-*").Any(), "no temporary files left");

                string other = Path.Combine(dir, "settings.json.extra");
                File.WriteAllText(other, "x");
                AssertEqual(2, SettingsFileStore.ListBackups(file).Count, "other files are not backups");
                AssertNull(SettingsFileStore.Save(Path.Combine(dir, "nobackup.json"), "a", 0));
                AssertNull(SettingsFileStore.Save(Path.Combine(dir, "nobackup.json"), "b", 0), "backups off");
                AssertEqual(0, SettingsFileStore.ListBackups(Path.Combine(dir, "nobackup.json")).Count);
            }));

            cases.Add(Case("settings_validation", "Settings text is checked as the Admiral parses it", TestTags.Positive, () =>
            {
                AssertNull(SettingsTextValidator.ValidateArmadaSettings("{ }"), "defaults are valid");
                AssertNull(SettingsTextValidator.ValidateArmadaSettings("{ \"admiralPort\": 7890, \"maxCaptains\": 4 }"));
                AssertNotNull(SettingsTextValidator.ValidateArmadaSettings(""), "empty");
                AssertNotNull(SettingsTextValidator.ValidateArmadaSettings("[ 1, 2 ]"), "not an object");

                string? syntax = SettingsTextValidator.ValidateArmadaSettings("{\n  \"admiralPort\": 7890,\n  \"maxCaptains\": \n}");
                AssertNotNull(syntax);
                AssertStartsWith("Line 4", syntax!, "one-based line of the error");
                AssertFalse(syntax!.Contains("LineNumber:"), "zero-based copy removed");

                AssertNotNull(SettingsTextValidator.ValidateArmadaSettings("{ \"maxCaptains\": -1 }"), "range checked by the setter");
                AssertNotNull(SettingsTextValidator.ValidateArmadaSettings("{ \"heartbeatIntervalSeconds\": 1 }"), "minimum 5");
                AssertNotNull(SettingsTextValidator.ValidateArmadaSettings("{ \"admiralPort\": \"seven\" }"), "wrong type");

                AssertNull(SettingsTextValidator.ValidateJsonObject("{ \"theme\": \"dark\", \"profiles\": [] }"));
                AssertNotNull(SettingsTextValidator.ValidateJsonObject("{ \"theme\": }"));
                AssertNotNull(SettingsTextValidator.ValidateJsonObject("null"));
                AssertNotNull(SettingsTextValidator.ValidateJsonObject("  "));

                ArmadaSettings parsed = ArmadaSettings.Parse("{ \"maxCaptains\": 9 }");
                AssertEqual(9, parsed.MaxCaptains);
                AssertNull(parsed.SettingsFilePath, "Parse does not bind a file");
            }));

            cases.Add(Case("directory_usage", "Usage totals each top-level item, largest first, without following links", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("admiral-files");
                File.WriteAllBytes(Path.Combine(root, "armada.db"), new byte[3000]);
                string logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
                File.WriteAllBytes(Path.Combine(logs, "a.log"), new byte[1000]);
                string nested = Directory.CreateDirectory(Path.Combine(logs, "missions")).FullName;
                File.WriteAllBytes(Path.Combine(nested, "m.log"), new byte[500]);
                Directory.CreateDirectory(Path.Combine(root, "empty"));

                List<DirectoryUsageEntry> usage = DirectoryUsage.Measure(root);
                AssertEqual(3, usage.Count);
                AssertEqual("armada.db", usage[0].Name);
                AssertEqual(3000L, usage[0].SizeBytes);
                AssertFalse(usage[0].IsDirectory);
                DirectoryUsageEntry logsEntry = usage.First(u => u.Name == "logs");
                AssertTrue(logsEntry.IsDirectory);
                AssertEqual(1500L, logsEntry.SizeBytes, "recursive");
                AssertEqual(2L, logsEntry.FileCount);
                AssertEqual(0L, usage.First(u => u.Name == "empty").SizeBytes);
                AssertEqual(0, DirectoryUsage.Measure(Path.Combine(root, "missing")).Count);

                if (!OperatingSystem.IsWindows())
                {
                    Directory.CreateSymbolicLink(Path.Combine(root, "link-to-logs"), logs);
                    DirectoryUsageEntry link = DirectoryUsage.Measure(root).First(u => u.Name == "link-to-logs");
                    AssertEqual(0L, link.SizeBytes, "symbolic links are not followed");
                }

                AssertEqual("512 B", DirectoryUsage.FormatBytes(512));
                AssertEqual("1.5 KB", DirectoryUsage.FormatBytes(1536));
                AssertEqual("96.0 MB", DirectoryUsage.FormatBytes(96L * 1024 * 1024));
                AssertThrows<OperationCanceledException>(() =>
                {
                    using (CancellationTokenSource cts = new CancellationTokenSource())
                    {
                        cts.Cancel();
                        DirectoryUsage.Measure(root, cts.Token);
                    }
                });
            }));

            cases.Add(CaseAsync("rest_url_and_local_api_key", "The REST URL follows the link URL; the local API key comes from settings.json", TestTags.Positive, async () =>
            {
                AssertEqual("http://127.0.0.1:7890", LocalAdmiralInfo.RestBaseUrl("ws://127.0.0.1:7890/v1.0/harbor/connect"));
                AssertEqual("https://admiral.example.com", LocalAdmiralInfo.RestBaseUrl("wss://admiral.example.com/v1.0/harbor/connect"));
                AssertEqual("https://admiral.example.com:8443", LocalAdmiralInfo.RestBaseUrl("wss://admiral.example.com:8443/x"));
                AssertEqual("http://[::1]:7890", LocalAdmiralInfo.RestBaseUrl("ws://[::1]:7890/v1.0/harbor/connect"));
                AssertEqual("http://localhost:7890", LocalAdmiralInfo.RestBaseUrl("http://localhost:7890/dashboard"));
                AssertNull(LocalAdmiralInfo.RestBaseUrl("ftp://host/x"));
                AssertNull(LocalAdmiralInfo.RestBaseUrl("nonsense"));
                AssertNull(LocalAdmiralInfo.RestBaseUrl(null));

                string data = TestTemp.NewDirectory("admiral-files");
                LocalAdmiralInfo none = await LocalAdmiralInfo.ResolveAsync("ws://127.0.0.1:7890/v1.0/harbor/connect", data).ConfigureAwait(false);
                AssertNull(none.LocalApiKey);
                AssertEqual(Path.Combine(data, "backups"), none.BackupsDirectory);
                AssertEqual(Path.Combine(data, "tui.json"), none.TuiPreferencesFile);
                AssertEqual(none.LogDirectory, none.Logs.LogDirectory);

                await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "{ \"apiKey\": \"  local-key  \" }").ConfigureAwait(false);
                LocalAdmiralInfo keyed = await LocalAdmiralInfo.ResolveAsync("ws://127.0.0.1:7890/v1.0/harbor/connect", data).ConfigureAwait(false);
                AssertEqual("local-key", keyed.LocalApiKey);

                // A value the Admiral would reject elsewhere in the file does not hide the key or the log directory.
                string customLogs = Path.Combine(data, "elsewhere");
                await File.WriteAllTextAsync(Path.Combine(data, "settings.json"),
                    "{ \"maxCaptains\": -3, \"apiKey\": \"k2\", \"logDirectory\": \"" + customLogs.Replace("\\", "\\\\") + "\" }").ConfigureAwait(false);
                AssertNotNull(SettingsTextValidator.ValidateArmadaSettings(await File.ReadAllTextAsync(Path.Combine(data, "settings.json")).ConfigureAwait(false)), "the file is invalid");
                LocalAdmiralInfo lenient = await LocalAdmiralInfo.ResolveAsync("ws://127.0.0.1:7890/v1.0/harbor/connect", data).ConfigureAwait(false);
                AssertEqual("k2", lenient.LocalApiKey);
                AssertEqual(customLogs, lenient.LogDirectory);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Local Admiral Files (logs, settings, usage)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void Touch(string path, int day)
        {
            File.WriteAllText(path, "x\n");
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc));
        }

        private static void AssertEndsWith(string expected, string actual)
        {
            AssertTrue(actual.EndsWith(expected, StringComparison.Ordinal), "expected '" + actual + "' to end with '" + expected + "'");
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
    }
}
