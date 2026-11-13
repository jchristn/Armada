namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// TUI concurrency views (CONCURRENCY_IMPROVEMENTS.md M9): the voyage Tree tab, the mission Wait Time and Children
    /// sections, and the vessel Concurrency section. Set ARMADA_TUI_FRAMES to a directory to keep the rendered frames.
    /// </summary>
    public sealed class TuiConcurrencyViewsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.ConcurrencyViews";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "voyage_tree_tab", "Voyage Tree: logical missions indented by parent, the pipeline per row, branch markers, integration last; Enter opens the mission", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/voyages/vyg_t", "{\"Voyage\":{\"Id\":\"vyg_t\",\"Title\":\"Audit logging\",\"Status\":\"InProgress\",\"CreatedUtc\":\"2026-11-05T10:00:00Z\",\"LastUpdateUtc\":\"2026-11-05T10:00:00Z\"},\"TotalMissions\":5,\"MissionCountsByStatus\":{}}");
                stub.Json("GET", "/api/v1/missions", StubHttpHandler.PageJson("[]"));
                stub.Json("GET", "/api/v1/voyages/vyg_t/blackbox", "{\"Error\":\"NotFound\",\"Message\":\"none\"}", HttpStatusCode.NotFound);
                VoyageTree tree = new VoyageTree
                {
                    VoyageId = "vyg_t", Title = "Audit logging", TotalMissions = 5, MaxDepth = 1,
                    Roots = new List<MissionTreeNode>
                    {
                        Node(Summary("msn_doc", "Document auditing [Worker]", MissionStatusEnum.Pending, integration: true)),
                        Node(Summary("msn_p", "Add the audit table [Worker]", MissionStatusEnum.Complete),
                            new List<MissionSummary> { Summary("msn_pj", "Add the audit table [Judge]", MissionStatusEnum.Complete) },
                            new List<MissionTreeNode>
                            {
                                Node(Summary("msn_c", "Write audit entries [Worker]", MissionStatusEnum.InProgress, "msn_p", MissionBranchStrategyEnum.ContinueParent)),
                                Node(Summary("msn_f", "Show the audit page [Worker]", MissionStatusEnum.Pending, "msn_p", MissionBranchStrategyEnum.ForkFromParent))
                            })
                    }
                };
                stub.Json("GET", "/api/v1/voyages/vyg_t/tree", Armada.Client.ArmadaJson.Serialize(tree));
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/voyages/vyg_t", stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is VoyageScreen v && v.Voyage != null), "voyage loaded\n" + host.Screen());
                    VoyageScreen screen = (VoyageScreen)host.Tui.Shell.Screen!;
                    AssertEqual(0, stub.CountFor("GET", "/api/v1/voyages/vyg_t/tree"), "read only when the tab opens");
                    for (int i = 0; i < 12 && screen.CurrentPanelKey != "tree"; i++) host.Press("]");
                    AssertEqual("tree", screen.CurrentPanelKey);
                    AssertTrue(host.PumpUntil(() => screen.Tree.Rows.Count == 4), "four logical missions\n" + host.Screen());
                    AssertEqual("msn_p,msn_c,msn_f,msn_doc", String.Join(",", screen.Tree.Rows.Select(r => r.Mission.Id)), "children under the parent, integration last");
                    AssertEqual("0,1,1,0", String.Join(",", screen.Tree.Rows.Select(r => r.Depth)));
                    AssertTrue(host.WaitForText("Worker: Complete > Judge: Complete"), "pipeline column\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "  > Write audit entries", "indented child with the base title");
                    TuiCase.Contains(frame, "continues parent", "branch marker");
                    TuiCase.Contains(frame, "forks from parent", "fork marker");
                    TuiCase.Contains(frame, "integration", "integration marker");
                    Keep("voyage_tree", frame);
                    host.Press("down");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions/msn_c"), "Enter opens the mission");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "mission_waits_and_children", "Mission: a Wait Time section with the summary, slot wait and history, and a Children section with branch markers", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                Mission mission = new Mission("Write audit entries [Worker]", "do it") { Id = "msn_c", Status = MissionStatusEnum.WorkProduced, ParentMissionId = "msn_p",
                    ReadyUtc = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), AssignedUtc = new DateTime(2026, 11, 5, 9, 12, 0, DateTimeKind.Utc) };
                stub.Json("GET", "/api/v1/missions/msn_c", Armada.Client.ArmadaJson.Serialize(mission));
                List<MissionWaitInterval> waits = new List<MissionWaitInterval>
                {
                    new MissionWaitInterval { MissionId = "msn_c", Gate = MissionWaitGateEnum.Parent, StartedUtc = new DateTime(2026, 11, 5, 8, 40, 0, DateTimeKind.Utc), EndedUtc = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), Detail = "ParentNotFinished" },
                    new MissionWaitInterval { MissionId = "msn_c", Gate = MissionWaitGateEnum.CaptainSlot, StartedUtc = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), EndedUtc = new DateTime(2026, 11, 5, 9, 12, 0, DateTimeKind.Utc) }
                };
                stub.Json("GET", "/api/v1/missions/msn_c/waits", Armada.Client.ArmadaJson.Serialize(new EnumerationResult<MissionWaitInterval> { Objects = waits, TotalRecords = 2, PageSize = 100 }));
                stub.Json("GET", "/api/v1/missions/summaries", Armada.Client.ArmadaJson.Serialize(new EnumerationResult<MissionSummary>
                {
                    Objects = new List<MissionSummary> { Summary("msn_g", "Export as CSV [Worker]", MissionStatusEnum.Failed, "msn_c", MissionBranchStrategyEnum.ContinueParent) }, TotalRecords = 1, PageSize = 50
                }));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/missions/msn_c", stub))
                {
                    AssertTrue(host.WaitForText("Wait Time"), "wait section\n" + host.Screen());
                    AssertTrue(host.WaitForText("Waited 32m before starting (Parent mission 20m, Captain slot 12m)"), "summary line\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Slot wait 12m", "slot wait");
                    TuiCase.Contains(frame, "Parent mission  20m  Parent not finished", "history row with a readable detail");
                    TuiCase.Contains(frame, "Export as CSV", "child");
                    TuiCase.Contains(frame, "continues parent", "child branch marker");
                    AssertTrue(stub.Saw("GET", "/api/v1/missions/summaries", r => r.Query.Contains("parentMissionId=msn_c")), "children read by parent");
                    Keep("mission_waits", frame);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "vessel_concurrency_section", "Vessel: a Concurrency section with slots against the limit, slot wait, holders and waiting missions", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                Vessel vessel = new Vessel("api", "https://example.invalid/repo") { Id = "vsl_c", MaxParallelVoyages = 2, MaxParallelMissionsPerVoyage = 3 };
                stub.Json("GET", "/api/v1/vessels", Armada.Client.ArmadaJson.Serialize(new EnumerationResult<Vessel> { Objects = new List<Vessel> { vessel }, TotalRecords = 1 }));
                stub.Json("GET", "/api/v1/fleets", StubHttpHandler.PageJson("[]"));
                stub.Json("GET", "/api/v1/missions/summaries", StubHttpHandler.PageJson("[]"));
                stub.Json("GET", "/api/v1/pipelines", StubHttpHandler.PageJson("[]"));
                VesselConcurrency report = new VesselConcurrency
                {
                    VesselId = "vsl_c", Name = "api", MaxParallelVoyages = 2, MaxParallelMissionsPerVoyage = 3, VoyageSlotsInUse = 2, RunningMissions = 3, WaitingCount = 1,
                    HoldingVoyages = new List<VesselVoyageSlotUsage> { new VesselVoyageSlotUsage { VoyageId = "vyg_t", Title = "Audit logging", RunningMissions = 2, PendingMissions = 1 } },
                    Waiting = new List<WaitingMissionInfo> { new WaitingMissionInfo { MissionId = "msn_w", Title = "Queued work", Gate = MissionWaitGateEnum.VesselVoyageSlot, WaitedSeconds = 540 } },
                    SlotWait24h = new SlotWaitStatistics { WindowHours = 24, Missions = 3, P50Seconds = 60, P95Seconds = 600, MaxSeconds = 900 },
                    SlotWait7d = new SlotWaitStatistics { WindowHours = 168 }
                };
                stub.Json("GET", "/api/v1/vessels/vsl_c/concurrency", Armada.Client.ArmadaJson.Serialize(report));
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/vessels/vsl_c", stub))
                {
                    AssertTrue(host.WaitForText("Concurrency"), "section\n" + host.Screen());
                    AssertTrue(host.WaitForText("2 / 2 in use"), "voyage slots\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "p50 1m, p95 10m, max 15m (3 missions)", "24h slot wait");
                    TuiCase.Contains(frame, "No waits recorded", "empty 7d window");
                    TuiCase.Contains(frame, "Audit logging  2 running, 1 pending", "holder");
                    TuiCase.Contains(frame, "Queued work  Vessel voyage slot  9m", "waiting mission");
                    Keep("vessel_concurrency", frame);
                }
            }));

            return new TestSuiteDescriptor(Suite, "TUI concurrency views", cases);
        }

        private static MissionSummary Summary(string id, string title, MissionStatusEnum status, string? parent = null,
            MissionBranchStrategyEnum strategy = MissionBranchStrategyEnum.New, bool integration = false)
            => new MissionSummary { Id = id, Title = title, Status = status, ParentMissionId = parent, BranchStrategy = strategy, WaitForVoyageWorkers = integration, VoyageId = "vyg_t" };

        private static MissionTreeNode Node(MissionSummary mission, List<MissionSummary>? stages = null, List<MissionTreeNode>? children = null)
            => new MissionTreeNode { Mission = mission, Stages = stages ?? new List<MissionSummary>(), Children = children ?? new List<MissionTreeNode>() };

        private static void Keep(string name, string frame)
        {
            string? dir = Environment.GetEnvironmentVariable("ARMADA_TUI_FRAMES");
            if (String.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".txt"), frame);
        }
    }
}
