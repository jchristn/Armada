namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// mission dispatch (including the wrapped <c>{ Mission, Warning }</c> reply), mission detail, review, restart and
    /// landing calls, voyages, captains, docks, the merge queue, signals, events, jobs, history, and request history.
    /// </summary>
    public sealed class ClientContractWorkSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Work";
        private const int LiveTimeoutMs = 60000;
        private readonly StubCaptainBehavior _Behavior = new StubCaptainBehavior();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // Runs first, before any captain exists, so the server cannot assign the mission and wraps its reply.
            cases.Add(Case("dispatch_warning_and_edits", "Dispatch with no captain returns the mission and the warning; update, transition, delete, purge", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "warning");
                DispatchRequest request = new DispatchRequest();
                request.VesselId = setup.Vessel.Id;
                request.Title = "Contract pending mission";
                request.Description = "No captain exists yet.";
                MissionDispatchResult? result = await c.DispatchMissionAsync(request);
                AssertStartsWith("msn_", result?.Mission?.Id, "mission id read from the wrapped reply");
                AssertEqual("Contract pending mission", result!.Mission!.Title, "title");
                AssertEqual(MissionStatusEnum.Pending, result.Mission.Status, "pending");
                AssertFalse(String.IsNullOrEmpty(result.Warning), "warning kept");

                Mission created = (await c.CreateMissionAsync(new Mission("Contract created mission", "Also pending.") { VesselId = setup.Vessel.Id }))!;
                AssertStartsWith("msn_", created.Id, "CreateMissionAsync unwraps the reply");
                created.Description = "updated";
                AssertEqual("updated", (await c.UpdateMissionAsync(created.Id, created))?.Description, "update mission");
                AssertEqual(MissionStatusEnum.Cancelled, (await c.TransitionMissionAsync(created.Id, new TransitionRequest { Status = "Cancelled" }))?.Status, "transition");
                await c.DeleteMissionAsync(result.Mission.Id);
                await c.PurgeMissionAsync(created.Id);
                await ClientContract.ExpectErrorAsync(() => c.GetMissionAsync(created.Id), "purged", 404, 404);
            }));

            cases.Add(Case("missions_land_and_detail", "A dispatched mission lands; diff, log, instructions, landing preview, PR, retry landing; review approve and deny; restart a failed mission", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "missions");
                await LiveServerSetup.CreateCaptainAsync(c, "contract-worker-1");
                await LiveServerSetup.CreateCaptainAsync(c, "contract-worker-2");
                DispatchRequest request = new DispatchRequest();
                request.VesselId = setup.Vessel.Id;
                request.Title = "Contract landing mission";
                request.Description = "Add a file.";
                MissionDispatchResult? result = await c.DispatchMissionAsync(request);
                AssertTrue(String.IsNullOrEmpty(result?.Warning), "assigned at once, no warning");
                Mission done = await WaitForStatusAsync(c, result!.Mission!.Id, MissionStatusEnum.Complete);
                AssertTrue(LiveServerSetup.OriginHasStubCommit(setup.BarePath), "landed on the origin");

                AssertContains("stub-captain-", (await c.GetMissionDiffAsync(done.Id))?.Diff ?? "", "diff");
                AssertContains("[stub captain]", (await c.GetMissionLogAsync(done.Id, 100))?.Log ?? "", "log");
                AssertNotNull(await c.GetMissionInstructionsAsync(done.Id), "instructions");
                AssertEqual(setup.Vessel.Id, (await c.GetMissionLandingPreviewAsync(done.Id))?.VesselId, "landing preview");
                await ClientContract.ExpectErrorAsync(() => c.GetMissionGitHubPullRequestAsync(done.Id), "no pull request for a local merge");
                await ClientContract.ExpectErrorAsync(() => c.RetryMissionLandingAsync(done.Id), "a landed mission cannot retry landing", 400, 409);

                Mission review = (await c.CreateMissionAsync(new Mission("Contract review approve", "Needs review.") { VesselId = setup.Vessel.Id, RequiresReview = true }))!;
                await WaitForStatusAsync(c, review.Id, MissionStatusEnum.Review);
                AssertNotEqual(MissionStatusEnum.Review, (await c.ApproveMissionReviewAsync(review.Id, new MissionReviewApproveRequest { Comment = "ok" }))?.Status, "approved");
                await WaitForStatusAsync(c, review.Id, MissionStatusEnum.Complete);

                Mission denied = (await c.CreateMissionAsync(new Mission("Contract review deny", "Needs review.") { VesselId = setup.Vessel.Id, RequiresReview = true }))!;
                await WaitForStatusAsync(c, denied.Id, MissionStatusEnum.Review);
                AssertNotEqual(MissionStatusEnum.Review, (await c.DenyMissionReviewAsync(denied.Id, new MissionReviewDenyRequest { Comment = "no", Action = "FailPipeline" }))?.Status, "denied");

                _Behavior.MissionsSucceed = false;
                Mission failing = (await c.CreateMissionAsync(new Mission("Contract failing mission", "Fails first.") { VesselId = setup.Vessel.Id }))!;
                await WaitForStatusAsync(c, failing.Id, MissionStatusEnum.Failed);
                _Behavior.MissionsSucceed = true;
                Mission? restarted = await c.RestartMissionAsync(failing.Id);
                AssertEqual(failing.Id, restarted?.Id, "restart keeps the mission");
                AssertNotEqual(MissionStatusEnum.Failed, restarted!.Status, "restart re-queues the mission (dispatched on the next cycle)");
            }));

            cases.Add(Case("voyages", "Voyage create, get, detail, status; cancel and purge", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "voyages");
                VoyageCreateRequest create = new VoyageCreateRequest();
                create.Title = "Contract voyage";
                create.VesselId = setup.Vessel.Id;
                create.Missions.Add(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Voyage mission", Description = "Add a file." });
                Voyage voyage = (await c.CreateVoyageAsync(create))!;
                AssertStartsWith("vyg_", voyage.Id, "voyage id");
                AssertEqual("Contract voyage", (await c.GetVoyageAsync(voyage.Id))?.Title, "GetVoyageAsync unwraps { Voyage, Missions }");
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => ((await c.GetVoyageDetailAsync(voyage.Id))?.Missions ?? new List<Mission>()).Any(m => m.Status == MissionStatusEnum.Complete), LiveTimeoutMs), "voyage mission landed");
                ArmadaRawJson? statusJson = await c.GetVoyageStatusAsync(voyage.Id);
                AssertNotNull(statusJson, "status JSON");
                VoyageDetail statusDetail = JsonHelper.Deserialize<VoyageDetail>(statusJson!.Json);
                AssertEqual(voyage.Id, statusDetail.Voyage?.Id, "status JSON voyage");
                AssertTrue(statusDetail.Missions != null && statusDetail.Missions.Count > 0, "status JSON lists the missions");

                _Behavior.MissionsSucceed = false;
                VoyageCreateRequest second = new VoyageCreateRequest();
                second.Title = "Contract voyage to cancel";
                second.VesselId = setup.Vessel.Id;
                second.Missions.Add(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Cancelled mission", Description = "x" });
                Voyage toCancel = (await c.CreateVoyageAsync(second))!;
                await c.CancelVoyageAsync(toCancel.Id);
                _Behavior.MissionsSucceed = true;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    VoyageDetail? detail = await c.GetVoyageDetailAsync(toCancel.Id);
                    return detail?.Voyage != null && detail.Voyage.Status != VoyageStatusEnum.InProgress
                        && detail.Missions.All(m => m.Status != MissionStatusEnum.Assigned && m.Status != MissionStatusEnum.InProgress);
                }, LiveTimeoutMs), "voyage and its missions stopped");
                await c.PurgeVoyageAsync(toCancel.Id);
                await ClientContract.ExpectErrorAsync(() => c.GetVoyageAsync(toCancel.Id), "purged", 404, 404);
            }));

            cases.Add(Case("captains", "Captain create, get, update, tools, log, stop, recall, unquarantine, restart, stop all, delete", async (c, fx) =>
            {
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-captain");
                AssertEqual(captain.Name, (await c.GetCaptainAsync(captain.Id))?.Name, "get captain");
                captain.SystemInstructions = "Be brief.";
                AssertEqual("Be brief.", (await c.UpdateCaptainAsync(captain.Id, captain))?.SystemInstructions, "update captain");
                AssertEqual(captain.Id, (await c.GetCaptainToolsAsync(captain.Id))?.CaptainId, "tools");
                try
                {
                    AssertNotNull(await c.GetCaptainLogAsync(captain.Id, 50), "log");
                }
                catch (ArmadaApiException ex)
                {
                    AssertEqual(404, ex.StatusCode, "an idle captain without a log reads 404");
                }

                await c.StopCaptainAsync(captain.Id);
                await c.RecallCaptainAsync(captain.Id);
                AssertEqual(captain.Id, (await c.UnquarantineCaptainAsync(captain.Id))?.Id, "unquarantine of a captain that is not quarantined returns that captain");

                Captain replacement = (await c.RestartCaptainAsync(captain.Id))!;
                AssertEqual(captain.Name, replacement.Name, "restart recreates the captain");
                AssertNotEqual(captain.Id, replacement.Id, "with a new id");
                await c.StopAllCaptainsAsync();
                await c.DeleteCaptainAsync(replacement.Id);
            }));

            cases.Add(Case("docks_and_merge_queue", "Docks list, get, delete; merge queue enqueue, get, process, process all, cancel, delete", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "merge");
                EnumerationResult<Dock>? docks = await c.ListDocksAsync(new ArmadaPageQuery(1, 100));
                Dock? dock = docks?.Objects.FirstOrDefault();
                AssertNotNull(dock, "earlier missions left docks");
                AssertEqual(dock!.Id, (await c.GetDockAsync(dock.Id))?.Id, "get dock");
                try
                {
                    await c.DeleteDockAsync(dock.Id);
                }
                catch (ArmadaApiException ex)
                {
                    AssertEqual(409, ex.StatusCode, "a dock in use is refused with 409");
                }

                string branch = "contract/merge-" + ClientContract.Suffix();
                string work = setup.Vessel.WorkingDirectory!;
                AssertEqual(0, LiveServerSetup.Git(work, out string o1, "checkout", "-b", branch), "branch: " + o1);
                System.IO.File.WriteAllText(System.IO.Path.Combine(work, "merge-queue.txt"), "queued\n");
                LiveServerSetup.Git(work, out _, "add", "-A");
                AssertEqual(0, LiveServerSetup.Git(work, out string o2, "-c", "user.name=Contract", "-c", "user.email=contract@armada.test", "commit", "-m", "Merge queue change"), "commit: " + o2);
                AssertEqual(0, LiveServerSetup.Git(work, out string o3, "push", "origin", branch), "push: " + o3);
                LiveServerSetup.Git(work, out _, "checkout", "main");

                MergeEntry entry = (await c.EnqueueMergeAsync(new MergeEntry(branch, "main") { VesselId = setup.Vessel.Id }))!;
                AssertEqual(branch, (await c.GetMergeEntryAsync(entry.Id))?.BranchName, "get entry");
                await c.ProcessMergeEntryAsync(entry.Id);
                await c.ProcessAllMergeQueueAsync();
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => (await c.GetMergeEntryAsync(entry.Id))?.Status != MergeStatusEnum.Queued, LiveTimeoutMs), "entry processed");

                MergeEntry second = (await c.EnqueueMergeAsync(new MergeEntry(branch, "main") { VesselId = setup.Vessel.Id }))!;
                await c.CancelMergeEntryAsync(second.Id);
                MergeEntry third = (await c.EnqueueMergeAsync(new MergeEntry(branch, "main") { VesselId = setup.Vessel.Id }))!;
                await c.DeleteMergeEntryAsync(third.Id);
                AssertNotNull(await c.ListMergeQueueAsync(new ArmadaPageQuery(1, 50)), "list");
            }));

            cases.Add(Case("signals_events_jobs_history", "Signals send, get, read, batch delete; event get and batch delete; job get and cancel; history; request history get and deletes", async (c, fx) =>
            {
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-signals");
                Signal sent = (await c.SendSignalAsync(new SendSignalRequest { ToCaptainId = captain.Id, Type = "Nudge", Payload = "contract" }))!;
                AssertEqual("contract", (await c.GetSignalAsync(sent.Id))?.Payload, "get signal");
                await c.MarkSignalReadAsync(sent.Id);
                AssertTrue((await c.GetSignalAsync(sent.Id))?.Read == true, "read");
                AssertEqual(1, (await c.DeleteSignalsBatchAsync(new List<string> { sent.Id }))?.Deleted ?? 0, "batch delete signals");

                ArmadaEvent evt = (await c.ListEventsAsync(new ArmadaPageQuery(1, 10)))!.Objects.First();
                AssertEqual(evt.Id, (await c.GetEventAsync(evt.Id))?.Id, "GET /api/v1/events/{id}");
                AssertEqual(1, (await c.DeleteEventsBatchAsync(new List<string> { evt.Id }))?.Deleted ?? 0, "batch delete events");
                await ClientContract.ExpectErrorAsync(() => c.GetEventAsync(evt.Id), "deleted event", 404, 404);

                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "jobs");
                VesselHealthEvaluationStart? start = await c.EvaluateVesselHealthAsync(new VesselHealthEvaluateRequest { VesselIds = new List<string> { setup.Vessel.Id } });
                AssertFalse(String.IsNullOrEmpty(start?.JobId), "evaluation job");
                AssertEqual(start!.JobId, (await c.GetJobAsync(start.JobId))?.Id, "get job");
                try
                {
                    AssertEqual(start.JobId, (await c.CancelJobAsync(start.JobId))?.Id, "cancel job");
                }
                catch (ArmadaApiException ex)
                {
                    AssertEqual(409, ex.StatusCode, "a finished job cannot be cancelled");
                }

                AssertNotNull(await c.ListHistoryTimelineAsync(new HistoricalTimelineQuery { PageSize = 5 }), "history (GET)");

                await ClientContract.ExpectErrorAsync(() => c.GetMissionAsync("msn_contract_missing_a"), "404 to record", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.GetMissionAsync("msn_contract_missing_b"), "404 to record", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.GetMissionAsync("msn_contract_missing_c"), "404 to record", 404, 404);
                List<RequestHistoryEntry> missing = new List<RequestHistoryEntry>();
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    missing = (await c.ListRequestHistoryAsync(new RequestHistoryQuery { StatusCode = 404, PageSize = 100 }))?.Objects.Where(e => e.Route.Contains("msn_contract_missing_", StringComparison.Ordinal)).ToList() ?? new List<RequestHistoryEntry>();
                    return missing.Count >= 3;
                }, 15000), "requests recorded");
                RequestHistoryEntry first = missing.First(e => e.Route.EndsWith("_a", StringComparison.Ordinal));
                AssertEqual(first.Id, (await c.GetRequestHistoryEntryAsync(first.Id))?.Entry.Id, "get request entry");
                await c.DeleteRequestHistoryEntryAsync(first.Id);
                RequestHistoryEntry second = missing.First(e => e.Route.EndsWith("_b", StringComparison.Ordinal));
                AssertEqual(1, (await c.DeleteRequestHistoryEntriesAsync(new List<string> { second.Id }))?.Deleted ?? 0, "delete entries");
                BatchDeleteResult? byFilter = await c.DeleteRequestHistoryByFilterAsync(new RequestHistoryQuery { Route = "/api/v1/missions/msn_contract_missing_c" });
                AssertTrue((byFilter?.Deleted ?? 0) >= 1, "delete by filter");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: missions, voyages, captains, queue, activity (live server, stub captain)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body, fx => StubCaptainRuntime.Install(fx.Server, _Behavior));
        }

        private async Task<Mission> WaitForStatusAsync(ArmadaClient c, string missionId, MissionStatusEnum status)
        {
            Mission? mission = null;
            bool reached = await LiveServerSetup.WaitUntilAsync(async () =>
            {
                mission = await c.GetMissionAsync(missionId);
                return mission != null && mission.Status == status;
            }, LiveTimeoutMs);
            if (!reached)
            {
                string errors = _Behavior.Errors.Count == 0 ? "no stub errors" : String.Join("; ", _Behavior.Errors);
                throw new AssertionException("mission " + missionId + " did not reach " + status + " (now " + (mission?.Status.ToString() ?? "missing") + " " + mission?.FailureReason + "): " + errors);
            }

            return mission!;
        }

        #endregion
    }
}
