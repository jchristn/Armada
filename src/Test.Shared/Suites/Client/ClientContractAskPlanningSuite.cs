namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server with a scripted stub captain (<see cref="StubCaptainRuntime"/>):
    /// Ask Armada turns, quick actions and proposal decisions, tracked-work snapshots, captain chat, planning sessions
    /// through dispatch, objectives and backlog items, and refinement sessions through apply.
    /// </summary>
    public sealed class ClientContractAskPlanningSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.AskPlanning";
        private const int LiveTimeoutMs = 60000;
        private readonly StubCaptainBehavior _Behavior = new StubCaptainBehavior();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("ask_turns_and_proposals", "Ask: send, read, summarize, cancel; quick action proposals rejected and approved; work snapshot; captain chat", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "ask");
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-ask");
                await LiveServerSetup.CreateCaptainAsync(c, "contract-ask-worker");
                _Behavior.OnTurn = turn => Task.FromResult("Contract reply.");
                AskThread thread = (await c.CreateAskThreadAsync(new AskThreadCreateRequest { Title = "Contract ask", CaptainId = captain.Id }))!;

                AskSendMessageResult? sent = await c.SendAskMessageAsync(thread.Id, "Hello captain");
                AssertFalse(String.IsNullOrEmpty(sent?.MessageId), "message id");
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => ((await c.EnumerateAskMessagesAsync(thread.Id))?.Messages ?? new List<AskMessage>()).Any(m => m.ContentText == "Contract reply."), LiveTimeoutMs), "captain replied");
                await c.MarkAskThreadReadAsync(thread.Id);
                await c.SummarizeAskThreadAsync(thread.Id);
                try
                {
                    await c.CancelAskTurnAsync(thread.Id);
                }
                catch (ArmadaApiException ex)
                {
                    AssertTrue(ex.StatusCode == 404 || ex.StatusCode == 409, "no turn to cancel: " + ex.StatusCode);
                }

                ArmadaRawJson args = new ArmadaRawJson(JsonSerializer.Serialize(new StubDispatchArguments { Title = "Contract quick dispatch", VesselId = setup.Vessel.Id, Missions = new List<MissionDescription> { new MissionDescription("Contract quick dispatch", "Add a file.") } }));
                AskActionProposal? quick = await c.RunAskQuickActionAsync(thread.Id, "dispatch", args);
                AssertEqual(AskProposalStatusEnum.Executed, quick?.Status, "a user's quick action runs at once");

                _Behavior.OnTurn = async turn =>
                {
                    if (!turn.Prompt.Contains("PROPOSE-DISPATCH", StringComparison.Ordinal)) return "Noted.";
                    StubDispatchArguments proposed = new StubDispatchArguments { Title = "Contract proposed dispatch", VesselId = setup.Vessel.Id };
                    proposed.Missions.Add(new MissionDescription("Contract proposed dispatch", "Add a file."));
                    await turn.CallToolAsync("dispatch", JsonSerializer.Serialize(proposed)).ConfigureAwait(false);
                    return "Proposed.";
                };
                AskActionProposal toReject = await ProposeAsync(c, thread.Id);
                AssertEqual(AskProposalStatusEnum.Rejected, (await c.RejectAskProposalAsync(thread.Id, toReject.Id))?.Status, "captain proposal rejected");
                AskActionProposal toApprove = await ProposeAsync(c, thread.Id);
                AskActionProposal? executed = await c.ApproveAskProposalAsync(thread.Id, toApprove.Id);
                AssertTrue(executed?.Status == AskProposalStatusEnum.Executed || executed?.Status == AskProposalStatusEnum.Approved, "captain proposal approved: " + executed?.Status);
                AskTrackedWork? work = null;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    work = (await c.GetAskThreadAsync(thread.Id))?.TrackedWork.FirstOrDefault();
                    return work != null;
                }, LiveTimeoutMs), "dispatch tracked in the thread");
                AskWorkSnapshot? snapshot = await c.GetAskWorkSnapshotAsync(thread.Id, work!.Id);
                AssertEqual(work.Id, snapshot?.TrackedWorkId, "work snapshot");

                CaptainChatRequest chat = new CaptainChatRequest();
                chat.Message = "Status?";
                CaptainChatResponse? reply = await c.ChatWithCaptainAsync(captain.Id, chat);
                AssertTrue(reply?.Success == true, "chat succeeded: " + reply?.Error);
                AssertFalse(String.IsNullOrEmpty(reply!.Reply), "chat reply");
            }));

            cases.Add(Case("planning_sessions", "Planning: create, message, summarize, dispatch, stop turn, stop, get, delete", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "planning");
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-planner");
                _Behavior.OnPrompt = prompt => "Plan: add one file written by the stub captain.";
                PlanningSessionCreateRequest create = new PlanningSessionCreateRequest();
                create.Title = "Contract planning";
                create.CaptainId = captain.Id;
                create.VesselId = setup.Vessel.Id;
                PlanningSessionDetail session = (await c.CreatePlanningSessionAsync(create))!;
                string id = session.Session!.Id;
                await c.SendPlanningSessionMessageAsync(id, new PlanningSessionMessageRequest { Content = "Plan a tiny change." });
                PlanningSessionMessage? answer = null;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    PlanningSessionDetail? detail = await c.GetPlanningSessionAsync(id);
                    answer = detail?.Messages.LastOrDefault(m => m.Role != "User" && m.Content.Contains("Plan:", StringComparison.Ordinal));
                    return answer != null && detail!.Session!.Status != PlanningSessionStatusEnum.Responding;
                }, LiveTimeoutMs), "captain answered: " + String.Join("; ", _Behavior.Errors));
                PlanningSessionSummaryResponse? summary = await c.SummarizePlanningSessionAsync(id, new PlanningSessionSummaryRequest { MessageId = answer!.Id });
                AssertEqual(id, summary?.SessionId, "summary");
                Voyage? voyage = await c.DispatchPlanningSessionAsync(id, new PlanningSessionDispatchRequest { MessageId = answer.Id, Title = "Contract planned voyage" });
                AssertStartsWith("vyg_", voyage?.Id, "dispatched voyage");
                await c.StopPlanningTurnAsync(id);
                await c.StopPlanningSessionAsync(id);
                AssertTrue((await c.ListPlanningSessionsAsync())!.Any(p => p.Id == id), "listed");
                await c.DeletePlanningSessionAsync(id);
            }));

            cases.Add(Case("objectives_and_refinement", "Objectives and backlog: CRUD, enumerate, reorder, GitHub import error; refinement sessions through apply", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "objectives");
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-refiner");
                Objective first = (await c.CreateObjectiveAsync(new ObjectiveUpsertRequest { Title = "Contract objective one" }))!;
                Objective second = (await c.CreateObjectiveAsync(new ObjectiveUpsertRequest { Title = "Contract objective two" }))!;
                AssertEqual(first.Title, (await c.GetObjectiveAsync(first.Id))?.Title, "get objective");
                AssertEqual("updated", (await c.UpdateObjectiveAsync(first.Id, new ObjectiveUpsertRequest { Description = "updated" }))?.Description, "update objective");
                AssertTrue((await c.ListObjectivesAsync(new ObjectiveQuery { PageSize = 100 }))!.Objects.Any(o => o.Id == first.Id), "list objectives");
                AssertTrue((await c.EnumerateObjectivesAsync(new ObjectiveQuery { PageSize = 100 }))!.Objects.Any(o => o.Id == second.Id), "enumerate objectives");
                ObjectiveReorderRequest reorder = new ObjectiveReorderRequest();
                reorder.Items = new List<ObjectiveReorderItem> { new ObjectiveReorderItem { ObjectiveId = second.Id, Rank = 1 }, new ObjectiveReorderItem { ObjectiveId = first.Id, Rank = 2 } };
                AssertTrue((await c.ReorderObjectivesAsync(reorder))!.Any(o => o.Id == second.Id && o.Rank == 1), "reorder objectives");
                AssertTrue((await c.ReorderBacklogAsync(reorder))!.Any(o => o.Id == first.Id && o.Rank == 2), "reorder backlog");
                AssertEqual("backlog edit", (await c.UpdateBacklogItemAsync(second.Id, new ObjectiveUpsertRequest { Description = "backlog edit" }))?.Description, "update backlog item");
                await ClientContract.ExpectErrorAsync(() => c.ImportObjectiveFromGitHubAsync(new GitHubObjectiveImportRequest { VesselId = setup.Vessel.Id, Number = 1 }), "a local vessel has no GitHub repository");

                _Behavior.OnPrompt = prompt => "Summary: ship a tiny change.\nAcceptance criteria:\n- A file exists.";
                ObjectiveRefinementSessionCreateRequest refine = new ObjectiveRefinementSessionCreateRequest();
                refine.CaptainId = captain.Id;
                refine.VesselId = setup.Vessel.Id;
                refine.Title = "Contract refinement";
                ObjectiveRefinementSessionDetail backlogSession = (await c.CreateBacklogRefinementSessionAsync(first.Id, refine))!;
                string sid = backlogSession.Session.Id;
                await c.SendObjectiveRefinementMessageAsync(sid, new ObjectiveRefinementMessageRequest { Content = "Refine it." });
                ObjectiveRefinementMessage? answer = null;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    ObjectiveRefinementSessionDetail? detail = await c.GetObjectiveRefinementSessionAsync(sid);
                    answer = detail?.Messages.LastOrDefault(m => m.Role != "User" && m.Content.Contains("Summary:", StringComparison.Ordinal));
                    return answer != null && detail!.Session.Status != ObjectiveRefinementSessionStatusEnum.Responding;
                }, LiveTimeoutMs), "captain answered: " + String.Join("; ", _Behavior.Errors));
                AssertEqual(sid, (await c.SummarizeObjectiveRefinementSessionAsync(sid, new ObjectiveRefinementSummaryRequest { MessageId = answer!.Id }))?.SessionId, "summarize");
                AssertEqual(first.Id, (await c.ApplyObjectiveRefinementSummaryAsync(sid, new ObjectiveRefinementApplyRequest { MessageId = answer.Id }))?.Objective.Id, "apply");
                AssertTrue((await c.ListBacklogRefinementSessionsAsync(first.Id))!.Any(r => r.Id == sid), "backlog sessions");
                await c.StopObjectiveRefinementSessionAsync(sid);
                await c.DeleteObjectiveRefinementSessionAsync(sid);

                ObjectiveRefinementSessionDetail objectiveSession = (await c.CreateObjectiveRefinementSessionAsync(second.Id, refine))!;
                AssertTrue((await c.ListObjectiveRefinementSessionsAsync(second.Id))!.Any(r => r.Id == objectiveSession.Session.Id), "objective sessions");
                await c.StopObjectiveRefinementSessionAsync(objectiveSession.Session.Id);
                await c.DeleteObjectiveRefinementSessionAsync(objectiveSession.Session.Id);

                await c.DeleteObjectiveAsync(first.Id);
                await c.DeleteBacklogItemAsync(second.Id);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: Ask, planning, backlog (live server, stub captain)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<AskActionProposal> ProposeAsync(ArmadaClient c, string threadId)
        {
            List<string> before = ((await c.GetAskThreadAsync(threadId))?.PendingProposals ?? new List<AskActionProposal>()).Select(p => p.Id).ToList();
            // A turn may still be running (the follow-up after a decision); the server refuses a second one with 409.
            bool sent = await LiveServerSetup.WaitUntilAsync(async () =>
            {
                try
                {
                    await c.SendAskMessageAsync(threadId, "Please PROPOSE-DISPATCH now");
                    return true;
                }
                catch (ArmadaApiException ex) when (ex.StatusCode == 409)
                {
                    return false;
                }
            }, LiveTimeoutMs);
            if (!sent) throw new AssertionException("the thread stayed busy");
            AskActionProposal? found = null;
            bool ok = await LiveServerSetup.WaitUntilAsync(async () =>
            {
                found = ((await c.GetAskThreadAsync(threadId))?.PendingProposals ?? new List<AskActionProposal>()).FirstOrDefault(p => !before.Contains(p.Id));
                return found != null;
            }, LiveTimeoutMs);
            if (!ok) throw new AssertionException("the captain did not propose");
            return found!;
        }

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body, fx => StubCaptainRuntime.Install(fx.Server, _Behavior));
        }

        #endregion
    }
}
