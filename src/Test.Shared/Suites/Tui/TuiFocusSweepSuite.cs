namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Shell;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui.ActivitySystem;
    using Test.Shared.Suites.Tui.Build;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Testing;
    using TUIKit.Widgets;
    using FocusFrame = Armada.Tui.Widgets.FocusFrame;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The focus treatment on every route (see <see cref="TuiFocusSweep"/>): each route of the route table (and the
    /// record pages with data from each feature suite's stub server) is rendered at 80x24, 120x40, and 160x50, Tab is
    /// pressed through every stop, and at each stop the rendered cells must show exactly one box in the focus style
    /// (heavy glyphs, or <c>#</c> and <c>=</c> in ASCII) around the pane that contains the focused widget, plain light
    /// boxes around every other focus region, and no box over content. Plus the owner's examples (the Ask conversation
    /// list, transcript, and composer; Planning; Dispatch; a tab bar and its content), dialogs, and every palette.
    /// </summary>
    public sealed class TuiFocusSweepSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.FocusSweep";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (int[] size in new int[][] { new int[] { 120, 40 }, new int[] { 80, 24 }, new int[] { 160, 50 } })
            {
                int width = size[0];
                int height = size[1];
                cases.Add(TuiCase.Sync(Suite, "every_route_" + width + "x" + height, "Every route at " + width + "x" + height + ": one focused box, around the focused pane, at every Tab stop", () =>
                {
                    List<string> problems = new List<string>();
                    int stops = 0;
                    foreach (string path in TuiDisplaySuite.AllPaths())
                    {
                        using (TuiTestHost host = TuiCase.SignedIn(width, height, path))
                        {
                            List<string> visited = new List<string>();
                            problems.AddRange(TuiFocusSweep.Sweep(host, path + "@" + width + "x" + height, visited));
                            stops += visited.Count;
                        }
                    }

                    Report(problems);
                    AssertTrue(stops >= TuiDisplaySuite.AllPaths().Count, "visited at least one stop per route (" + stops + ")");
                }));
            }

            cases.Add(TuiCase.Sync(Suite, "focus_audit", "TUIKit's FocusAudit on every route, with and without data: Tab returns to the first stop, Shift+Tab retraces every stop, no stop is hidden, and the layout never moves with focus", () =>
            {
                List<string> problems = new List<string>();
                int audited = 0;
                foreach (string path in TuiDisplaySuite.AllPaths())
                {
                    using (TuiTestHost host = TuiCase.SignedIn(120, 40, path))
                    {
                        // Screens rebuild their forms when background data arrives (Dispatch rebuilds on pipelines,
                        // captains, and personas), which replaces the widgets the audit is walking; start after.
                        host.SettleRequests();
                        audited += Audit(host, path, problems);
                    }
                }

                foreach (TuiFocusRoute route in DataRoutes())
                {
                    using (TuiTestHost host = TuiCase.SignedIn(route.Width, route.Height, route.Path, route.Stub()))
                    {
                        // The record has to be on screen first: stops appear as its fields load.
                        Settle(host);
                        audited += Audit(host, route.Path + "@" + route.Width + "x" + route.Height, problems);
                    }
                }

                Report(problems);
                AssertTrue(audited >= TuiDisplaySuite.AllPaths().Count, "audited most routes (" + audited + ")");
            }));

            cases.Add(TuiCase.Sync(Suite, "focus_audit_slow_reference_data", "FocusAudit on Dispatch waits for slow reference data, which rebuilds the form when it arrives", () =>
            {
                // A slow runner delivered Dispatch's pipelines, captains, and personas after the audit had started; the
                // form rebuild replaced the stop the audit began from, so Tab could never return to it.
                StubHttpHandler stub = TuiFixtures.SignedInServer()
                    .Delay("GET", "/api/v1/pipelines", 400)
                    .Delay("GET", "/api/v1/captains", 400)
                    .Delay("GET", "/api/v1/personas", 400);
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/dispatch?tab=dispatch", stub))
                {
                    AssertTrue(host.SettleRequests(), "requests settled\n" + String.Join("\n", stub.Requests));
                    AssertEqual(0, stub.InFlight, "nothing in flight after settling");
                    DispatchScreen dispatch = (DispatchScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    foreach (string name in new[] { "pipelines", "captains", "personas" })
                        AssertTrue(dispatch.Reference.Loaded.Contains(name), name + " arrived (and the form rebuilt) before the audit starts");
                    List<string> problems = new List<string>();
                    AssertEqual(1, Audit(host, "/dispatch?tab=dispatch (slow data)", problems), "audited");
                    Report(problems);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "routes_with_data", "Record pages, panels, and forms with data: one focused box around the focused pane at every Tab stop", () =>
            {
                List<string> problems = new List<string>();
                foreach (TuiFocusRoute route in DataRoutes())
                {
                    using (TuiTestHost host = TuiCase.SignedIn(route.Width, route.Height, route.Path, route.Stub()))
                    {
                        Settle(host);
                        TuiScreenDump.Write("focus" + route.Path.Replace('/', '_').Replace('?', '_').Replace('=', '_').Replace('&', '_'), host.Screen());
                        problems.AddRange(TuiFocusSweep.Sweep(host, route.Path + "@" + route.Width + "x" + route.Height));
                    }

                    using (TuiTestHost host = TuiCase.SignedIn(80, 24, route.Path, route.Stub()))
                    {
                        Settle(host);
                        TuiScreenDump.Write("focus80" + route.Path.Replace('/', '_').Replace('?', '_').Replace('=', '_').Replace('&', '_'), host.Screen());
                        problems.AddRange(TuiFocusSweep.Sweep(host, route.Path + "@80x24"));
                    }
                }

                Report(problems);
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_conversation_list", "Ask: Tab to the conversation list boxes the whole list (all four edges) and nothing else", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", TuiReadmeFramesSuite.Fixtures().Stub))
                {
                    AskScreen screen = AskOf(host);
                    host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count >= 3, 5000);
                    AssertTrue(PressUntil(host, "tab", () => ReferenceEquals(screen.Scope.Focused, screen.ThreadList) && InMain(host)), "Tab reaches the conversation list");
                    RegionFrame region = AssertFocused(host, screen.ThreadList);
                    AssertTrue(region.Region.Height >= 10 && region.Region.Width >= 20, "the list's box spans the list: " + region.Region);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_transcript", "Ask: the message list (transcript) gets the whole box when focused", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", TuiReadmeFramesSuite.Fixtures().Stub))
                {
                    AskScreen screen = AskOf(host);
                    host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count >= 3, 5000);
                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Transcript), "Esc moves to the transcript");
                    AssertFocused(host, screen.Transcript);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_composer", "Ask: the message box (composer) gets the whole box when focused, at 160 and at 80 columns", () =>
            {
                foreach (int[] size in new int[][] { new int[] { 160, 50 }, new int[] { 80, 24 } })
                {
                    using (TuiTestHost host = TuiCase.SignedIn(size[0], size[1], "/ask/ath_1", TuiReadmeFramesSuite.Fixtures().Stub))
                    {
                        AskScreen screen = AskOf(host);
                        host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count >= 3, 5000);
                        AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Composer), "composer focused on open");
                        AssertFocused(host, screen.Composer);
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "planning_panes", "Planning: the transcript and the composer of a session are separate boxes, and Tab moves the focused box between them", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/planning/ps_1", TuiOpsPlanningSuite.Stub()))
                {
                    Settle(host);
                    HashSet<string> seen = new HashSet<string>();
                    for (int i = 0; i < 12; i++)
                    {
                        if (InMain(host))
                        {
                            RegionFrame? focused = RegionFrames.FocusedOf(host.Tui.Shell.LastRegions);
                            if (focused != null) seen.Add(TuiFocusSweep.Describe(focused.Widget));
                        }

                        host.Press("tab");
                        TuiFocusSweep.Render(host);
                    }

                    AssertTrue(seen.Contains("PlanningComposer"), "composer boxed on its own: " + String.Join(", ", seen));
                    AssertTrue(seen.Contains("PlanningTranscriptView"), "transcript boxed on its own: " + String.Join(", ", seen));
                    Report(TuiFocusSweep.Sweep(host, "/planning/ps_1"));
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dispatch_form", "Dispatch: the form's box is whole and does not cover the page heading", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/dispatch"))
                {
                    Settle(host);
                    AssertTrue(InMain(host), "main focused");
                    RegionFrame? focused = RegionFrames.FocusedOf(host.Tui.Shell.LastRegions);
                    AssertNotNull(focused, "a focused region");
                    AssertEqual("FormView", TuiFocusSweep.Describe(focused!.Widget), "the form holds focus");
                    Report(TuiFocusSweep.Check(host, "/dispatch"));
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "tab_bar_and_content", "A hub: the tab bar's box and the >Tab< marker while the bar has focus; the content's box (and [Tab]) when the content has focus", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    Settle(host);
                    ShellView shell = host.Tui.Shell;
                    Armada.Tui.Screens.HubScreen hub = (Armada.Tui.Screens.HubScreen)shell.Screen!;
                    AssertTrue(PressUntil(host, "tab", () => ReferenceEquals(hub.Scope.Focused, hub.Tabs) && InMain(host)), "Tab reaches the tab bar");
                    RegionFrame bar = AssertFocused(host, hub.Tabs);
                    AssertEqual(1, bar.Region.Height, "the bar's box holds one row");
                    string row = Row(host, bar.Region.Y);
                    TuiCase.Contains(row, ">Missions<", "focused tab marked without color");
                    TuiCase.NotContains(row, "[Missions]", "no bracket marker while the bar has focus");

                    AssertTrue(PressUntil(host, "tab", () => !ReferenceEquals(hub.Scope.Focused, hub.Tabs) && InMain(host)), "Tab moves into the content");
                    TuiFocusSweep.Render(host);
                    RegionFrame content = RegionFrames.FocusedOf(shell.LastRegions)!;
                    AssertTrue(content.Region.Y > bar.Region.Y, "the content's region is below the bar");
                    AssertTrue(TuiFocusSweep.Perimeter(bar.Box).All(p => !TuiFocusSweep.IsFocusedBorder(TuiFocusSweep.Render(host).Get(p.X, p.Y), shell.Theme) || TuiFocusSweep.Perimeter(content.Box).Contains(p)), "the bar's box is plain once the content has focus");
                    TuiCase.Contains(Row(host, bar.Region.Y), "[Missions]", "selected tab bracketed again");
                    Report(TuiFocusSweep.Check(host, "/missions content"));
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sidebar_and_dock", "The sidebar and the Ask dock use the same box: whole and focused when they hold focus, while every screen box is plain", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    Settle(host);
                    ShellView shell = host.Tui.Shell;
                    AssertTrue(PressUntil(host, "shift+tab", () => ReferenceEquals(shell.Scope.Focused, shell.Sidebar)), "Shift+Tab reaches the sidebar");
                    Report(TuiFocusSweep.Check(host, "sidebar"));
                    AssertEqual(shell.LastLayout!.Sidebar, shell.LastFocusedBox, "the sidebar's box is the focused one");
                    AssertTrue(shell.LastRegions.All(r => !r.Focused), "no screen region focused");
                    host.Press("ctrl+j");
                    AssertTrue(PressUntil(host, "tab", () => ReferenceEquals(shell.Scope.Focused, shell.Dock)), "Tab reaches the dock");
                    Report(TuiFocusSweep.Check(host, "dock"));
                    AssertEqual(shell.LastLayout!.Dock, shell.LastFocusedBox, "the dock's box is the focused one");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dialog_holds_focus", "An open dialog draws its own box focused and every pane box behind it plain", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    Settle(host);
                    ShellView shell = host.Tui.Shell;
                    host.Press("ctrl+k");
                    AssertTrue(host.Tui.Context.Modals.IsModalOpen, "palette open");
                    Report(TuiFocusSweep.CheckModal(host, "palette"));
                    host.Press("esc");
                    AssertFalse(host.Tui.Context.Modals.IsModalOpen, "palette closed");
                    Report(TuiFocusSweep.Check(host, "after the dialog"));
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "every_palette", "Dark, light, high contrast (also the NO_COLOR default), and ASCII glyphs: the focused box differs from a plain box in glyph and style, not by color alone", () =>
            {
                foreach (ArmadaTheme palette in new ArmadaTheme[] { ThemePalettes.Dark(), ThemePalettes.Light(), ThemePalettes.HighContrast() })
                {
                    AssertTrue(palette.FocusBorder != palette.Border, palette.Name + ": focus style differs");
                    AssertTrue(palette.FocusBorder.HasAttribute(CellAttributes.Bold), palette.Name + ": focus style bold");
                    AssertTrue(palette.FocusBorder.Foreground != palette.FocusBorder.Background, palette.Name + ": focus border legible");
                    string focusedGlyphs = FocusFrame.GlyphsFor(palette, true);
                    string plainGlyphs = FocusFrame.UnfocusedGlyphsFor(palette);
                    AssertFalse(focusedGlyphs.Any(c => plainGlyphs.Contains(c)), palette.Name + ": focused glyphs never used by plain boxes");
                }

                AssertTrue(ThemePalettes.HighContrast().FocusBorder.HasAttribute(CellAttributes.Reverse), "high contrast: reverse video");
                foreach (string command in new string[] { "view.theme.light", "view.theme.high-contrast", "view.icons.ascii" })
                {
                    using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                    {
                        Settle(host);
                        host.Tui.Context.Commands.Execute(command);
                        host.Tui.Context.Notifications.DismissToasts();
                        List<string> problems = TuiFocusSweep.Sweep(host, "/missions " + command);
                        Report(problems);
                        if (command == "view.icons.ascii")
                        {
                            string text = host.Screen();
                            AssertFalse(text.Any(c => c > 0x7F), "ASCII snapshot");
                            TuiCase.Contains(text, "#=", "focused box in # and =");
                        }
                    }
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI focus treatment on every route", cases: cases);
        }

        /// <summary>
        /// Record pages and panels with data, from the feature suites' stub servers.
        /// </summary>
        /// <returns>Routes.</returns>
        internal static List<TuiFocusRoute> DataRoutes()
        {
            List<TuiFocusRoute> routes = new List<TuiFocusRoute>();
            routes.Add(new TuiFocusRoute("/", 120, 40, () => TuiReadmeFramesSuite.Fixtures().Stub));
            routes.Add(new TuiFocusRoute("/ask/ath_1", 160, 50, () => TuiReadmeFramesSuite.Fixtures().Stub));
            routes.Add(new TuiFocusRoute("/inbox", 120, 40, () => TuiReadmeFramesSuite.Fixtures().Stub));
            routes.Add(new TuiFocusRoute("/approvals", 120, 40, () => TuiReadmeFramesSuite.Fixtures().Stub));
            routes.Add(new TuiFocusRoute("/planning", 150, 50, TuiOpsPlanningSuite.Stub));
            routes.Add(new TuiFocusRoute("/planning/ps_1", 150, 50, TuiOpsPlanningSuite.Stub));
            routes.Add(new TuiFocusRoute("/missions/msn_m", 150, 50, TuiOpsMissionDetailSuite.Stub));
            routes.Add(new TuiFocusRoute("/missions/msn_w", 150, 50, TuiOpsMissionDetailSuite.Stub));
            routes.Add(new TuiFocusRoute("/missions", 150, 50, TuiOpsMissionsSuite.Stub));
            routes.Add(new TuiFocusRoute("/backlog/obj_a", 160, 50, TuiOpsBacklogItemSuite.Stub));
            routes.Add(new TuiFocusRoute("/dispatch?tab=backlog", 160, 50, TuiOpsBacklogSuite.Stub));
            routes.Add(new TuiFocusRoute("/fleet-actions", 150, 45, TuiOpsFleetActionsSuite.Stub));
            routes.Add(new TuiFocusRoute("/fleet-actions/runs/far_1", 190, 45, TuiOpsFleetActionsSuite.Stub));
            routes.Add(new TuiFocusRoute("/merge-queue/mrg_1", 150, 45, TuiOpsMergeQueueSuite.Stub));
            routes.Add(new TuiFocusRoute("/voyages/vyg_b", 150, 45, TuiOpsVoyagesSuite.Stub));
            routes.Add(new TuiFocusRoute("/voyages/create", 150, 45, TuiOpsVoyagesSuite.Stub));
            routes.Add(new TuiFocusRoute("/deployments/dpl_1", 150, 45, TuiDeploymentsSuite.Server));
            routes.Add(new TuiFocusRoute("/checks/chk_2", 150, 45, TuiChecksSuite.Server));
            routes.Add(new TuiFocusRoute("/environments/env_1", 150, 45, TuiEnvironmentsSuite.Server));
            routes.Add(new TuiFocusRoute("/incidents/inc_1", 150, 45, TuiIncidentsSuite.Server));
            routes.Add(new TuiFocusRoute("/releases/rel_1", 150, 45, TuiReleasesSuite.Server));
            routes.Add(new TuiFocusRoute("/runbooks/rbk_1?executionId=rex_1", 150, 45, TuiRunbooksSuite.Server));
            routes.Add(new TuiFocusRoute("/personas/Worker", 150, 45, TuiPersonasSuite.Server));
            routes.Add(new TuiFocusRoute("/pipelines/Default", 150, 45, TuiPipelinesSuite.Server));
            routes.Add(new TuiFocusRoute("/prompt-templates/mission.rules", 150, 45, TuiPromptsSuite.Server));
            routes.Add(new TuiFocusRoute("/playbooks/pbk_1", 150, 45, TuiPlaybooksSuite.Server));
            routes.Add(new TuiFocusRoute("/skills/skl_1", 150, 45, TuiSkillsSuite.Server));
            routes.Add(new TuiFocusRoute("/workflow-profiles/wfp_1", 150, 45, TuiWorkflowProfilesSuite.Server));
            routes.Add(new TuiFocusRoute("/project-profiles/ppr_1", 150, 45, TuiProjectProfilesSuite.Server));
            routes.Add(new TuiFocusRoute("/configuration?tab=memory", 150, 45, TuiMemorySuite.Server));
            routes.Add(new TuiFocusRoute("/configuration?tab=harbors", 150, 45, TuiHarborsSuite.Server));
            routes.Add(new TuiFocusRoute("/configuration?tab=endpoints", 150, 45, TuiEndpointsSuite.Server));
            routes.Add(new TuiFocusRoute("/captains", 170, 50, TuiBuildCaptainsSuite.Stub));
            routes.Add(new TuiFocusRoute("/captains/cpt_2", 170, 50, TuiBuildCaptainsSuite.Stub));
            routes.Add(new TuiFocusRoute("/workspace/vsl_demo", 170, 50, TuiBuildWorkspaceSuite.Stub));
            routes.Add(new TuiFocusRoute("/vessels/health", 190, 55, TuiBuildHealthSuite.Stub));
            routes.Add(new TuiFocusRoute("/vessels/vsl_demo", 180, 60, TuiBuildVesselsSuite.Stub));
            routes.Add(new TuiFocusRoute("/vessels/vsl_demo/onboarding", 180, 60, TuiBuildVesselsSuite.Stub));
            routes.Add(new TuiFocusRoute("/fleets/flt_web", 160, 45, BuildStubs.Server));
            routes.Add(new TuiFocusRoute("/vessels/import", 170, 50, () => TuiBuildImportSuite.Stub(() => 0)));
            routes.Add(new TuiFocusRoute("/activity?source=history", 170, 44, TuiActivityHistorySuite.Stub));
            routes.Add(new TuiFocusRoute("/server?tab=users", 160, 40, TuiAdminSuite.Stub));
            routes.Add(new TuiFocusRoute("/api-explorer/updateMission", 160, 50, TuiApiExplorerSuite.Stub));
            routes.Add(new TuiFocusRoute("/events/evt_1", 140, 40, TuiEventsJobsSuite.Stub));
            routes.Add(new TuiFocusRoute("/server?tab=server", 160, 50, TuiServerSettingsSuite.Stub));
            routes.Add(new TuiFocusRoute("/signals/sig_1", 140, 40, TuiSignalsSuite.Stub));
            routes.Add(new TuiFocusRoute("/activity?source=tokens", 160, 44, TuiTokenUsageSuite.Stub));
            routes.Add(new TuiFocusRoute("/requests/req_1", 160, 50, TuiRequestHistorySuite.Stub));
            routes.Add(new TuiFocusRoute("/setup", 140, 50, TuiSetupWizardSuite.PopulatedServer));
            return routes;
        }

        /// <summary>
        /// Run TUIKit's <see cref="FocusAudit"/> on a host's started application (hosts share the process, see
        /// <see cref="TuiTestHost.StartApp"/>) with any error dialog closed (again if one opens during the audit). The audit's
        /// focus-indicator check is off: Armada draws every box inside one TUIKit region, so TUIKit sees no region frame,
        /// and <see cref="TuiFocusSweep"/> checks the boxes cell by cell instead. The import wizard is skipped: it keeps Tab
        /// inside the wizard, so Tab stops moving at its last stop.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="label">Route label for problems.</param>
        /// <param name="problems">Problems found.</param>
        /// <returns>1 when the route was audited, 0 when it was skipped.</returns>
        private static int Audit(TuiTestHost host, string label, List<string> problems)
        {
            if (label.StartsWith("/vessels/import", StringComparison.Ordinal)) return 0;
            host.StartApp();
            FocusAuditOptions options = new FocusAuditOptions();
            options.CheckFocusIndicator = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                host.Pump();
                for (int i = 0; i < 4 && host.Tui.Context.Modals.IsModalOpen; i++) host.Press("esc");
                if (host.Tui.Context.Modals.IsModalOpen) break;
                FocusAuditResult result = FocusAudit.Run(host.App, options);
                // A route without data reports its failed load in a dialog, which takes Tab while it is open; audit again
                // once it is closed.
                if (host.Tui.Context.Modals.IsModalOpen) continue;
                foreach (FocusAuditProblem problem in result.Problems) problems.Add(label + ": " + problem);
                return 1;
            }

            problems.Add(label + ": a dialog stayed open");
            return 0;
        }

        private static void Settle(TuiTestHost host)
        {
            host.SettleRequests();
            string previous = "";
            int stable = 0;
            host.PumpUntil(() =>
            {
                string current = host.Screen();
                stable = current == previous && !current.Contains("Loading", StringComparison.Ordinal) ? stable + 1 : 0;
                previous = current;
                return stable >= 3;
            }, 3000);
        }

        private static void Report(List<string> problems)
        {
            if (problems.Count == 0) return;
            throw new AssertionException(problems.Count + " focus problems:\n" + String.Join("\n", problems.Take(40)));
        }

        private static AskScreen AskOf(TuiTestHost host)
        {
            return (AskScreen)host.Tui.Shell.Screen!;
        }

        private static bool InMain(TuiTestHost host)
        {
            return ReferenceEquals(host.Tui.Shell.Scope.Focused, host.Tui.Shell.Screen);
        }

        private static bool PressUntil(TuiTestHost host, string key, Func<bool> condition)
        {
            for (int i = 0; i < 24 && !condition(); i++) host.Press(key);
            return condition();
        }

        private static string Row(TuiTestHost host, int y)
        {
            return host.Screen().Split('\n')[y];
        }

        /// <summary>
        /// Check the whole treatment and that the focused region is the given widget's, whole on all four sides.
        /// </summary>
        private static RegionFrame AssertFocused(TuiTestHost host, IWidget widget)
        {
            Report(TuiFocusSweep.Check(host, TuiFocusSweep.Describe(widget)));
            ShellView shell = host.Tui.Shell;
            RegionFrame? region = RegionFrames.FocusedOf(shell.LastRegions);
            AssertNotNull(region, "a focused region");
            AssertTrue(ReferenceEquals(region!.Widget, widget), "the focused region is " + TuiFocusSweep.Describe(widget) + ", not " + TuiFocusSweep.Describe(region.Widget));
            CellBuffer frame = TuiFocusSweep.Render(host);
            Rect box = region.Box.Intersect(shell.LastLayout!.Main);
            AssertEqual(region.Box, box, "the box lies inside the main pane");
            foreach (Point p in TuiFocusSweep.Perimeter(box))
                AssertTrue(TuiFocusSweep.IsFocusedBorder(frame.Get(p.X, p.Y), shell.Theme), "focused edge at " + p.X + "," + p.Y + " (\"" + frame.Get(p.X, p.Y).Grapheme + "\")");
            AssertEqual("\u250F", frame.Get(box.X, box.Y).Grapheme, "heavy top-left corner");
            AssertEqual("\u251B", frame.Get(box.Right - 1, box.Bottom - 1).Grapheme, "heavy bottom-right corner");
            return region;
        }
    }
}
