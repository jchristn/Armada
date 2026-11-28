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
                MissionDispatchResult? result = await c.Missions.DispatchAsync(request);
                AssertStartsWith("msn_", result?.Mission?.Id, "mission id read from the wrapped reply");
                AssertEqual("Contract pending mission", result!.Mission!.Title, "title");
                AssertEqual(MissionStatusEnum.Pending, result.Mission.Status, "pending");
                AssertFalse(String.IsNullOrEmpty(result.Warning), "warning kept");

                Mission created = (await c.Missions.CreateAsync(new Mission("Contract created mission", "Also pending.") { VesselId = setup.Vessel.Id }))!;
                AssertStartsWith("msn_", created.Id, "CreateMissionAsync unwraps the reply");
                created.Description = "updated";
                AssertEqual("updated", (await c.Missions.UpdateAsync(created.Id, created))?.Description, "update mission");
                AssertEqual(MissionStatusEnum.Cancelled, (await c.Missions.TransitionAsync(created.Id, new TransitionRequest { Status = "Cancelled" }))?.Status, "transition");
                await c.Missions.DeleteAsync(result.Mission.Id);
                await c.Missions.PurgeAsync(created.Id);
                await ClientContract.ExpectErrorAsync(() => c.Missions.GetAsync(created.Id), "purged", 404, 404);
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
                MissionDispatchResult? result = await c.Missions.DispatchAsync(request);
                AssertTrue(String.IsNullOrEmpty(result?.Warning), "assigned at once, no warning");
                Mission done = await WaitForStatusAsync(c, result!.Mission!.Id, MissionStatusEnum.Complete);
                AssertTrue(LiveServerSetup.OriginHasStubCommit(setup.BarePath), "landed on the origin");

                AssertContains("stub-captain-", (await c.Missions.GetDiffAsync(done.Id))?.Diff ?? "", "diff");
                AssertContains("[stub captain]", (await c.Missions.GetLogAsync(done.Id, 100))?.Log ?? "", "log");
                AssertNotNull(await c.Missions.GetInstructionsAsync(done.Id), "instructions");
                AssertEqual(setup.Vessel.Id, (await c.Missions.GetLandingPreviewAsync(done.Id))?.VesselId, "landing preview");
                await ClientContract.ExpectErrorAsync(() => c.Missions.GetGitHubPullRequestAsync(done.Id), "no pull request for a local merge");
                await ClientContract.ExpectErrorAsync(() => c.Missions.EnumeratePullRequestReviewsAsync(done.Id, new EnumerationQuery { PageSize = 5 }), "no pull request reviews for a local merge");
                await ClientContract.ExpectErrorAsync(() => c.Missions.EnumeratePullRequestCommentsAsync(done.Id), "no pull request comments for a local merge");
                await ClientContract.ExpectErrorAsync(() => c.Missions.EnumeratePullRequestChecksAsync(done.Id), "no pull request checks for a local merge");
                await ClientContract.ExpectErrorAsync(() => c.Missions.RetryLandingAsync(done.Id), "a landed mission cannot retry landing", 400, 409);

                Mission review = (await c.Missions.CreateAsync(new Mission("Contract review approve", "Needs review.") { VesselId = setup.Vessel.Id, RequiresReview = true }))!;
                await WaitForStatusAsync(c, review.Id, MissionStatusEnum.Review);
                AssertNotEqual(MissionStatusEnum.Review, (await c.Missions.ApproveReviewAsync(review.Id, new MissionReviewApproveRequest { Comment = "ok" }))?.Status, "approved");
                await WaitForStatusAsync(c, review.Id, MissionStatusEnum.Complete);

                Mission denied = (await c.Missions.CreateAsync(new Mission("Contract review deny", "Needs review.") { VesselId = setup.Vessel.Id, RequiresReview = true }))!;
                await WaitForStatusAsync(c, denied.Id, MissionStatusEnum.Review);
                AssertNotEqual(MissionStatusEnum.Review, (await c.Missions.DenyReviewAsync(denied.Id, new MissionReviewDenyRequest { Comment = "no", Action = "FailPipeline" }))?.Status, "denied");

                _Behavior.MissionsSucceed = false;
                Mission failing = (await c.Missions.CreateAsync(new Mission("Contract failing mission", "Fails first.") { VesselId = setup.Vessel.Id }))!;
                await WaitForStatusAsync(c, failing.Id, MissionStatusEnum.Failed);
                _Behavior.MissionsSucceed = true;
                Mission? restarted = await c.Missions.RestartAsync(failing.Id);
                AssertEqual(failing.Id, restarted?.Id, "restart keeps the mission");
                AssertNotEqual(MissionStatusEnum.Failed, restarted!.Status, "restart re-queues the mission (dispatched on the next cycle)");
            }));

            cases.Add(Case("local_merge_vs_merge_and_push", "LocalMerge merges into the checkout without pushing; MergeAndPush merges and pushes to the origin", async (c, fx) =>
            {
                await LiveServerSetup.CreateCaptainAsync(c, "contract-lander");

                VesselSetup local = await LiveServerSetup.CreateVesselAsync(c, "localmerge", LandingModeEnum.LocalMerge);
                Mission localMission = (await c.Missions.CreateAsync(new Mission("Contract LocalMerge mission", "Add a file.") { VesselId = local.Vessel.Id }))!;
                await WaitForStatusAsync(c, localMission.Id, MissionStatusEnum.Complete);
                AssertTrue(LiveServerSetup.CheckoutHasStubCommit(local.WorkingDirectory), "LocalMerge: merged into the working checkout");
                AssertFalse(LiveServerSetup.OriginHasStubCommit(local.BarePath), "LocalMerge: nothing pushed to the origin");
                AssertEqual(0, LiveServerSetup.Git(local.WorkingDirectory, out string ahead, "rev-list", "--count", "origin/main..main"), "rev-list: " + ahead);
                AssertTrue(Int32.TryParse(ahead.Trim(), out int aheadCount) && aheadCount > 0, "LocalMerge: the checkout's main is ahead of origin/main, not pushed (" + ahead.Trim() + ")");

                VesselSetup pushed = await LiveServerSetup.CreateVesselAsync(c, "mergeandpush", LandingModeEnum.MergeAndPush);
                Mission pushedMission = (await c.Missions.CreateAsync(new Mission("Contract MergeAndPush mission", "Add a file.") { VesselId = pushed.Vessel.Id }))!;
                await WaitForStatusAsync(c, pushedMission.Id, MissionStatusEnum.Complete);
                AssertTrue(LiveServerSetup.CheckoutHasStubCommit(pushed.WorkingDirectory), "MergeAndPush: merged into the working checkout");
                AssertTrue(LiveServerSetup.OriginHasStubCommit(pushed.BarePath), "MergeAndPush: pushed to the origin");
                AssertEqual(0, LiveServerSetup.Git(null, out string originMain, "--git-dir", pushed.BarePath, "rev-parse", "main"), "origin rev-parse: " + originMain);
                AssertEqual(0, LiveServerSetup.Git(pushed.WorkingDirectory, out string checkoutMain, "rev-parse", "main"), "checkout rev-parse: " + checkoutMain);
                AssertEqual(checkoutMain.Trim(), originMain.Trim(), "MergeAndPush: the origin's main is the checkout's main");
            }));

            cases.Add(Case("voyage_landing_mode_overrides_vessel", "A voyage created with LandingMode LocalMerge lands its missions without pushing, over the vessel's MergeAndPush", async (c, fx) =>
            {
                await LiveServerSetup.CreateCaptainAsync(c, "contract-voyage-lander");
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "voyagelanding", LandingModeEnum.MergeAndPush);
                VoyageCreateRequest create = new VoyageCreateRequest();
                create.Title = "Contract LocalMerge voyage";
                create.VesselId = setup.Vessel.Id;
                create.LandingMode = LandingModeEnum.LocalMerge;
                create.Missions.Add(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Voyage LocalMerge mission", Description = "Add a file." });
                Voyage voyage = (await c.Voyages.CreateAsync(create))!;
                AssertEqual(LandingModeEnum.LocalMerge, voyage.LandingMode, "the create reply carries the voyage landing mode");
                AssertEqual(LandingModeEnum.LocalMerge, (await c.Voyages.GetAsync(voyage.Id))?.LandingMode, "persisted");
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => (await VoyageMissionsAsync(c, voyage.Id)).Any(m => m.Status == MissionStatusEnum.Complete), LiveTimeoutMs), "voyage mission landed");
                AssertTrue(LiveServerSetup.CheckoutHasStubCommit(setup.WorkingDirectory), "merged into the working checkout");
                AssertFalse(LiveServerSetup.OriginHasStubCommit(setup.BarePath), "the voyage's LocalMerge did not push");

                VoyageCreateRequest bare = new VoyageCreateRequest();
                bare.Title = "Contract bare voyage with a landing mode";
                bare.VesselId = setup.Vessel.Id;
                bare.LandingMode = LandingModeEnum.PullRequest;
                Voyage bareVoyage = (await c.Voyages.CreateAsync(bare))!;
                AssertEqual(LandingModeEnum.PullRequest, (await c.Voyages.GetAsync(bareVoyage.Id))?.LandingMode, "a bare voyage keeps its landing mode");

                VoyageCreateRequest inherit = new VoyageCreateRequest();
                inherit.Title = "Contract bare voyage inheriting";
                inherit.VesselId = setup.Vessel.Id;
                Voyage inheritVoyage = (await c.Voyages.CreateAsync(inherit))!;
                AssertNull((await c.Voyages.GetAsync(inheritVoyage.Id))?.LandingMode, "no landing mode means inherit");
            }));

            cases.Add(Case("voyage_evaluation_flag", "Voyage and mission dispatch carry IsEvaluation; missions inherit the voyage flag; the default stays false", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "voyageevaluation");
                VoyageCreateRequest evaluation = new VoyageCreateRequest();
                evaluation.Title = "Contract evaluation voyage";
                evaluation.VesselId = setup.Vessel.Id;
                evaluation.IsEvaluation = true;
                evaluation.Missions.Add(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Evaluation mission", Description = "Add a file." });
                Voyage evalVoyage = (await c.Voyages.CreateAsync(evaluation))!;
                AssertTrue(evalVoyage.IsEvaluation, "the create reply carries the evaluation flag");
                VoyageProgress? evalDetail = await c.Voyages.GetDetailAsync(evalVoyage.Id);
                AssertTrue(evalDetail?.Voyage?.IsEvaluation == true, "persisted voyage flagged");
                List<Mission> evalMissions = await VoyageMissionsAsync(c, evalVoyage.Id);
                AssertTrue(evalMissions.Count > 0 && evalMissions.All(m => m.IsEvaluation), "every voyage mission inherits the flag");

                VoyageCreateRequest bareEval = new VoyageCreateRequest();
                bareEval.Title = "Contract bare evaluation voyage";
                bareEval.VesselId = setup.Vessel.Id;
                bareEval.IsEvaluation = true;
                Voyage bareVoyage = (await c.Voyages.CreateAsync(bareEval))!;
                AssertTrue((await c.Voyages.GetAsync(bareVoyage.Id))?.IsEvaluation == true, "a bare voyage keeps the flag");

                MissionDispatchResult? standalone = await c.Missions.DispatchAsync(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Standalone evaluation mission", Description = "x", IsEvaluation = true });
                AssertTrue(standalone?.Mission?.IsEvaluation == true, "a standalone mission keeps the flag");

                VoyageCreateRequest production = new VoyageCreateRequest();
                production.Title = "Contract production voyage";
                production.VesselId = setup.Vessel.Id;
                production.Missions.Add(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Production mission", Description = "Add a file." });
                Voyage prodVoyage = (await c.Voyages.CreateAsync(production))!;
                VoyageProgress? prodDetail = await c.Voyages.GetDetailAsync(prodVoyage.Id);
                AssertFalse(prodDetail!.Voyage!.IsEvaluation, "a normal dispatch is not evaluation");
                List<Mission> prodMissions = await VoyageMissionsAsync(c, prodVoyage.Id);
                AssertTrue(prodMissions.Count > 0 && prodMissions.All(m => !m.IsEvaluation), "normal missions are not evaluation");
            }));

            cases.Add(Case("voyages", "Voyage create, get, detail, status; cancel and purge", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "voyages");
                VoyageCreateRequest create = new VoyageCreateRequest();
                create.Title = "Contract voyage";
                create.VesselId = setup.Vessel.Id;
                create.Missions.Add(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Voyage mission", Description = "Add a file." });
                Voyage voyage = (await c.Voyages.CreateAsync(create))!;
                AssertStartsWith("vyg_", voyage.Id, "voyage id");
                AssertEqual("Contract voyage", (await c.Voyages.GetAsync(voyage.Id))?.Title, "GetVoyageAsync unwraps the voyage detail");
                VoyageProgress? progress = await c.Voyages.GetDetailAsync(voyage.Id);
                AssertEqual(1, progress?.TotalMissions, "the detail counts the voyage's missions");
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => (await VoyageMissionsAsync(c, voyage.Id)).Any(m => m.Status == MissionStatusEnum.Complete), LiveTimeoutMs), "voyage mission landed");
                ArmadaRawJson? statusJson = await c.Voyages.GetStatusAsync(voyage.Id);
                AssertNotNull(statusJson, "status JSON");
                VoyageProgress statusDetail = JsonHelper.Deserialize<VoyageProgress>(statusJson!.Json);
                AssertEqual(voyage.Id, statusDetail.Voyage?.Id, "status JSON voyage");
                AssertTrue(statusDetail.TotalMissions > 0, "status JSON counts the missions");
                AssertFalse(statusJson.Json.Contains("\"Missions\"", StringComparison.Ordinal), "status JSON embeds no mission list");
                EnumerationResult<VoyageProgress>? active = await c.Voyages.EnumerateProgressAsync(new EnumerationQuery { PageSize = 1000 });
                AssertNotNull(active, "voyage progress page");

                _Behavior.MissionsSucceed = false;
                VoyageCreateRequest second = new VoyageCreateRequest();
                second.Title = "Contract voyage to cancel";
                second.VesselId = setup.Vessel.Id;
                second.Missions.Add(new DispatchRequest { VesselId = setup.Vessel.Id, Title = "Cancelled mission", Description = "x" });
                Voyage toCancel = (await c.Voyages.CreateAsync(second))!;
                VoyageCancelResult? cancelResult = await c.Voyages.CancelAsync(toCancel.Id);
                AssertEqual(VoyageStatusEnum.Cancelled, cancelResult?.Voyage?.Status, "cancel returns the cancelled voyage");
                AssertTrue(cancelResult!.CancelledMissions + cancelResult.NotCancelledMissions <= 1, "cancel returns counts");
                _Behavior.MissionsSucceed = true;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    VoyageProgress? detail = await c.Voyages.GetDetailAsync(toCancel.Id);
                    return detail?.Voyage != null && detail.Voyage.Status != VoyageStatusEnum.InProgress
                        && (await VoyageMissionsAsync(c, toCancel.Id)).All(m => m.Status != MissionStatusEnum.Assigned && m.Status != MissionStatusEnum.InProgress);
                }, LiveTimeoutMs), "voyage and its missions stopped");
                await c.Voyages.PurgeAsync(toCancel.Id);
                await ClientContract.ExpectErrorAsync(() => c.Voyages.GetAsync(toCancel.Id), "purged", 404, 404);
            }));

            cases.Add(Case("captains", "Captain create, get, update, tools, log, stop, recall, unquarantine, restart, stop all, delete", async (c, fx) =>
            {
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-captain");
                AssertEqual(captain.Name, (await c.Captains.GetAsync(captain.Id))?.Name, "get captain");
                captain.SystemInstructions = "Be brief.";
                AssertEqual("Be brief.", (await c.Captains.UpdateAsync(captain.Id, captain))?.SystemInstructions, "update captain");
                AssertEqual(captain.Id, (await c.Captains.GetToolsAsync(captain.Id))?.CaptainId, "tools");
                CaptainToolAccessResult? toolPage = await c.Captains.EnumerateToolsAsync(captain.Id, 2, 1, CaptainToolSourceKindEnum.McpServer);
                AssertNotNull(toolPage, "paged tools");
                AssertEqual(2, toolPage!.Tools.PageNumber, "tools page number");
                AssertEqual(1, toolPage.Tools.PageSize, "tools page size");
                AssertTrue(toolPage.Tools.Objects.All(t => t.SourceKind == CaptainToolSourceKindEnum.McpServer), "source kind filter");
                try
                {
                    AssertNotNull(await c.Captains.GetLogAsync(captain.Id, 50), "log");
                }
                catch (ArmadaApiException ex)
                {
                    AssertEqual(404, ex.StatusCode, "an idle captain without a log reads 404");
                }

                await c.Captains.StopAsync(captain.Id);
                await c.Captains.RecallAsync(captain.Id);
                AssertEqual(captain.Id, (await c.Captains.UnquarantineAsync(captain.Id))?.Id, "unquarantine of a captain that is not quarantined returns that captain");

                Captain replacement = (await c.Captains.RestartAsync(captain.Id))!;
                AssertEqual(captain.Name, replacement.Name, "restart recreates the captain");
                AssertNotEqual(captain.Id, replacement.Id, "with a new id");
                await c.Captains.StopAllAsync();
                await c.Captains.DeleteAsync(replacement.Id);
            }));

            cases.Add(Case("docks_and_merge_queue", "Docks list, get, delete; merge queue enqueue, get, process, process all, cancel, delete", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "merge");
                EnumerationResult<Dock>? docks = await c.Docks.ListAsync(new ArmadaPageQuery(1, 100));
                Dock? dock = docks?.Objects.FirstOrDefault();
                AssertNotNull(dock, "earlier missions left docks");
                AssertEqual(dock!.Id, (await c.Docks.GetAsync(dock.Id))?.Id, "get dock");
                try
                {
                    await c.Docks.DeleteAsync(dock.Id);
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

                MergeEntry entry = (await c.MergeQueue.EnqueueAsync(new MergeEntry(branch, "main") { VesselId = setup.Vessel.Id }))!;
                AssertEqual(branch, (await c.MergeQueue.GetAsync(entry.Id))?.BranchName, "get entry");
                await c.MergeQueue.ProcessAsync(entry.Id);
                await c.MergeQueue.ProcessAllAsync();
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => (await c.MergeQueue.GetAsync(entry.Id))?.Status != MergeStatusEnum.Queued, LiveTimeoutMs), "entry processed");

                MergeEntry second = (await c.MergeQueue.EnqueueAsync(new MergeEntry(branch, "main") { VesselId = setup.Vessel.Id }))!;
                await c.MergeQueue.CancelAsync(second.Id);
                MergeEntry third = (await c.MergeQueue.EnqueueAsync(new MergeEntry(branch, "main") { VesselId = setup.Vessel.Id }))!;
                await c.MergeQueue.DeleteAsync(third.Id);
                AssertNotNull(await c.MergeQueue.ListAsync(new ArmadaPageQuery(1, 50)), "list");
            }));

            cases.Add(Case("signals_events_jobs_history", "Signals send, get, read, batch delete; event get and batch delete; job get and cancel; history; request history get and deletes", async (c, fx) =>
            {
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-signals");
                Signal sent = (await c.Signals.SendAsync(new SendSignalRequest { ToCaptainId = captain.Id, Type = "Nudge", Payload = "contract" }))!;
                AssertEqual("contract", (await c.Signals.GetAsync(sent.Id))?.Payload, "get signal");
                await c.Signals.MarkReadAsync(sent.Id);
                AssertTrue((await c.Signals.GetAsync(sent.Id))?.Read == true, "read");
                AssertEqual(1, (await c.Signals.DeleteBatchAsync(new List<string> { sent.Id }))?.Deleted ?? 0, "batch delete signals");

                ArmadaEvent evt = (await c.Events.ListAsync(new ArmadaPageQuery(1, 10)))!.Objects.First();
                AssertEqual(evt.Id, (await c.Events.GetAsync(evt.Id))?.Id, "GET /api/v1/events/{id}");
                AssertEqual(1, (await c.Events.DeleteBatchAsync(new List<string> { evt.Id }))?.Deleted ?? 0, "batch delete events");
                await ClientContract.ExpectErrorAsync(() => c.Events.GetAsync(evt.Id), "deleted event", 404, 404);

                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "jobs");
                VesselHealthEvaluationStart? start = await c.VesselHealth.EvaluateAsync(new VesselHealthEvaluateRequest { VesselIds = new List<string> { setup.Vessel.Id } });
                AssertFalse(String.IsNullOrEmpty(start?.JobId), "evaluation job");
                AssertEqual(start!.JobId, (await c.Jobs.GetAsync(start.JobId))?.Id, "get job");
                try
                {
                    AssertEqual(start.JobId, (await c.Jobs.CancelAsync(start.JobId))?.Id, "cancel job");
                }
                catch (ArmadaApiException ex)
                {
                    AssertEqual(409, ex.StatusCode, "a finished job cannot be cancelled");
                }

                AssertNotNull(await c.History.ListTimelineAsync(new HistoricalTimelineQuery { PageSize = 5 }), "history (GET)");

                await ClientContract.ExpectErrorAsync(() => c.Missions.GetAsync("msn_contract_missing_a"), "404 to record", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.Missions.GetAsync("msn_contract_missing_b"), "404 to record", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.Missions.GetAsync("msn_contract_missing_c"), "404 to record", 404, 404);
                List<RequestHistoryEntry> missing = new List<RequestHistoryEntry>();
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    missing = (await c.RequestHistory.ListAsync(new RequestHistoryQuery { StatusCode = 404, PageSize = 100 }))?.Objects.Where(e => e.Route.Contains("msn_contract_missing_", StringComparison.Ordinal)).ToList() ?? new List<RequestHistoryEntry>();
                    return missing.Count >= 3;
                }, 15000), "requests recorded");
                RequestHistoryEntry first = missing.First(e => e.Route.EndsWith("_a", StringComparison.Ordinal));
                AssertEqual(first.Id, (await c.RequestHistory.GetEntryAsync(first.Id))?.Entry.Id, "get request entry");
                await c.RequestHistory.DeleteEntryAsync(first.Id);
                RequestHistoryEntry second = missing.First(e => e.Route.EndsWith("_b", StringComparison.Ordinal));
                AssertEqual(1, (await c.RequestHistory.DeleteEntriesAsync(new List<string> { second.Id }))?.Deleted ?? 0, "delete entries");
                BatchDeleteResult? byFilter = await c.RequestHistory.DeleteByFilterAsync(new RequestHistoryQuery { Route = "/api/v1/missions/msn_contract_missing_c" });
                AssertTrue((byFilter?.Deleted ?? 0) >= 1, "delete by filter");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: missions, voyages, captains, queue, activity (live server, stub captain)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<List<Mission>> VoyageMissionsAsync(ArmadaClient c, string voyageId)
        {
            // The voyage detail no longer embeds missions: read them through bounded pages of the missions list.
            List<Mission> missions = new List<Mission>();
            for (int pageNumber = 1; pageNumber <= 100; pageNumber++)
            {
                EnumerationResult<Mission>? page = await c.Missions.ListAsync(new ArmadaPageQuery(pageNumber, 100).With("voyageId", voyageId)).ConfigureAwait(false);
                if (page == null || page.Objects.Count == 0) break;
                missions.AddRange(page.Objects);
                if (pageNumber >= page.TotalPages) break;
            }

            return missions;
        }

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body, fx => StubCaptainRuntime.Install(fx.Server, _Behavior));
        }

        private async Task<Mission> WaitForStatusAsync(ArmadaClient c, string missionId, MissionStatusEnum status)
        {
            Mission? mission = null;
            bool reached = await LiveServerSetup.WaitUntilAsync(async () =>
            {
                mission = await c.Missions.GetAsync(missionId);
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
