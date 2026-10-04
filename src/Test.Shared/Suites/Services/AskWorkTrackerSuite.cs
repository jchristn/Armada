namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Server.Ask;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Ask work tracker: snapshots of voyages (and jobs), hash-based change detection, ask.work events, milestone
    /// detection driven by stubbed entity state changes, deterministic and narrated WorkUpdate messages, unread counts,
    /// and no repeated milestones after a restart.
    /// </summary>
    public sealed class AskWorkTrackerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskWorkTracker";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("voyage_snapshot_and_milestones", "A tracked voyage produces snapshots, ask.work events, and milestone messages through landing", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.NarrateMilestones = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_track");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                Captain captain = await db.Captains.CreateAsync(new Captain("Ada") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Voyage voyage = await db.Voyages.CreateAsync(new Voyage("Cleanup") { TenantId = Constants.DefaultTenantId, Status = VoyageStatusEnum.Open }).ConfigureAwait(false);
                Mission first = await db.Missions.CreateAsync(new Mission("Remove dead code") { TenantId = Constants.DefaultTenantId, VoyageId = voyage.Id }).ConfigureAwait(false);
                Mission second = await db.Missions.CreateAsync(new Mission("Update docs") { TenantId = Constants.DefaultTenantId, VoyageId = voyage.Id }).ConfigureAwait(false);

                AskTrackedWork work = await h.Threads.TrackWorkAsync(thread, new AskWorkLink(AskTrackedEntityTypeEnum.Voyage, voyage.Id, false)).ConfigureAwait(false);
                await h.Tracker.OnWorkLinkedAsync(work).ConfigureAwait(false);
                AskWorkSnapshot initial = (await h.Threads.GetWorkSnapshotAsync(owner, thread.Id, work.Id).ConfigureAwait(false))!;
                AssertEqual("Cleanup", initial.Title);
                AssertEqual(2, initial.TotalCount, "two missions");
                AssertEqual(2, initial.Counts["Pending"], "counts by status");
                AssertEqual(AskTrackedWorkStateEnum.Active, initial.State);
                AssertEqual(1, h.EventsFor("usr_track", "ask.work").Count, "initial ask.work");

                AssertFalse(await h.Tracker.RefreshAsync(work).ConfigureAwait(false), "unchanged snapshot is not re-announced");
                AssertEqual(1, h.EventsFor("usr_track", "ask.work").Count, "still one ask.work");

                // Started: a captain picks up the first mission.
                first.CaptainId = captain.Id;
                first.Status = MissionStatusEnum.InProgress;
                first.BranchName = "armada/fix";
                first = await db.Missions.UpdateAsync(first).ConfigureAwait(false);
                voyage.Status = VoyageStatusEnum.InProgress;
                voyage = await db.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                AssertTrue(await h.Tracker.RefreshAsync(work).ConfigureAwait(false), "change detected");
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);

                // Mission failed with a reason; the second lands.
                first.Status = MissionStatusEnum.Failed;
                first.FailureReason = "Tests failed";
                await db.Missions.UpdateAsync(first).ConfigureAwait(false);
                second.Status = MissionStatusEnum.Complete;
                second.CaptainId = captain.Id;
                await db.Missions.UpdateAsync(second).ConfigureAwait(false);
                await h.Tracker.RefreshAsync(work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);

                // Voyage finished (failed overall).
                voyage.Status = VoyageStatusEnum.Failed;
                voyage.CompletedUtc = DateTime.UtcNow;
                await db.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                await h.Tracker.RefreshAsync(work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                List<string> updates = page.Messages.Where(m => m.Kind == AskMessageKindEnum.WorkUpdate).Select(m => m.ContentText).ToList();
                AssertEqual(4, updates.Count, "started, failed, landed, finished: " + String.Join(" | ", updates));
                AssertEqual("Voyage \"Cleanup\" started: Ada picked up mission \"Remove dead code\".", updates[0]);
                AssertEqual("Mission \"Remove dead code\" failed: Tests failed.", updates[1]);
                AssertEqual("Mission \"Update docs\" landed.", updates[2]);
                AssertContains("Voyage \"Cleanup\" failed (1 of 2 missions done, 1 failed)", updates[3]);
                AssertTrue(page.Messages.Where(m => m.Kind == AskMessageKindEnum.WorkUpdate).All(m => m.TrackedWorkId == work.Id && m.Role == AskMessageRoleEnum.System), "deterministic system messages linked to the work");

                AskThread? after = await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(4, after!.UnreadCount, "milestones count as unread");
                AssertEqual(0, after.ActiveWorkCount, "no active work left");
                AskTrackedWork? row = await db.AskTrackedWork.ReadAsync(Constants.DefaultTenantId, work.Id).ConfigureAwait(false);
                AssertEqual(AskTrackedWorkStateEnum.Failed, row!.State);
                AssertNotNull(row.CompletedUtc, "completed");
                AssertEqual("Failed", row.Status);

                AskWorkSnapshot final = (await h.Threads.GetWorkSnapshotAsync(owner, thread.Id, work.Id).ConfigureAwait(false))!;
                AskWorkMissionSnapshot landed = final.Missions.First(m => m.Id == second.Id);
                AssertEqual("Landed", landed.LandingOutcome);
                AssertEqual("Ada", landed.CaptainName);
                AssertEqual("armada/fix", final.Missions.First(m => m.Id == first.Id).BranchName);
            }));

            cases.Add(CaseAsync("no_repeat_after_restart", "A new tracker instance (server restart) does not repeat earlier milestones", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.NarrateMilestones = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_restart");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                Voyage voyage = await db.Voyages.CreateAsync(new Voyage("Restartable") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Mission mission = await db.Missions.CreateAsync(new Mission("Only mission") { TenantId = Constants.DefaultTenantId, VoyageId = voyage.Id, Status = MissionStatusEnum.InProgress, CaptainId = (await db.Captains.CreateAsync(new Captain("restart-captain") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false)).Id }).ConfigureAwait(false);
                AskTrackedWork work = await h.Threads.TrackWorkAsync(thread, new AskWorkLink(AskTrackedEntityTypeEnum.Voyage, voyage.Id, false)).ConfigureAwait(false);
                await h.Tracker.OnWorkLinkedAsync(work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                int before = await CountUpdatesAsync(h, owner, thread.Id).ConfigureAwait(false);
                AssertEqual(1, before, "started reported once on first snapshot");

                // New tracker (no in-memory history) sees a small change: no milestone is repeated.
                AskWorkTracker restarted = new AskWorkTracker(db, h.Threads, h.Settings, h.Logging);
                mission.BranchName = "armada/x";
                await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                AssertTrue(await restarted.RefreshAsync(work).ConfigureAwait(false), "change detected");
                await restarted.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                AssertEqual(before, await CountUpdatesAsync(h, owner, thread.Id).ConfigureAwait(false), "nothing repeated");

                // A terminal change is still reported after the restart.
                voyage.Status = VoyageStatusEnum.Complete;
                await db.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                mission.Status = MissionStatusEnum.Complete;
                await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                await restarted.RefreshAsync(work).ConfigureAwait(false);
                await restarted.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AssertContains("finished", page.Messages.Last().ContentText);
            }));

            cases.Add(CaseAsync("narration_and_fallback", "Milestones are narrated by an idle captain, and fall back to the deterministic sentence otherwise", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_narr");
                Captain captain = await db.Captains.CreateAsync(new Captain("Narrator") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskWorkSnapshot snapshot = new AskWorkSnapshot { EntityType = AskTrackedEntityTypeEnum.Job, EntityId = "job_1", Title = "Health", Status = "Running" };

                h.Runner.Reply = "Your health check is underway.";
                string? narrated = await h.Turns.NarrateAsync(thread, "Job \"Health\" started.", snapshot).ConfigureAwait(false);
                AssertEqual("Your health check is underway.", narrated, "idle captain narrates");
                AssertContains("Do not call any tools", h.Runner.Calls.Last().Prompt);
                AssertNotNull(h.Runner.Calls.Last().McpSessionToken, "narration uses a thread-scoped (gated) token");

                captain.State = CaptainStateEnum.Working;
                await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                AssertNull(await h.Turns.NarrateAsync(thread, "x", snapshot).ConfigureAwait(false), "a working captain is never borrowed");
                captain.State = CaptainStateEnum.Idle;
                await db.Captains.UpdateAsync(captain).ConfigureAwait(false);

                h.Runner.Error = "model unavailable";
                AssertNull(await h.Turns.NarrateAsync(thread, "x", snapshot).ConfigureAwait(false), "failed narration falls back");
                h.Runner.Error = null;

                h.Settings.Ask.NarrateMilestones = false;
                AssertNull(await h.Turns.NarrateAsync(thread, "x", snapshot).ConfigureAwait(false), "disabled by setting");
                h.Settings.Ask.NarrateMilestones = true;

                // A running turn in the thread blocks narration.
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                AskTurnStart start = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "hi" }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode);
                AssertNull(await h.Turns.NarrateAsync(thread, "x", snapshot).ConfigureAwait(false), "busy thread is not narrated");
                h.Runner.Gate.TrySetResult(true);
                await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Turns.ActiveTurnId(thread.Id) == null)).ConfigureAwait(false);
                h.Runner.Gate = null;

                // End to end: a narrated milestone is posted as the captain's WorkUpdate.
                Job job = await db.Jobs.CreateAsync(new Job { Id = Constants.IdGenerator.GenerateKSortable(Constants.JobIdPrefix, 24), TenantId = Constants.DefaultTenantId, Name = "Health", Status = JobStatusEnum.Queued }).ConfigureAwait(false);
                AskTrackedWork work = await h.Threads.TrackWorkAsync(thread, new AskWorkLink(AskTrackedEntityTypeEnum.Job, job.Id, false)).ConfigureAwait(false);
                await h.Tracker.OnWorkLinkedAsync(work).ConfigureAwait(false);
                job.Status = JobStatusEnum.Succeeded;
                await db.Jobs.UpdateAsync(job).ConfigureAwait(false);
                h.Runner.Reply = "The health evaluation finished.";
                await h.Tracker.RefreshAsync(work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AskMessage update = page.Messages.Last(m => m.Kind == AskMessageKindEnum.WorkUpdate);
                AssertEqual("The health evaluation finished.", update.ContentText);
                AssertEqual(AskMessageRoleEnum.Assistant, update.Role);
                AssertEqual(captain.Id, update.CaptainId);
            }));

            cases.Add(CaseAsync("milestone_detector_rules", "Milestone detection covers PR opened, landing failed, cancelled, and missing entities", () =>
            {
                AskWorkSnapshot before = Snapshot(AskTrackedWorkStateEnum.Active, M("msn_1", "InProgress", null));
                AskWorkSnapshot pr = Snapshot(AskTrackedWorkStateEnum.Active, M("msn_1", "PullRequestOpen", "https://example/pr/1"));
                List<AskMilestone> opened = AskMilestoneDetector.Detect(before, pr, AskTrackedWorkStateEnum.Active);
                AssertEqual("PullRequestOpened", opened.Single().Kind);
                AssertContains("https://example/pr/1", opened.Single().Text);

                AskWorkSnapshot landingFailed = Snapshot(AskTrackedWorkStateEnum.Active, M("msn_1", "LandingFailed", null));
                landingFailed.Missions[0].FailureReason = "merge conflict";
                AssertEqual("LandingFailed", AskMilestoneDetector.Detect(before, landingFailed, AskTrackedWorkStateEnum.Active).Single().Kind);

                AskWorkSnapshot cancelled = Snapshot(AskTrackedWorkStateEnum.Cancelled, M("msn_1", "InProgress", null));
                AskMilestone end = AskMilestoneDetector.Detect(before, cancelled, AskTrackedWorkStateEnum.Active).Single();
                AssertEqual("Cancelled", end.Kind);
                AssertTrue(end.Terminal, "terminal");

                AskWorkSnapshot gone = Snapshot(AskTrackedWorkStateEnum.Cancelled);
                gone.Found = false;
                AssertContains("no longer exists", AskMilestoneDetector.Detect(null, gone, AskTrackedWorkStateEnum.Active).Single().Text);

                AssertEqual(0, AskMilestoneDetector.Detect(before, Snapshot(AskTrackedWorkStateEnum.Active, M("msn_1", "InProgress", null)), AskTrackedWorkStateEnum.Active).Count, "no change");
                AssertEqual(0, AskMilestoneDetector.Detect(null, landingFailed, AskTrackedWorkStateEnum.Active).Count, "no history: only terminal changes");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("hash_ignores_capture_time", "The snapshot hash depends on content, not capture time", () =>
            {
                AskWorkSnapshot a = Snapshot(AskTrackedWorkStateEnum.Active, M("msn_1", "InProgress", null));
                AskWorkSnapshot b = Snapshot(AskTrackedWorkStateEnum.Active, M("msn_1", "InProgress", null));
                a.CapturedUtc = DateTime.UtcNow.AddMinutes(-5);
                b.CapturedUtc = DateTime.UtcNow;
                AssertEqual(AskWorkSnapshotBuilder.ComputeHash(a), AskWorkSnapshotBuilder.ComputeHash(b), "same content, same hash");
                b.Missions[0].Status = "Complete";
                AssertNotEqual(AskWorkSnapshotBuilder.ComputeHash(a), AskWorkSnapshotBuilder.ComputeHash(b), "content change");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Work Tracker", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AskWorkMissionSnapshot M(string id, string status, string? prUrl)
        {
            return new AskWorkMissionSnapshot { Id = id, Title = "M", Status = status, PrUrl = prUrl, CaptainId = "cpt_1" };
        }

        private static AskWorkSnapshot Snapshot(AskTrackedWorkStateEnum state, params AskWorkMissionSnapshot[] missions)
        {
            AskWorkSnapshot snapshot = new AskWorkSnapshot();
            snapshot.EntityType = AskTrackedEntityTypeEnum.Voyage;
            snapshot.EntityId = "vyg_1";
            snapshot.Title = "V";
            snapshot.State = state;
            snapshot.Missions.AddRange(missions);

            return snapshot;
        }

        private static async Task<int> CountUpdatesAsync(AskTestHarness h, AuthContext owner, string threadId)
        {
            AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, threadId, null).ConfigureAwait(false))!;
            return page.Messages.Count(m => m.Kind == AskMessageKindEnum.WorkUpdate);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
