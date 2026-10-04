namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades local branches. A branch is stale when its tip commit is older than StaleBranchDays and it is not the
    /// default branch, the current branch, or an armada/* branch (those are counted separately as leftovers). Pass when
    /// stale branches are below Thresholds.StaleBranchWarn and there are no armada/* branches; Fail when stale branches
    /// reach Thresholds.StaleBranchFail; otherwise Warn. Writes BranchCount, StaleBranchCount, and ArmadaBranchCount.
    /// </summary>
    public class BranchesCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.Branches;

        /// <inheritdoc />
        public bool RequiresRepository => true;

        /// <summary>
        /// Branch name prefix identifying branches created by Armada captains. Default "armada/".
        /// </summary>
        public string ArmadaBranchPrefix
        {
            get => _ArmadaBranchPrefix;
            set => _ArmadaBranchPrefix = String.IsNullOrEmpty(value) ? "armada/" : value;
        }

        #endregion

        #region Private-Members

        private string _ArmadaBranchPrefix = "armada/";

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
            IReadOnlyList<BranchInfo> branches = await context.Cache.GetBranchesAsync(context, token).ConfigureAwait(false);
            DateTime cutoff = context.NowUtc.AddDays(-context.Settings.StaleBranchDays);

            int stale = 0;
            int armada = 0;
            foreach (BranchInfo branch in branches)
            {
                if (branch.Name.StartsWith(_ArmadaBranchPrefix, StringComparison.Ordinal))
                {
                    armada++;
                    continue;
                }

                if (branch.IsDefault || branch.IsCurrent) continue;
                if (String.Equals(branch.Name, context.DefaultBranch, StringComparison.Ordinal)) continue;
                if (branch.CommitDate.HasValue && branch.CommitDate.Value < cutoff) stale++;
            }

            context.Health.BranchCount = branches.Count;
            context.Health.StaleBranchCount = stale;
            context.Health.ArmadaBranchCount = armada;
            return Grade(stale, armada, context.Settings.Thresholds.StaleBranchWarn, context.Settings.Thresholds.StaleBranchFail);
        }

        /// <summary>
        /// Grade stale and leftover armada branch counts against thresholds.
        /// </summary>
        /// <param name="stale">Stale branch count.</param>
        /// <param name="armada">Leftover armada/* branch count.</param>
        /// <param name="staleWarn">Stale count at which the grade is Warn.</param>
        /// <param name="staleFail">Stale count at which the grade is Fail.</param>
        /// <returns>The result.</returns>
        public static VesselHealthCriterionResult Grade(int stale, int armada, int staleWarn, int staleFail)
        {
            if (stale >= staleFail)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Fail, VesselHealthDetailCodes.StaleBranches, stale, armada);
            if (stale >= staleWarn)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Warn, VesselHealthDetailCodes.StaleBranches, stale, armada);
            if (armada > 0)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Warn, VesselHealthDetailCodes.ArmadaBranches, stale, armada);
            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.BranchesOk, stale, 0);
        }

        #endregion
    }
}
