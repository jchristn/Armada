namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Renders the README's text screenshots at 120x40 (login, Ask Armada, Home, the Missions list, and the Approvals
    /// center) from stub data and, when ARMADA_TUI_README_DIR is set, writes them there (<c>docs/tui-screens</c>) as
    /// ASCII (box-drawing borders transliterated to <c>+ - |</c>, as an ASCII terminal shows them), so the docs stay ASCII.
    /// Always asserts each frame shows its screen.
    /// </summary>
    public sealed class TuiReadmeFramesSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.ReadmeFrames";
        private const int Width = 120;
        private const int Height = 40;

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(TuiCase.Sync(Suite, "readme_frames", "README frames render at 120x40 (written to ARMADA_TUI_README_DIR when set)", () =>
            {
                Dictionary<string, string> frames = new Dictionary<string, string>();
                using (TuiTestHost host = new TuiTestHost(Width, Height, TuiFixtures.SignedInServer()))
                {
                    host.Start();
                    host.PumpUntil(() => host.Screen().Contains("Continue", StringComparison.Ordinal), 3000);
                    frames["login"] = host.Screen();
                }

                AskFixtures fx = Fixtures();
                using (TuiTestHost host = TuiCase.SignedIn(Width, Height, "/ask/ath_1", fx.Stub))
                {
                    host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count >= 3, 5000);
                    host.Pump();
                    frames["ask"] = host.Screen();
                }

                using (TuiTestHost host = TuiCase.SignedIn(Width, Height, "/", fx.Stub))
                {
                    host.Tui.Context.Status.PollAllAsync().GetAwaiter().GetResult();
                    host.PumpUntil(() => host.Screen().Contains("Mission History", StringComparison.Ordinal), 5000);
                    frames["home"] = host.Screen();
                }

                using (TuiTestHost host = TuiCase.SignedIn(Width, Height, "/missions", fx.Stub))
                {
                    host.PumpUntil(() => host.Screen().Contains("Fix column widths", StringComparison.Ordinal), 5000);
                    frames["missions"] = host.Screen();
                }

                using (TuiTestHost host = TuiCase.SignedIn(Width, Height, "/approvals", fx.Stub))
                {
                    host.Tui.ApprovalSources.SyncInbox();
                    host.Tui.Context.Status.PollAllAsync().GetAwaiter().GetResult();
                    host.PumpUntil(() => host.Tui.Context.Approvals.Count >= 4, 5000);
                    host.Pump();
                    frames["approvals"] = host.Screen();
                }

                TuiCase.Contains(frames["login"], "Continue", "login");
                TuiCase.Contains(frames["ask"], "Release checklist", "ask");
                TuiCase.Contains(frames["home"], "Mission History", "home");
                TuiCase.Contains(frames["missions"], "Fix column widths", "missions");
                TuiCase.Contains(frames["approvals"], "Staging", "approvals");

                string? dir = Environment.GetEnvironmentVariable("ARMADA_TUI_README_DIR");
                if (!String.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir!);
                    foreach (KeyValuePair<string, string> kvp in frames)
                    {
                        File.WriteAllText(Path.Combine(dir!, kvp.Key + "-" + Width + "x" + Height + ".txt"), TrimRight(Armada.Tui.Theming.AsciiGlyphs.Transliterate(kvp.Value)));
                    }
                }
            }));
            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI README frames", cases: cases);
        }

        private static AskFixtures Fixtures()
        {
            AskFixtures fx = new AskFixtures();
            AskMessage user = AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "What is left before we cut the 1.0 release?");
            AskMessage reply = AskFixtures.Message("amg_2", "ath_1", 2, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text,
                "Two things are open:\n\n1. **Fix column widths** is in progress on DemoRepo.\n2. **Broken** failed its tests; I can restart it.\n\nWant me to dispatch a voyage for the remaining docs work?");
            reply.CaptainId = "cpt_1";
            reply.DurationMs = 3800;
            AskMessage ask = AskFixtures.Message("amg_3", "ath_1", 3, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "Yes, one mission for the README.");
            fx.AddThread(AskFixtures.Thread("ath_1", "Release checklist"), user, reply, ask);
            fx.AddThread(AskFixtures.Thread("ath_2", "Flaky CI on main"));
            fx.AddThread(AskFixtures.Thread("ath_3", "Dependency bumps"));

            StubHttpHandler stub = fx.Stub;
            stub.Json("GET", "/api/v1/status", "{\"TotalCaptains\":3,\"IdleCaptains\":1,\"WorkingCaptains\":1,\"StalledCaptains\":1,\"ActiveVoyages\":1," +
                "\"MissionsByStatus\":{\"Failed\":1,\"Complete\":5,\"InProgress\":1}," +
                "\"Voyages\":[{\"Voyage\":{\"Id\":\"vyg_1\",\"Title\":\"Release train\",\"Status\":\"InProgress\"},\"TotalMissions\":3,\"CompletedMissions\":2,\"FailedMissions\":0,\"VesselIds\":[\"vsl_a\"]}]," +
                "\"RecentSignals\":[{\"Id\":\"sig_1\",\"Type\":\"Progress\",\"Payload\":\"Tests passing on DemoRepo\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[" +
                "{\"Id\":\"msn_f\",\"Title\":\"Fix column widths\",\"Status\":\"InProgress\",\"Priority\":100,\"VesselId\":\"vsl_a\",\"CaptainId\":\"cpt_1\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:05:00Z\"}," +
                "{\"Id\":\"msn_x\",\"Title\":\"Broken\",\"Status\":\"Failed\",\"Priority\":100,\"VesselId\":\"vsl_a\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:30:00Z\"}," +
                "{\"Id\":\"msn_d\",\"Title\":\"Update getting started guide\",\"Status\":\"Complete\",\"Priority\":100,\"VesselId\":\"vsl_a\",\"CreatedUtc\":\"2026-10-04T08:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T08:40:00Z\"}," +
                "{\"Id\":\"msn_r\",\"Title\":\"Add retry to webhook sender\",\"Status\":\"Review\",\"Priority\":50,\"VesselId\":\"vsl_b\",\"CreatedUtc\":\"2026-10-04T07:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T07:50:00Z\"}],\"TotalRecords\":4}");
            stub.Json("GET", "/api/v1/vessel-health/summary", "{\"TotalVessels\":2,\"Pass\":1,\"Warn\":1,\"Fail\":0,\"NotEvaluated\":0,\"OutdatedMajorVessels\":0,\"HighOrCriticalVulnerabilityVessels\":0}");
            stub.Json("GET", "/api/v1/missions/history", "{\"TotalCount\":7,\"CompleteCount\":5,\"FailedCount\":1,\"OtherCount\":1,\"BucketMinutes\":60,\"Buckets\":[" +
                "{\"StartUtc\":\"2026-10-04T08:00:00Z\",\"CompleteCount\":3,\"FailedCount\":0,\"OtherCount\":0}," +
                "{\"StartUtc\":\"2026-10-04T09:00:00Z\",\"CompleteCount\":2,\"FailedCount\":1,\"OtherCount\":1}]}");
            stub.Json("GET", "/api/v1/fleets", "{\"Success\":true,\"Objects\":[{\"Id\":\"flt_1\",\"Name\":\"Default\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/inbox", "[" +
                "{\"Kind\":\"review\",\"Severity\":\"Warning\",\"Title\":\"Review: Add retry to webhook sender\",\"Detail\":\"Waiting 12m\",\"EntityType\":\"mission\",\"EntityId\":\"msn_r\",\"Href\":\"/missions/msn_r\"}," +
                "{\"Kind\":\"landing_failed\",\"Severity\":\"Critical\",\"Title\":\"Landing failed: Broken\",\"Detail\":\"Merge conflict in src/app.cs\",\"EntityType\":\"mission\",\"EntityId\":\"msn_x\",\"Href\":\"/missions/msn_x\"}," +
                "{\"Kind\":\"stalled_captain\",\"Severity\":\"Warning\",\"Title\":\"Stalled captain: codex-2\",\"Detail\":\"No heartbeat for 6m\",\"EntityType\":\"captain\",\"EntityId\":\"cpt_s\",\"Href\":\"/captains/cpt_s\"}," +
                "{\"Kind\":\"deployment_approval\",\"Severity\":\"Warning\",\"Title\":\"Deployment awaiting approval: Staging\",\"Detail\":\"v1.0.0-rc1\",\"EntityType\":\"deployment\",\"EntityId\":\"dpl_1\",\"Href\":\"/deployments/dpl_1\"}]");
            return fx;
        }

        private static string TrimRight(string frame)
        {
            string[] lines = frame.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();
            return String.Join("\n", lines).TrimEnd('\n') + "\n";
        }
    }
}
