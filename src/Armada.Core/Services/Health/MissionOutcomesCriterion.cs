namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades recent mission outcomes on the vessel: the number of Failed and LandingFailed missions that finished (or
    /// were last updated) within MissionWindowDays. Pass at 0, Warn at Thresholds.MissionFailureWarn or more, Fail at
    /// Thresholds.MissionFailureFail or more. Writes RecentMissionFailureCount.
    /// </summary>
    public class MissionOutcomesCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.MissionOutcomes;

        /// <inheritdoc />
        public bool RequiresRepository => false;

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public MissionOutcomesCriterion(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<bool> AppliesAsync(VesselHealthContext context, CancellationToken token = default)
        {
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public async Task<VesselHealthCriterionResult> EvaluateAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            int window = context.Settings.MissionWindowDays;
            DateTime cutoff = context.NowUtc.AddDays(-window);
            List<MissionSummary> missions = await _Database.Missions.EnumerateSummariesByVesselAsync(context.TenantId, context.Vessel.Id, token).ConfigureAwait(false);
            int failures = missions.Count(m =>
                (m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.LandingFailed)
                && (m.CompletedUtc ?? m.LastUpdateUtc) >= cutoff);
            context.Health.RecentMissionFailureCount = failures;
            return Grade(failures, window, context.Settings.Thresholds.MissionFailureWarn, context.Settings.Thresholds.MissionFailureFail);
        }

        /// <summary>
        /// Grade a failure count.
        /// </summary>
        /// <param name="failures">Failed and landing-failed missions in the window.</param>
        /// <param name="windowDays">Window in days.</param>
        /// <param name="warn">Count at which the grade is Warn.</param>
        /// <param name="fail">Count at which the grade is Fail.</param>
        /// <returns>The result.</returns>
        public static VesselHealthCriterionResult Grade(int failures, int windowDays, int warn, int fail)
        {
            if (failures >= fail) return new VesselHealthCriterionResult(VesselHealthStatusEnum.Fail, VesselHealthDetailCodes.RecentFailures, failures, windowDays);
            if (failures >= warn) return new VesselHealthCriterionResult(VesselHealthStatusEnum.Warn, VesselHealthDetailCodes.RecentFailures, failures, windowDays);
            if (failures > 0) return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.RecentFailures, failures, windowDays);
            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.NoRecentFailures, 0, windowDays);
        }

        #endregion
    }
}
