namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Mission landing preview regressions: a Pending mission is never Ready To Land (F11), a mission whose effective
    /// landing mode is None reports manual landing only and is not ready (F12), and a Complete mission is not ready (F22).
    /// </summary>
    public sealed class LandingPreviewMissionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LandingPreviewMission";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("pending_mission_not_ready", "A Pending mission is not Ready To Land", TestTags.Negative, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.Pending, LandingModeEnum.LocalMerge, null).ConfigureAwait(false);
                AssertFalse(preview.IsReadyToLand, "Pending is not ready to land");
                AssertTrue(preview.Issues.Exists(i => i.Code == "mission_not_landable"), "not-landable issue");
                AssertEqual(MissionStatusEnum.Pending, preview.MissionStatus, "mission status echoed");
            }));

            cases.Add(CaseAsync("work_produced_local_merge_ready", "A WorkProduced mission on a Local Merge vessel is ready", TestTags.Positive, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, LandingModeEnum.LocalMerge, null).ConfigureAwait(false);
                AssertTrue(preview.IsReadyToLand, "ready");
                AssertFalse(preview.ManualLandingOnly, "automatic landing");
                AssertEqual(LandingModeEnum.LocalMerge, preview.EffectiveLandingMode, "effective mode");
            }));

            cases.Add(CaseAsync("local_merge_action_says_no_push", "Local Merge previews a merge into the working directory without a push", TestTags.Positive, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, LandingModeEnum.LocalMerge, null).ConfigureAwait(false);
                AssertEqual("Merge the branch into the target branch in the working directory, without pushing", preview.ExpectedLandingAction, "expected action");
            }));

            cases.Add(CaseAsync("work_produced_merge_and_push_ready", "A WorkProduced mission on a Merge and Push vessel is ready, and the preview says it pushes", TestTags.Positive, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, LandingModeEnum.MergeAndPush, null).ConfigureAwait(false);
                AssertTrue(preview.IsReadyToLand, "ready");
                AssertFalse(preview.ManualLandingOnly, "automatic landing");
                AssertEqual(LandingModeEnum.MergeAndPush, preview.EffectiveLandingMode, "effective mode");
                AssertEqual("Merge the branch into the target branch in the working directory, then push it", preview.ExpectedLandingAction, "expected action");
            }));

            cases.Add(CaseAsync("unset_modes_use_merge_and_push_default", "With no landing mode on the vessel or voyage, the effective mode is the MergeAndPush default", TestTags.Positive, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, null, null).ConfigureAwait(false);
                AssertEqual(LandingModeEnum.MergeAndPush, preview.EffectiveLandingMode, "effective mode");
                AssertFalse(preview.ManualLandingOnly, "automatic landing");
            }));

            cases.Add(CaseAsync("voyage_local_merge_overrides_vessel_merge_and_push", "A voyage's Local Merge overrides the vessel's Merge and Push in the preview", TestTags.Positive, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, LandingModeEnum.MergeAndPush, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                AssertEqual(LandingModeEnum.LocalMerge, preview.EffectiveLandingMode, "effective mode from the voyage");
            }));

            foreach (LandingModeEnum localMode in new[] { LandingModeEnum.LocalMerge, LandingModeEnum.MergeAndPush })
            {
                LandingModeEnum mode = localMode;
                cases.Add(CaseAsync("hotfix_protected_branch_warns_" + mode.ToString().ToLowerInvariant(), mode + " of a hotfix into a protected branch that requires pull requests warns", TestTags.Positive, async () =>
                {
                    LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, mode, null, "hotfix/urgent", v =>
                    {
                        v.ProtectedBranchPatterns = new List<string> { "main" };
                        v.RequirePullRequestForProtectedBranches = true;
                    }).ConfigureAwait(false);
                    AssertTrue(preview.Issues.Exists(i => i.Code == "hotfix_branch_local_merge_warning"), "hotfix local merge warning for " + mode);
                }));
            }

            cases.Add(CaseAsync("hotfix_protected_branch_pull_request_no_local_merge_warning", "PullRequest of a hotfix into a protected branch has no local merge warning", TestTags.Negative, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, LandingModeEnum.PullRequest, null, "hotfix/urgent", v =>
                {
                    v.ProtectedBranchPatterns = new List<string> { "main" };
                    v.RequirePullRequestForProtectedBranches = true;
                }).ConfigureAwait(false);
                AssertFalse(preview.Issues.Exists(i => i.Code == "hotfix_branch_local_merge_warning"), "no local merge warning for PullRequest");
            }));

            cases.Add(CaseAsync("landing_mode_none_manual_only", "Landing Mode None reports manual landing only and is not ready", TestTags.Negative, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, LandingModeEnum.None, null).ConfigureAwait(false);
                AssertTrue(preview.ManualLandingOnly, "manual only");
                AssertFalse(preview.IsReadyToLand, "Land would be refused, so not ready");
            }));

            cases.Add(CaseAsync("voyage_mode_overrides_vessel_none", "A voyage landing mode overrides the vessel's None", TestTags.Positive, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.WorkProduced, LandingModeEnum.None, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                AssertFalse(preview.ManualLandingOnly, "voyage mode applies");
                AssertEqual(LandingModeEnum.LocalMerge, preview.EffectiveLandingMode, "effective mode from the voyage");
            }));

            cases.Add(CaseAsync("complete_mission_not_ready", "A Complete mission is not Ready To Land", TestTags.Negative, async () =>
            {
                LandingPreviewResult preview = await PreviewAsync(MissionStatusEnum.Complete, LandingModeEnum.LocalMerge, null).ConfigureAwait(false);
                AssertFalse(preview.IsReadyToLand, "nothing left to land");
                AssertTrue(preview.Issues.Exists(i => i.Code == "mission_already_landed"), "already-landed issue");
            }));

            return new TestSuiteDescriptor(SuiteId, "Mission landing preview", cases);
        }

        #endregion

        #region Private-Methods

        private static Task<LandingPreviewResult> PreviewAsync(MissionStatusEnum status, LandingModeEnum? vesselMode, LandingModeEnum? voyageMode)
        {
            return PreviewAsync(status, vesselMode, voyageMode, "armada/preview", null);
        }

        private static async Task<LandingPreviewResult> PreviewAsync(MissionStatusEnum status, LandingModeEnum? vesselMode, LandingModeEnum? voyageMode, string branchName, Action<Vessel>? configureVessel)
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                LandingPreviewService service = new LandingPreviewService(testDb.Driver, logging, new ArmadaSettings());

                Vessel vessel = new Vessel("preview-vessel", "https://github.com/test/repo.git");
                vessel.DefaultBranch = "main";
                vessel.LandingMode = vesselMode;
                configureVessel?.Invoke(vessel);
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Mission mission = new Mission("preview", "work");
                mission.VesselId = vessel.Id;
                mission.BranchName = branchName;
                mission.Status = status;
                if (voyageMode.HasValue)
                {
                    Voyage voyage = new Voyage("preview voyage");
                    voyage.LandingMode = voyageMode;
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                    mission.VoyageId = voyage.Id;
                }

                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
                AuthContext auth = AuthContext.Authenticated("default", "default", true, true, "UnitTest");
                return await service.PreviewForMissionAsync(auth, vessel, mission).ConfigureAwait(false);
            }
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
    }
}
