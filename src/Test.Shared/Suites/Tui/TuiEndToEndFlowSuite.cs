namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Admin;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end flows: the TUI against a live in-process server (<see cref="E2EServerFixture"/>) with a scripted stub
    /// captain (<see cref="StubCaptainRuntime"/>) standing in for the Claude Code runtime. Covers an Ask Armada dispatch
    /// the captain proposes, approved with <c>a</c> and followed through landing on the vessel's origin; decisions in the
    /// Approvals center (Ask proposals and a deployment); notifications raised by live WebSocket events and opened from
    /// the notification center; a Fleet Action run; and a server settings save.
    /// </summary>
    public sealed class TuiEndToEndFlowSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.EndToEnd.Flows";
        private const string DispatchMarker = "E2E-DISPATCH";
        private const int LiveTimeoutMs = 60000;
        private readonly StubCaptainBehavior _Behavior = new StubCaptainBehavior();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Async(Suite, "ask_dispatch_approval_lands", "Ask: the captain proposes a dispatch, a approves it, the mission lands on the origin, and live events notify", async () =>
            {
                E2EServerFixture fx = await AcquireAsync();
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    VesselSetup setup = await LiveServerSetup.CreateVesselAsync(admin, "ask-e2e");
                    await LiveServerSetup.CreateCaptainAsync(admin, "stub-ask-1");
                    await LiveServerSetup.CreateCaptainAsync(admin, "stub-ask-2");
                    string missionTitle = "Stub change " + Guid.NewGuid().ToString("N").Substring(0, 6);
                    _Behavior.OnTurn = turn => AskTurnAsync(turn, setup.Vessel.Id, missionTitle);

                    using (TuiTestHost host = SignIn(fx, "/ask"))
                    {
                        AssertTrue(host.PumpUntil(() => host.Tui.Ask.Captains.Count >= 2 && host.Tui.Ask.DraftCaptainId.Length > 0, 15000), "captains loaded");
                        host.Type("Please dispatch the change " + DispatchMarker).Press("enter");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath.StartsWith("/ask/ath_", StringComparison.Ordinal), 15000), "thread created and followed");

                        AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Proposals.Values.Any(p => p.Status == AskProposalStatusEnum.Pending), LiveTimeoutMs), "captain's proposal arrives live: " + Errors());
                        AskActionProposal proposal = host.Tui.Ask.Conversation.Proposals.Values.First(p => p.Status == AskProposalStatusEnum.Pending);
                        AssertEqual("dispatch", proposal.ToolName, "proposed tool");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Count == 1, 10000), "proposal in the approvals queue");

                        // Wait for the captain's turn to finish (its reply follows the proposal), so the transcript does not
                        // move the selection to a newer block between selecting the card and pressing a.
                        AssertTrue(host.PumpUntil(() => !host.Tui.Ask.Conversation.TurnActive && host.Tui.Ask.Conversation.Messages.Any(m => m.ContentText.StartsWith("I proposed a dispatch", StringComparison.Ordinal)), LiveTimeoutMs), "the captain's reply arrived");
                        AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                        host.Press("esc");
                        AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Transcript), "Esc moves to the transcript");
                        string cardKey = host.Tui.Ask.Conversation.Messages.First(m => m.ProposalId == proposal.Id).Id;
                        AssertTrue(host.PumpUntil(() => screen.Transcript.SelectNewestPending() && screen.Transcript.SelectedKey == cardKey, 10000), "the pending card is selected");
                        host.Press("a");
                        AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Proposals[proposal.Id].Status == AskProposalStatusEnum.Executed, LiveTimeoutMs), "approved and executed: " + host.Tui.Ask.Conversation.Proposals[proposal.Id].Status);
                        AssertEqual(0, host.Tui.Context.Approvals.Count, "left the approvals queue");

                        Mission? landed = null;
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () =>
                        {
                            landed = (await admin.ListMissionsAsync(new ArmadaPageQuery(1, 50)))?.Objects.FirstOrDefault(m => m.Title == missionTitle);
                            return landed != null && landed.Status == MissionStatusEnum.Complete;
                        }, LiveTimeoutMs), "mission landed (status " + (landed?.Status.ToString() ?? "none") + " " + landed?.FailureReason + "): " + Errors() + "\n" + FixtureLog(fx));

                        AssertTrue(LiveServerSetup.OriginHasStubCommit(setup.BarePath), "the stub captain's commit is on the origin's main branch");
                        AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.TrackedWork.Count > 0, 15000), "the dispatch is tracked in the conversation");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.History.Any(n => n.AssetType == "Mission" && n.Name == missionTitle && n.Status == "Complete"), 15000), "live mission.changed raised a notification");
                        AssertTrue(_Behavior.TurnPrompts.Any(p => p.Contains("The user approved", StringComparison.Ordinal)), "the captain was told about the approval");
                        StopLive(host);
                    }
                }
            }, TestTags.EndToEnd));

            cases.Add(TuiCase.Async(Suite, "approvals_center_decisions", "Approvals center: approve and reject live Ask proposals and approve a deployment; the server records each decision", async () =>
            {
                E2EServerFixture fx = await AcquireAsync();
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    VesselSetup setup = await LiveServerSetup.CreateVesselAsync(admin, "approvals-e2e");
                    Captain captain = await LiveServerSetup.CreateCaptainAsync(admin, "stub-approvals");
                    string approveTitle = "Approve me " + Guid.NewGuid().ToString("N").Substring(0, 6);
                    string rejectTitle = "Reject me " + Guid.NewGuid().ToString("N").Substring(0, 6);
                    _Behavior.OnTurn = turn => AskTurnAsync(turn, setup.Vessel.Id, turn.Prompt.Contains("first change", StringComparison.Ordinal) ? approveTitle : rejectTitle);

                    using (TuiTestHost host = SignIn(fx, "/approvals"))
                    {
                        AskThread first = (await admin.CreateAskThreadAsync(new AskThreadCreateRequest { Title = "Approvals first", CaptainId = captain.Id }))!;
                        await admin.SendAskMessageAsync(first.Id, "Dispatch the first change " + DispatchMarker);
                        AssertTrue(host.PumpUntil(() => FindProposal(host, first.Id) != null, LiveTimeoutMs), "first proposal reaches the center live: " + Errors());
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () => await TurnIdleAsync(admin, first.Id), LiveTimeoutMs), "first turn finished");
                        AskThread second = (await admin.CreateAskThreadAsync(new AskThreadCreateRequest { Title = "Approvals second", CaptainId = captain.Id }))!;
                        await admin.SendAskMessageAsync(second.Id, "Dispatch the second change " + DispatchMarker);
                        AssertTrue(host.PumpUntil(() => FindProposal(host, second.Id) != null, LiveTimeoutMs), "second proposal reaches the center live: " + Errors());

                        Deployment deployment = await LiveServerSetup.CreatePendingDeploymentAsync(admin, setup.Vessel.Id, "E2E production deploy");
                        AssertEqual(DeploymentStatusEnum.PendingApproval, deployment.Status, "deployment waits for approval");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Find(ApprovalKindEnum.DeploymentApproval, deployment.Id) != null, 30000), "deployment approval reaches the center");
                        TuiCase.Contains(host.Screen(), "Ask proposal", "proposals listed");

                        ApprovalItem approveItem = FindProposal(host, first.Id)!;
                        SelectItem(host, approveItem);
                        host.Press("a");
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () => await ProposalStatusAsync(admin, first.Id, approveItem.EntityId) == AskProposalStatusEnum.Executed, LiveTimeoutMs), "approved proposal executed on the server");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Find(ApprovalKindEnum.AskProposal, approveItem.EntityId) == null, 10000), "approved proposal left the center");
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () => ((await admin.ListVoyagesAsync(new ArmadaPageQuery(1, 100)))?.Objects ?? new List<Voyage>()).Any(v => v.Title == approveTitle), 15000), "the approved dispatch created its voyage");

                        ApprovalItem rejectItem = FindProposal(host, second.Id)!;
                        SelectItem(host, rejectItem);
                        host.Press("r");
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () => await ProposalStatusAsync(admin, second.Id, rejectItem.EntityId) == AskProposalStatusEnum.Rejected, LiveTimeoutMs), "rejected proposal recorded on the server");
                        AssertFalse(((await admin.ListVoyagesAsync(new ArmadaPageQuery(1, 100)))?.Objects ?? new List<Voyage>()).Any(v => v.Title == rejectTitle), "the rejected dispatch never ran");

                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Find(ApprovalKindEnum.AskProposal, rejectItem.EntityId) == null, 10000), "rejected proposal left the center");
                        ApprovalItem deployItem = host.Tui.Context.Approvals.Find(ApprovalKindEnum.DeploymentApproval, deployment.Id)!;
                        SelectItem(host, deployItem);
                        host.Press("a");
                        AssertTrue(host.WaitForText("Approve and execute \"E2E production deploy\"?", 5000), "deployment approval asks to confirm\n" + host.Screen());
                        host.Press("y");
                        DeploymentStatusEnum? deployStatus = null;
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () =>
                        {
                            Deployment? current = await admin.GetDeploymentAsync(deployment.Id);
                            deployStatus = current?.Status;
                            return current != null && current.Status != DeploymentStatusEnum.PendingApproval;
                        }, LiveTimeoutMs), "deployment approved on the server: " + deployStatus + "\n" + host.Screen());
                        Deployment? after = await admin.GetDeploymentAsync(deployment.Id);
                        AssertNotEqual(DeploymentStatusEnum.Denied, after!.Status, "approved, not denied");
                        StopLive(host);
                    }
                }
            }, TestTags.EndToEnd));

            cases.Add(TuiCase.Async(Suite, "live_notifications_center", "Notifications: live entity events become entries and toasts; the center opens the entity", async () =>
            {
                E2EServerFixture fx = await AcquireAsync();
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    VesselSetup setup = await LiveServerSetup.CreateVesselAsync(admin, "notify-e2e");
                    using (TuiTestHost host = SignIn(fx, "/jobs"))
                    {
                        IncidentUpsertRequest incident = new IncidentUpsertRequest();
                        incident.Title = "E2E incident " + Guid.NewGuid().ToString("N").Substring(0, 6);
                        incident.VesselId = setup.Vessel.Id;
                        await admin.CreateIncidentAsync(incident);
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.History.Any(n => n.AssetType == "Incident" && n.Name == incident.Title), 15000), "incident.changed raised a notification");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains(incident.Title!, StringComparison.Ordinal)), 5000), "and a toast");

                        Mission request = new Mission("E2E pending mission " + Guid.NewGuid().ToString("N").Substring(0, 6), "No captain can take this one.");
                        request.VesselId = setup.Vessel.Id;
                        Mission pending = (await admin.CreateMissionAsync(request))!;
                        AssertStartsWith("msn_", pending.Id, "mission id read from the wrapped { Mission, Warning } reply");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.History.Any(n => n.AssetType == "Mission" && n.Name == pending.Title), 15000), "mission.changed raised a notification");

                        host.Press("ctrl+n");
                        AssertTrue(host.WaitForText("Notifications", 5000), "center open");
                        AssertTrue(host.WaitForText(incident.Title!, 5000), "incident listed in the center\n" + host.Screen());
                        int unread = host.Tui.Context.Notifications.UnreadCount;
                        AssertTrue(unread >= 2, "unread entries: " + unread);
                        host.Press("enter");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions/" + pending.Id, 5000), "Enter opens the newest entry (the mission): " + host.Tui.Context.Router.Current!.Path);
                        AssertTrue(host.Tui.Context.Notifications.UnreadCount < unread, "opened entry marked read");
                        StopLive(host);
                    }
                }
            }, TestTags.EndToEnd));

            cases.Add(TuiCase.Async(Suite, "fleet_action_run", "Fleet Actions: pick the vessel, preview, review, run, and follow the run to completion", async () =>
            {
                E2EServerFixture fx = await AcquireAsync();
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    VesselSetup setup = await LiveServerSetup.CreateVesselAsync(admin, "fa-e2e");
                    FleetActionUpsertRequest action = new FleetActionUpsertRequest();
                    action.Name = "E2E status " + Guid.NewGuid().ToString("N").Substring(0, 6);
                    action.Kind = FleetActionKindEnum.Command;
                    action.CommandText = "git status --short";
                    action.TimeoutSeconds = 60;
                    action.DefaultConcurrency = 1;
                    FleetAction created = (await admin.CreateFleetActionAsync(action))!;

                    using (TuiTestHost host = SignIn(fx, "/fleet-actions"))
                    {
                        AssertTrue(host.WaitForText(action.Name, 15000), "custom action listed\n" + host.Screen());
                        FleetActionsScreen screen = HubContent<FleetActionsScreen>(host);
                        host.Press("home");
                        for (int i = 0; i < 40 && screen.Grid.Current?.Id != created.Id; i++) host.Press("down");
                        AssertEqual(created.Id, screen.Grid.Current?.Id, "action selected");
                        host.Press("r");
                        AssertTrue(host.WaitForText("Choose vessels to run on", 10000), "picker");
                        AssertTrue(host.WaitForText(setup.Vessel.Name, 10000), "vessel listed\n" + host.Screen());
                        host.Press("tab").Paste(setup.Vessel.Name).Press("enter");
                        host.Press("ctrl+a");
                        AssertTrue(host.WaitForText("1 vessel", 5000), "only our vessel selected\n" + host.Screen());
                        host.Press("ctrl+s");
                        AssertTrue(host.WaitForText("Run fleet action", 10000), "run dialog\n" + host.Screen());
                        AssertTrue(host.WaitForText("Preview for " + setup.Vessel.Name, 10000), "preview\n" + host.Screen());
                        host.Press("ctrl+s");
                        AssertTrue(host.WaitForText("Run on 1 vessel", 10000), "review step\n" + host.Screen());
                        host.Press("ctrl+s");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath.StartsWith("/fleet-actions/runs/", StringComparison.Ordinal), 15000), "navigated to the run\n" + host.Screen());
                        string runId = host.Tui.Context.Router.Current!.FullPath.Substring("/fleet-actions/runs/".Length);
                        FleetActionRunStatusEnum? serverStatus = null;
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () =>
                        {
                            FleetActionRunDetail? detail = await admin.GetFleetActionRunAsync(runId);
                            serverStatus = detail?.Run?.Status;
                            return serverStatus == FleetActionRunStatusEnum.Completed;
                        }, LiveTimeoutMs), "run completed on the server: " + serverStatus);
                        AssertTrue(host.PumpUntil(() => HubContentOrNull<FleetActionRunScreen>(host)?.CurrentRun?.Status == FleetActionRunStatusEnum.Completed, 20000), "run screen shows completion\n" + host.Screen());
                        StopLive(host);
                    }
                }
            }, TestTags.EndToEnd));

            cases.Add(TuiCase.Async(Suite, "settings_save", "Server settings: edit Max Captains, Ctrl+S saves, and the server returns the new value", async () =>
            {
                E2EServerFixture fx = await AcquireAsync();
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    SettingsData before = (await admin.GetSettingsAsync())!;
                    int original = before.MaxCaptains ?? 0;
                    int next = original == 7 ? 8 : 7;
                    using (TuiTestHost host = SignIn(fx, "/server?tab=server"))
                    {
                        ServerSettingsScreen? screen = null;
                        AssertTrue(host.PumpUntil(() =>
                        {
                            screen = HubContentOrNull<ServerSettingsScreen>(host);
                            return screen != null && screen.Settings != null && screen.Health != null;
                        }, 15000), "settings loaded");
                        AssertEqual(original.ToString(CultureInfo.InvariantCulture), screen!.MaxCaptains.Value, "live value shown");
                        screen.Form.Scope.Focus(screen.MaxCaptains);
                        host.Press("ctrl+u").Type(next.ToString(CultureInfo.InvariantCulture)).Press("ctrl+s");
                        AssertTrue(host.WaitForText("Server configuration saved", 15000), "saved toast\n" + host.Screen());
                        SettingsData after = (await admin.GetSettingsAsync())!;
                        AssertEqual(next, after.MaxCaptains ?? 0, "server has the new value");
                        StopLive(host);
                    }

                    SettingsData restore = (await admin.GetSettingsAsync())!;
                    restore.MaxCaptains = original;
                    await admin.UpdateSettingsAsync(restore);
                }
            }, TestTags.EndToEnd));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI end-to-end flows (live server, stub captain)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private async Task<E2EServerFixture> AcquireAsync()
        {
            E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
            StubCaptainRuntime.Install(fx.Server, _Behavior);
            return fx;
        }

        private static TuiTestHost SignIn(E2EServerFixture fx, string route, int width = 160, int height = 48)
        {
            TuiTestHost host = new TuiTestHost(width, height, null, fx.BaseUrl, o => { o.Live = true; o.StartRoute = route; });
            host.Start();
            host.Press("f2").Paste(fx.ApiKey).Press("enter");
            AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null, 15000), "signed in");
            AssertTrue(host.PumpUntil(() => host.Tui.Context.Events.IsLive, 15000), "websocket live");
            return host;
        }

        private static void StopLive(TuiTestHost host)
        {
            host.Tui.Context.Events.Stop();
            host.Tui.Context.Status.Stop();
        }

        private static T HubContent<T>(TuiTestHost host) where T : class
        {
            T? content = HubContentOrNull<T>(host);
            if (content == null) throw new AssertionException("expected " + typeof(T).Name + " but the screen is " + (host.Tui.Shell.Screen?.GetType().Name ?? "none"));
            return content;
        }

        private static T? HubContentOrNull<T>(TuiTestHost host) where T : class
        {
            ScreenBase? screen = host.Tui.Shell.Screen;
            if (screen is HubScreen hub) return hub.Content as T;
            return screen as T;
        }

        private static ApprovalItem? FindProposal(TuiTestHost host, string threadId)
        {
            return host.Tui.Context.Approvals.Items.FirstOrDefault(i => i.Kind == ApprovalKindEnum.AskProposal && i.ParentId == threadId);
        }

        private static void SelectItem(TuiTestHost host, ApprovalItem item)
        {
            ApprovalsScreen screen = HubContent<ApprovalsScreen>(host);
            host.Press("home");
            for (int i = 0; i < 20; i++)
            {
                ApprovalItem? current = screen.Current();
                if (current != null && current.Kind == item.Kind && current.EntityId == item.EntityId) return;
                host.Press("down");
            }

            throw new AssertionException("could not select " + item.Kind + " " + item.EntityId + "\n" + host.Screen());
        }

        private static async Task<AskProposalStatusEnum?> ProposalStatusAsync(ArmadaClient admin, string threadId, string proposalId)
        {
            AskMessagePage? page = await admin.EnumerateAskMessagesAsync(threadId);
            AskMessage? message = page?.Messages.FirstOrDefault(m => m.ProposalId == proposalId && m.Proposal != null);
            return message?.Proposal?.Status;
        }

        private static async Task<bool> TurnIdleAsync(ArmadaClient admin, string threadId)
        {
            AskMessagePage? page = await admin.EnumerateAskMessagesAsync(threadId);
            return page != null && page.Messages.Any(m => m.ContentText.StartsWith("I proposed a dispatch", StringComparison.Ordinal));
        }

        private static async Task<string> AskTurnAsync(StubCaptainTurn turn, string vesselId, string missionTitle)
        {
            if (turn.Prompt.Contains("The user approved", StringComparison.Ordinal)) return "The voyage is running; I will follow it here.";
            if (turn.Prompt.Contains("The user rejected", StringComparison.Ordinal)) return "Understood, nothing was run.";
            if (!turn.Prompt.Contains(DispatchMarker, StringComparison.Ordinal)) return "Done.";

            StubDispatchArguments args = new StubDispatchArguments();
            args.Title = missionTitle;
            args.VesselId = vesselId;
            args.Missions.Add(new MissionDescription(missionTitle, "Add a file (stub captain end-to-end test)."));
            string result = await turn.CallToolAsync("dispatch", JsonSerializer.Serialize(args)).ConfigureAwait(false);
            return "I proposed a dispatch: " + result;
        }

        private static string FixtureLog(E2EServerFixture fx)
        {
            string path = Path.Combine(fx.TempDir, "fixture-warnings.log");
            if (!File.Exists(path)) return "(no server warnings)";
            string[] lines = File.ReadAllLines(path);
            return "server warnings:\n" + String.Join("\n", lines.Skip(Math.Max(0, lines.Length - 20)));
        }

        private string Errors()
        {
            return _Behavior.Errors.Count == 0 ? "no stub errors" : String.Join("; ", _Behavior.Errors);
        }

        #endregion
    }
}
