namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Terminal status chosen by <see cref="VoyageSettlement.DecideAsync"/> once every mission has settled.
    /// </summary>
    public sealed class VoyageSettlementSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VoyageSettlement";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("all_cancelled_settles_cancelled", "Every cancelled mission settles the voyage as Cancelled", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Cancelled, MissionStatusEnum.Cancelled).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Cancelled, settled, "every mission cancelled");
            }));

            cases.Add(CaseAsync("cancelled_and_work_produced_without_complete_settles_failed", "Cancelled plus settled WorkProduced and no Complete settles the voyage as Failed", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Cancelled, MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Failed, settled, "cancelled work with no completed mission");
            }));

            cases.Add(CaseAsync("complete_and_cancelled_settles_complete", "A completed mission with a cancelled mission settles the voyage as Complete", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Complete, MissionStatusEnum.Cancelled).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Complete, settled, "completed mission present");
            }));

            cases.Add(CaseAsync("failed_mission_settles_failed", "A failed mission settles the voyage as Failed", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Failed, MissionStatusEnum.Complete).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Failed, settled, "failed mission");
            }));

            cases.Add(CaseAsync("landing_failed_mission_settles_failed", "A landing-failed mission settles the voyage as Failed", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.LandingFailed, MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Failed, settled, "landing-failed mission");
            }));

            cases.Add(CaseAsync("work_produced_only_settles_complete", "Settled WorkProduced missions and no cancelled mission settle the voyage as Complete", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Complete, settled, "work produced only");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Voyage Settlement", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<VoyageStatusEnum?> SettleAsync(params MissionStatusEnum[] statuses)
        {
            List<Mission> missions = new List<Mission>();
            foreach (MissionStatusEnum status in statuses)
            {
                Mission mission = new Mission(status.ToString());
                mission.Status = status;
                missions.Add(mission);
            }

            // Completion is finished and no merge-queue entry is still open, so the decision is the terminal status.
            return await VoyageSettlement.DecideAsync(
                missions,
                _ => false,
                () => Task.FromResult(new HashSet<string>())).ConfigureAwait(false);
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
