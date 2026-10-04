namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Renders reference frames (login, shell, palette, help, notifications, light and high-contrast themes) and,
    /// when ARMADA_TUI_SNAPSHOT_DIR is set, writes them there as text files for review (docs and reports). Always
    /// asserts the frames are non-empty.
    /// </summary>
    public sealed class TuiSnapshotSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Snapshots";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(TuiCase.Sync(Suite, "reference_frames", "Reference frames render (written to ARMADA_TUI_SNAPSHOT_DIR when set)", () =>
            {
                Dictionary<string, string> frames = new Dictionary<string, string>();
                using (TuiTestHost host = new TuiTestHost(100, 32, TuiFixtures.SignedInServer()))
                {
                    host.Start();
                    frames["login-100x32"] = host.Screen();
                }

                using (TuiTestHost host = TuiCase.SignedIn(120, 36, "/missions"))
                {
                    host.Tui.Context.Status.PollAllAsync().GetAwaiter().GetResult();
                    host.Pump();
                    frames["shell-missions-120x36"] = host.Screen();
                    host.Press("ctrl+k");
                    host.Type("deliv");
                    frames["palette-120x36"] = host.Screen();
                    host.Press("esc");
                    host.Press("?");
                    frames["help-120x36"] = host.Screen();
                    host.Press("esc");
                    host.Tui.Context.Notifications.PushEntityChange("Mission", "msn_1", "Fix column widths", "Failed");
                    frames["toast-120x36"] = host.Screen();
                    host.Press("ctrl+n");
                    frames["notifications-120x36"] = host.Screen();
                    host.Press("esc");
                    host.Press("f10");
                    host.Press("right");
                    frames["menu-go-120x36"] = host.Screen();
                    host.Press("esc");
                }

                using (TuiTestHost host = TuiCase.SignedIn(80, 24, "/server?tab=diagnostics"))
                {
                    frames["shell-80x24"] = host.Screen();
                }

                using (TuiTestHost host = TuiCase.SignedIn(200, 60, "/vessels/health?overall=Fail"))
                {
                    frames["shell-200x60"] = host.Screen();
                }

                string? dir = Environment.GetEnvironmentVariable("ARMADA_TUI_SNAPSHOT_DIR");
                foreach (KeyValuePair<string, string> kvp in frames)
                {
                    Asserts.AssertTrue(kvp.Value.Trim().Length > 0, kvp.Key);
                    if (!String.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir!);
                        File.WriteAllText(Path.Combine(dir!, kvp.Key + ".txt"), kvp.Value);
                    }
                }
            }));
            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI reference frames", cases: cases);
        }
    }
}
