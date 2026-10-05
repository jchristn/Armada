namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="NotificationService"/>. The service runs its platform command through a recording
    /// runner, so these tests check the exact command and arguments (including escaping) for each platform and never
    /// raise a real desktop notification on the machine running the tests.
    /// </summary>
    public sealed class NotificationServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the NotificationService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("macos_osascript_command", "macOS sends display notification through osascript with escaped quotes", TestTags.Positive, () =>
            {
                RecordingNotificationCommandRunner runner = new RecordingNotificationCommandRunner();
                new NotificationService(runner, DesktopPlatformEnum.MacOs).Send("Test's \"Title\"", "Message with 'quotes' and \"doubles\" and \\ slash");
                AssertEqual(1, runner.Calls.Count, "one command");
                AssertEqual("osascript", runner.Calls[0].FileName, "executable");
                AssertEqual(2, runner.Calls[0].Arguments.Count, "argument count");
                AssertEqual("-e", runner.Calls[0].Arguments[0], "script flag");
                AssertEqual("display notification \"Message with 'quotes' and \\\"doubles\\\" and \\\\ slash\" with title \"Test's \\\"Title\\\"\"", runner.Calls[0].Arguments[1], "script");
            }));

            cases.Add(Case("linux_notify_send_command", "Linux passes title and message to notify-send as separate arguments", TestTags.Positive, () =>
            {
                RecordingNotificationCommandRunner runner = new RecordingNotificationCommandRunner();
                new NotificationService(runner, DesktopPlatformEnum.Linux).Send("Armada", "Mission done; $(rm -rf /)");
                AssertEqual("notify-send", runner.Calls[0].FileName, "executable");
                AssertEqual("Armada", runner.Calls[0].Arguments[0], "title");
                AssertEqual("Mission done; $(rm -rf /)", runner.Calls[0].Arguments[1], "message is one argument, not shell text");
            }));

            cases.Add(Case("windows_toast_command", "Windows escapes XML in the toast and quotes it for PowerShell", TestTags.Positive, () =>
            {
                RecordingNotificationCommandRunner runner = new RecordingNotificationCommandRunner();
                new NotificationService(runner, DesktopPlatformEnum.Windows).Send("A & B", "It's <done>");
                AssertEqual("powershell", runner.Calls[0].FileName, "executable");
                AssertEqual("-NoProfile", runner.Calls[0].Arguments[0], "no profile");
                AssertEqual("-Command", runner.Calls[0].Arguments[1], "command flag");
                string expected =
                    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null; " +
                    "$xml = [Windows.Data.Xml.Dom.XmlDocument]::new(); " +
                    "$xml.LoadXml('<toast><visual><binding template=''ToastGeneric''><text>A &amp; B</text><text>It&apos;s &lt;done&gt;</text></binding></visual></toast>'); " +
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Armada').Show([Windows.UI.Notifications.ToastNotification]::new($xml))";
                AssertEqual(expected, runner.Calls[0].Arguments[2], "toast script");
            }));

            cases.Add(Case("other_platform_sends_nothing", "An unsupported platform runs no command", TestTags.Negative, () =>
            {
                RecordingNotificationCommandRunner runner = new RecordingNotificationCommandRunner();
                new NotificationService(runner, DesktopPlatformEnum.Other).Send("Armada", "Done");
                AssertEqual(0, runner.Calls.Count, "no command");
            }));

            cases.Add(Case("null_inputs_are_empty", "Null title and message are sent as empty strings", TestTags.Negative, () =>
            {
                RecordingNotificationCommandRunner runner = new RecordingNotificationCommandRunner();
                new NotificationService(runner, DesktopPlatformEnum.Linux).Send(null, null);
                AssertEqual("", runner.Calls[0].Arguments[0], "title");
                AssertEqual("", runner.Calls[0].Arguments[1], "message");
            }));

            cases.Add(Case("runner_failure_is_swallowed", "A notifier that cannot start does not affect the caller", TestTags.Negative, () =>
            {
                RecordingNotificationCommandRunner runner = new RecordingNotificationCommandRunner();
                runner.Failure = new System.ComponentModel.Win32Exception(2, "No such file or directory");
                new NotificationService(runner, DesktopPlatformEnum.Linux).Send("Armada", "Done");
                AssertEqual(1, runner.Calls.Count, "attempted once");
            }));

            cases.Add(Case("constructor_rejects_null_runner", "The constructor rejects a null runner", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => new NotificationService(null!, DesktopPlatformEnum.MacOs));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.NotificationService",
                displayName: "Notification Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.NotificationService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
