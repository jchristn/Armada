namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for stage-level manual review gates in pipeline dispatch. Covers a reviewed stage
    /// entering Review and blocking downstream work, approving a review to complete the stage and
    /// release the next, denying a review to retry the same stage with feedback, denying a review to
    /// fail the pipeline and cancel downstream stages, and a single-stage reviewed worker that retains
    /// its dock until approval and then lands.
    /// </summary>
    public sealed class ReviewGateSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ReviewGate";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Review Gate suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("reviewed_stage_enters_review_and_blocks_downstream_dispatch", "Reviewed stage enters Review and blocks downstream dispatch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ReviewScenario scenario = await CreateScenarioAsync(testDb.Driver, includeDownstreamStage: true).ConfigureAwait(false);

                    await scenario.Missions.HandleCompletionAsync(scenario.WorkerCaptain, scenario.WorkerMission.Id).ConfigureAwait(false);

                    Mission? reviewed = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    Mission? downstream = await testDb.Driver.Missions.ReadAsync(scenario.DownstreamMission!.Id).ConfigureAwait(false);
                    Captain? workerCaptain = await testDb.Driver.Captains.ReadAsync(scenario.WorkerCaptain.Id).ConfigureAwait(false);
                    List<Dock> docks = await testDb.Driver.Docks.EnumerateByVesselAsync(scenario.Vessel.Id).ConfigureAwait(false);

                    AssertNotNull(reviewed, "Reviewed mission should still exist");
                    AssertEqual(MissionStatusEnum.Review, reviewed!.Status, "Reviewed stage should stop in Review");
                    AssertTrue(reviewed.RequiresReview, "Reviewed stage should carry the copied review gate");
                    AssertNotNull(reviewed.ReviewRequestedUtc, "Review-request time should be stamped");
                    AssertNotNull(downstream, "Downstream stage should still exist");
                    AssertEqual(MissionStatusEnum.Pending, downstream!.Status, "Downstream stage should remain blocked pending approval");
                    AssertNull(downstream.CaptainId, "Blocked downstream stage should not be assigned");
                    AssertNotNull(workerCaptain, "Worker captain should still exist");
                    AssertEqual(CaptainStateEnum.Idle, workerCaptain!.State, "Worker captain should be released while review is pending");
                    AssertEqual(0, docks.Count(d => d.Active), "Non-terminal review should not retain an active dock");
                }
            }));

            cases.Add(CaseAsync("approving_review_completes_stage_and_assigns_downstream", "Approving review completes reviewed stage and assigns downstream mission", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ReviewScenario scenario = await CreateScenarioAsync(testDb.Driver, includeDownstreamStage: true).ConfigureAwait(false);

                    await scenario.Missions.HandleCompletionAsync(scenario.WorkerCaptain, scenario.WorkerMission.Id).ConfigureAwait(false);

                    Mission? beforeApprove = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    AssertNotNull(beforeApprove, "Reviewed mission should be readable before approval");
                    string? branchName = beforeApprove!.BranchName;

                    await scenario.Missions.ApproveReviewAsync(scenario.WorkerMission.Id, "usr_reviewer", "Looks good").ConfigureAwait(false);

                    Mission? approved = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    Mission? downstream = await testDb.Driver.Missions.ReadAsync(scenario.DownstreamMission!.Id).ConfigureAwait(false);

                    AssertNotNull(approved, "Approved mission should still exist");
                    AssertEqual(MissionStatusEnum.Complete, approved!.Status, "Approved reviewed stage should complete");
                    AssertEqual("usr_reviewer", approved.ReviewedByUserId, "Reviewer should be recorded");
                    AssertEqual("Looks good", approved.ReviewComment, "Approval comment should be recorded");

                    AssertNotNull(downstream, "Downstream stage should still exist");
                    AssertEqual(MissionStatusEnum.InProgress, downstream!.Status, "Approval should release the next stage");
                    AssertEqual(branchName, downstream.BranchName, "Downstream stage should inherit the reviewed branch");
                    AssertContains("## Prior Stage Output", downstream.Description ?? "", "Downstream description should include prior-stage handoff");
                    AssertEqual(scenario.JudgeCaptain!.Id, downstream.CaptainId, "Judge stage should be assigned to the judge captain");
                }
            }));

            cases.Add(CaseAsync("denying_review_retries_same_stage_on_existing_branch", "Denying review retries same stage on existing branch with feedback", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ReviewScenario scenario = await CreateScenarioAsync(testDb.Driver, includeDownstreamStage: true).ConfigureAwait(false);

                    await scenario.Missions.HandleCompletionAsync(scenario.WorkerCaptain, scenario.WorkerMission.Id).ConfigureAwait(false);

                    Mission? beforeDeny = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    AssertNotNull(beforeDeny, "Reviewed mission should be readable before denial");
                    string? branchName = beforeDeny!.BranchName;

                    await scenario.Missions.DenyReviewAsync(scenario.WorkerMission.Id, "usr_reviewer", "Add tests before continuing").ConfigureAwait(false);

                    Mission? retried = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    Mission? downstream = await testDb.Driver.Missions.ReadAsync(scenario.DownstreamMission!.Id).ConfigureAwait(false);

                    AssertNotNull(retried, "Retried mission should still exist");
                    AssertEqual(MissionStatusEnum.InProgress, retried!.Status, "Denied review should requeue and relaunch the same stage");
                    AssertEqual(branchName, retried.BranchName, "Retry should stay on the existing branch");
                    AssertEqual("usr_reviewer", retried.ReviewedByUserId, "Reviewer should be recorded");
                    AssertEqual("Add tests before continuing", retried.ReviewComment, "Denial comment should be recorded");
                    AssertContains("## Review Feedback", retried.Description ?? "", "Retry description should embed review feedback");
                    AssertContains("Add tests before continuing", retried.Description ?? "", "Retry description should include reviewer comment");
                    AssertNotNull(downstream, "Downstream stage should still exist");
                    AssertEqual(MissionStatusEnum.Pending, downstream!.Status, "Denied review should keep downstream work blocked");
                }
            }));

            cases.Add(CaseAsync("denying_review_can_fail_pipeline_and_cancel_downstream", "Denying review can fail pipeline and cancel downstream stages", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ReviewScenario scenario = await CreateScenarioAsync(
                        testDb.Driver,
                        includeDownstreamStage: true,
                        firstStageDenyAction: ReviewDenyActionEnum.FailPipeline).ConfigureAwait(false);

                    await scenario.Missions.HandleCompletionAsync(scenario.WorkerCaptain, scenario.WorkerMission.Id).ConfigureAwait(false);
                    await scenario.Missions.DenyReviewAsync(scenario.WorkerMission.Id, "usr_reviewer", "This plan is not acceptable").ConfigureAwait(false);

                    Mission? failed = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    Mission? downstream = await testDb.Driver.Missions.ReadAsync(scenario.DownstreamMission!.Id).ConfigureAwait(false);
                    Voyage? voyage = await testDb.Driver.Voyages.ReadAsync(scenario.Voyage.Id).ConfigureAwait(false);

                    AssertNotNull(failed, "Failed reviewed mission should still exist");
                    AssertEqual(MissionStatusEnum.Failed, failed!.Status, "Denied review should fail the mission when configured");
                    AssertContains("Review denied", failed.FailureReason ?? "", "Failure reason should record the review denial");
                    AssertEqual(MissionFailureKindEnum.ReviewDenied, failed.FailureKind, "failure kind is recorded where the failure happens");
                    AssertNotNull(downstream, "Downstream stage should still exist");
                    AssertEqual(MissionStatusEnum.Cancelled, downstream!.Status, "Downstream work should be cancelled when the review gate fails the pipeline");
                    AssertNotNull(voyage, "Voyage should still exist");
                    AssertEqual(VoyageStatusEnum.Failed, voyage!.Status, "Voyage should fail when a review gate fails the pipeline");
                }
            }));

            cases.Add(CaseAsync("single_stage_reviewed_worker_retains_dock_until_approval_then_lands", "Single-stage reviewed Worker pipeline retains dock until approval and then lands", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ReviewScenario scenario = await CreateScenarioAsync(testDb.Driver, includeDownstreamStage: false).ConfigureAwait(false);

                    scenario.Missions.OnMissionComplete = async (mission, dock) =>
                    {
                        mission.Status = MissionStatusEnum.Complete;
                        mission.CompletedUtc = DateTime.UtcNow;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    };

                    await scenario.Missions.HandleCompletionAsync(scenario.WorkerCaptain, scenario.WorkerMission.Id).ConfigureAwait(false);

                    Mission? awaitingApproval = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    Dock? retainedDock = awaitingApproval?.DockId == null
                        ? null
                        : await testDb.Driver.Docks.ReadAsync(awaitingApproval.DockId).ConfigureAwait(false);

                    AssertNotNull(awaitingApproval, "Single-stage mission should still exist");
                    AssertEqual(MissionStatusEnum.Review, awaitingApproval!.Status, "Single-stage worker pipeline should honor the review gate");
                    AssertNotNull(retainedDock, "Terminal review should retain the dock until approval");
                    AssertTrue(retainedDock!.Active, "Retained dock should stay active while terminal review is pending");

                    await scenario.Missions.ApproveReviewAsync(scenario.WorkerMission.Id, "usr_reviewer", "Ship it").ConfigureAwait(false);

                    Mission? completed = await testDb.Driver.Missions.ReadAsync(scenario.WorkerMission.Id).ConfigureAwait(false);
                    Dock? reclaimedDock = retainedDock == null
                        ? null
                        : await testDb.Driver.Docks.ReadAsync(retainedDock.Id).ConfigureAwait(false);

                    AssertNotNull(completed, "Approved single-stage mission should still exist");
                    AssertEqual(MissionStatusEnum.Complete, completed!.Status, "Approved terminal review should proceed through landing");
                    AssertNotNull(reclaimedDock, "Dock row should remain readable after reclaim");
                    AssertFalse(reclaimedDock!.Active, "Dock should be reclaimed after landing approval");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Review Gate Workflows",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_review_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_review_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private static async Task<ReviewScenario> CreateScenarioAsync(
            DatabaseDriver db,
            bool includeDownstreamStage,
            ReviewDenyActionEnum firstStageDenyAction = ReviewDenyActionEnum.RetryStage)
        {
            LoggingModule logging = CreateLogging();
            ArmadaSettings settings = CreateSettings();
            DirCreatingGitService git = new DirCreatingGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService, git: git);
            IVoyageService voyageService = new VoyageService(logging, db);
            AdmiralService admiralService = new AdmiralService(logging, db, settings, captainService, missionService, voyageService, dockService);

            int nextPid = 4000;
            captainService.OnLaunchAgent = (_, _, _) =>
            {
                nextPid++;
                return Task.FromResult(nextPid);
            };
            missionService.OnGetMissionOutput = _ => "reviewable stage output";

            Vessel vessel = new Vessel("review-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_review_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_review_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Captain workerCaptain = new Captain("review-worker");
            workerCaptain.State = CaptainStateEnum.Idle;
            workerCaptain.AllowedPersonas = "[\"Worker\"]";
            workerCaptain = await db.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

            Captain? judgeCaptain = null;
            if (includeDownstreamStage)
            {
                judgeCaptain = new Captain("review-judge");
                judgeCaptain.State = CaptainStateEnum.Idle;
                judgeCaptain.AllowedPersonas = "[\"Judge\"]";
                judgeCaptain = await db.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);
            }

            Pipeline pipeline = new Pipeline(includeDownstreamStage ? "ReviewedPipeline" : "ReviewedWorkerOnly");
            pipeline.Stages = new List<PipelineStage>
            {
                new PipelineStage(1, "Worker")
                {
                    RequiresReview = true,
                    ReviewDenyAction = firstStageDenyAction
                }
            };
            if (includeDownstreamStage)
            {
                pipeline.Stages.Add(new PipelineStage(2, "Judge"));
            }

            pipeline = await db.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

            Voyage voyage = await admiralService.DispatchVoyageAsync(
                "Review Voyage",
                "Review workflow coverage",
                vessel.Id,
                new List<MissionDescription>
                {
                    new MissionDescription("Implement review gate", "Implement the requested change.")
                },
                pipeline.Id).ConfigureAwait(false);

            List<Mission> voyageMissions = await db.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
            Mission workerMission = voyageMissions.First(m => String.Equals(m.Persona ?? "Worker", "Worker", StringComparison.OrdinalIgnoreCase));
            Mission? downstreamMission = voyageMissions.FirstOrDefault(m => String.Equals(m.Persona, "Judge", StringComparison.OrdinalIgnoreCase));

            Captain? assignedWorker = await db.Captains.ReadAsync(workerCaptain.Id).ConfigureAwait(false);
            AssertNotNull(assignedWorker, "Worker captain should be readable after dispatch");

            workerMission = await db.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Expected worker mission to be readable after dispatch.");

            if (includeDownstreamStage)
            {
                AssertNotNull(downstreamMission, "Downstream judge stage should exist");
            }

            return new ReviewScenario
            {
                Missions = missionService,
                Vessel = vessel,
                Voyage = voyage,
                WorkerCaptain = assignedWorker!,
                JudgeCaptain = judgeCaptain,
                WorkerMission = workerMission,
                DownstreamMission = downstreamMission
            };
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Types

        private sealed class ReviewScenario
        {
            public MissionService Missions { get; set; } = null!;
            public Vessel Vessel { get; set; } = null!;
            public Voyage Voyage { get; set; } = null!;
            public Captain WorkerCaptain { get; set; } = null!;
            public Captain? JudgeCaptain { get; set; } = null;
            public Mission WorkerMission { get; set; } = null!;
            public Mission? DownstreamMission { get; set; } = null;
        }

        #endregion
    }
}
