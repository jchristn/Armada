namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Covers the platform pieces behind Harbor's menus: the desktop shell commands (open, reveal, open in a text
    /// editor) on each platform, and how Harbor decides whether the Armada data directory and logs on this machine
    /// belong to the Admiral it is linked to. Nothing is launched; the commands are checked as data.
    /// </summary>
    public sealed class HarborDesktopSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborDesktop";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("shell_open_per_platform", "Open uses open, xdg-open, or the Windows shell association", TestTags.Positive, () =>
            {
                ShellLaunch mac = DesktopShellCommandBuilder.BuildOpen(HostPlatformEnum.MacOS, "/Users/a b/.armada");
                AssertEqual("open", mac.FileName);
                AssertEqual(1, mac.Arguments.Count);
                AssertEqual("/Users/a b/.armada", mac.Arguments[0], "path passed as one argument, spaces intact");
                AssertFalse(mac.UseShellExecute);
                AssertNull(mac.RawArguments);

                ShellLaunch linux = DesktopShellCommandBuilder.BuildOpen(HostPlatformEnum.Linux, "http://127.0.0.1:7890/dashboard");
                AssertEqual("xdg-open", linux.FileName);
                AssertEqual("http://127.0.0.1:7890/dashboard", linux.Arguments[0]);

                ShellLaunch windows = DesktopShellCommandBuilder.BuildOpen(HostPlatformEnum.Windows, "C:\\Users\\a b\\.armada");
                AssertEqual("C:\\Users\\a b\\.armada", windows.FileName);
                AssertTrue(windows.UseShellExecute);
                AssertEqual(0, windows.Arguments.Count);
            }));

            cases.Add(Case("shell_reveal_per_platform", "Reveal selects the item in Finder or Explorer and opens the folder on Linux", TestTags.Positive, () =>
            {
                ShellLaunch mac = DesktopShellCommandBuilder.BuildReveal(HostPlatformEnum.MacOS, "/Users/a/.armada/logs/admiral.log.20261007");
                AssertEqual("open", mac.FileName);
                AssertEqual(2, mac.Arguments.Count);
                AssertEqual("-R", mac.Arguments[0]);
                AssertEqual("/Users/a/.armada/logs/admiral.log.20261007", mac.Arguments[1]);

                ShellLaunch windows = DesktopShellCommandBuilder.BuildReveal(HostPlatformEnum.Windows, "C:\\Users\\a b\\.armada\\settings.json");
                AssertEqual("explorer.exe", windows.FileName);
                AssertEqual("/select,\"C:\\Users\\a b\\.armada\\settings.json\"", windows.RawArguments, "explorer wants the path quoted after the comma");
                AssertFalse(windows.UseShellExecute);

                ShellLaunch linux = DesktopShellCommandBuilder.BuildReveal(HostPlatformEnum.Linux, "/home/a/.armada/logs/admiral.log");
                AssertEqual("xdg-open", linux.FileName);
                AssertEqual("/home/a/.armada/logs", linux.Arguments[0], "Linux opens the containing folder");
            }));

            cases.Add(Case("shell_text_editor", "Text files open in a text editor, with Notepad as the Windows fallback", TestTags.Positive, () =>
            {
                ShellLaunch mac = DesktopShellCommandBuilder.BuildOpenInTextEditor(HostPlatformEnum.MacOS, "/Users/a/.armada/settings.json");
                AssertEqual("open", mac.FileName);
                AssertEqual("-t", mac.Arguments[0]);
                AssertEqual("/Users/a/.armada/settings.json", mac.Arguments[1]);

                ShellLaunch windows = DesktopShellCommandBuilder.BuildOpenInTextEditor(HostPlatformEnum.Windows, "C:\\a\\settings.json");
                AssertTrue(windows.UseShellExecute);
                ShellLaunch? fallback = DesktopShellCommandBuilder.BuildTextEditorFallback(HostPlatformEnum.Windows, "C:\\a\\settings.json");
                AssertNotNull(fallback);
                AssertEqual("notepad.exe", fallback!.FileName);
                AssertEqual("C:\\a\\settings.json", fallback.Arguments[0]);

                AssertNull(DesktopShellCommandBuilder.BuildTextEditorFallback(HostPlatformEnum.MacOS, "/a/settings.json"));
                AssertNull(DesktopShellCommandBuilder.BuildTextEditorFallback(HostPlatformEnum.Linux, "/a/settings.json"));
                AssertEqual("xdg-open", DesktopShellCommandBuilder.BuildOpenInTextEditor(HostPlatformEnum.Linux, "/a/settings.json").FileName);
            }));

            cases.Add(Case("shell_rejects_invalid", "Shell commands reject empty targets and unsupported platforms", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => DesktopShellCommandBuilder.BuildOpen(HostPlatformEnum.MacOS, ""));
                AssertThrows<ArgumentNullException>(() => DesktopShellCommandBuilder.BuildReveal(HostPlatformEnum.Windows, "  "));
                AssertThrows<ArgumentNullException>(() => DesktopShellCommandBuilder.BuildOpenInTextEditor(HostPlatformEnum.Linux, ""));
                AssertThrows<PlatformNotSupportedException>(() => DesktopShellCommandBuilder.BuildOpen(HostPlatformEnum.Unsupported, "/a"));
                AssertThrows<PlatformNotSupportedException>(() => DesktopShellCommandBuilder.BuildReveal(HostPlatformEnum.Unsupported, "/a"));
            }));

            cases.Add(Case("loopback_urls", "Only localhost and loopback addresses count as this machine", TestTags.Positive, () =>
            {
                AssertTrue(LocalAdmiralInfo.IsLoopbackUrl("ws://127.0.0.1:7890/v1.0/harbor/connect"));
                AssertTrue(LocalAdmiralInfo.IsLoopbackUrl("ws://localhost:7890/v1.0/harbor/connect"));
                AssertTrue(LocalAdmiralInfo.IsLoopbackUrl("wss://[::1]:7890/v1.0/harbor/connect"));
                AssertTrue(LocalAdmiralInfo.IsLoopbackUrl("http://127.0.0.2:7890/dashboard"), "all of 127/8 is loopback");
                AssertFalse(LocalAdmiralInfo.IsLoopbackUrl("ws://192.168.1.20:7890/v1.0/harbor/connect"));
                AssertFalse(LocalAdmiralInfo.IsLoopbackUrl("wss://admiral.example.com/v1.0/harbor/connect"));
                AssertFalse(LocalAdmiralInfo.IsLoopbackUrl("not a url"));
                AssertFalse(LocalAdmiralInfo.IsLoopbackUrl(null));
                AssertFalse(LocalAdmiralInfo.IsLoopbackUrl(""));
            }));

            cases.Add(CaseAsync("resolve_local_admiral", "A loopback link with a data directory is local; log directory comes from settings.json", TestTags.Positive, async () =>
            {
                string data = TestTemp.NewDirectory("harbor-desktop");
                string customLogs = Path.Combine(data, "custom-logs");
                Directory.CreateDirectory(customLogs);
                await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "{ \"logDirectory\": \"" + customLogs.Replace("\\", "\\\\") + "\" }").ConfigureAwait(false);

                LocalAdmiralInfo info = await LocalAdmiralInfo.ResolveAsync("ws://127.0.0.1:7890/v1.0/harbor/connect", data).ConfigureAwait(false);
                AssertTrue(info.IsLocal, info.Reason);
                AssertEqual("", info.Reason);
                AssertEqual(data, info.DataDirectory);
                AssertEqual(Path.Combine(data, "settings.json"), info.SettingsFile);
                AssertEqual(customLogs, info.LogDirectory);
            }));

            cases.Add(CaseAsync("resolve_remote_or_missing", "A remote link, or no data directory, is not local", TestTags.Negative, async () =>
            {
                string data = TestTemp.NewDirectory("harbor-desktop");
                LocalAdmiralInfo remote = await LocalAdmiralInfo.ResolveAsync("wss://admiral.example.com/v1.0/harbor/connect", data).ConfigureAwait(false);
                AssertFalse(remote.IsLocal);
                AssertContains("another machine", remote.Reason);
                AssertEqual(Path.Combine(data, "logs"), remote.LogDirectory, "no settings file: default log directory");

                string missing = Path.Combine(data, "does-not-exist");
                LocalAdmiralInfo none = await LocalAdmiralInfo.ResolveAsync("ws://127.0.0.1:7890/v1.0/harbor/connect", missing).ConfigureAwait(false);
                AssertFalse(none.IsLocal);
                AssertContains(missing, none.Reason);

                await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "{ not json").ConfigureAwait(false);
                LocalAdmiralInfo corrupt = await LocalAdmiralInfo.ResolveAsync("ws://127.0.0.1:7890/v1.0/harbor/connect", data).ConfigureAwait(false);
                AssertTrue(corrupt.IsLocal, "an unreadable settings file does not hide the data directory");
                AssertEqual(Path.Combine(data, "logs"), corrupt.LogDirectory);
            }));

            cases.Add(Case("latest_admiral_log", "The newest admiral.log file wins; other logs are ignored", TestTags.Positive, () =>
            {
                string logs = TestTemp.NewDirectory("harbor-desktop");
                AssertNull(LocalAdmiralInfo.FindLatestLog(logs, LocalAdmiralInfo.AdmiralLogBaseName), "empty directory");
                AssertNull(LocalAdmiralInfo.FindLatestLog(Path.Combine(logs, "missing"), LocalAdmiralInfo.AdmiralLogBaseName), "missing directory");

                string older = Path.Combine(logs, "admiral.log.20261006");
                string newer = Path.Combine(logs, "admiral.log.20261007");
                string other = Path.Combine(logs, "admiral.logger.txt");
                string unrelated = Path.Combine(logs, "harbor.log");
                File.WriteAllText(older, "a");
                File.WriteAllText(newer, "b");
                File.WriteAllText(other, "c");
                File.WriteAllText(unrelated, "d");
                File.SetLastWriteTimeUtc(older, new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(newer, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(other, new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(unrelated, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));

                AssertEqual(newer, LocalAdmiralInfo.FindLatestLog(logs, LocalAdmiralInfo.AdmiralLogBaseName));

                string plain = Path.Combine(logs, "admiral.log");
                File.WriteAllText(plain, "e");
                File.SetLastWriteTimeUtc(plain, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
                AssertEqual(plain, LocalAdmiralInfo.FindLatestLog(logs, LocalAdmiralInfo.AdmiralLogBaseName), "the undated file counts too");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Harbor Desktop Shell and Admiral Paths", cases: cases);
        }

        #endregion

        #region Private-Methods

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
