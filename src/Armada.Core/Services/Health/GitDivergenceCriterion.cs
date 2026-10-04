namespace Armada.Core.Services.Health
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades how far HEAD has drifted from the vessel's default branch (origin/&lt;DefaultBranch&gt;, falling back to the
    /// local default branch) and from its upstream (@{u}). Pass when not behind; Warn when behind by at least
    /// Thresholds.BehindWarn; Fail when behind by at least Thresholds.BehindFail, or when diverged both ways from the
    /// default branch or from the upstream. A failed fetch grades Unknown (FetchFailed) instead of trusting stale refs.
    /// Writes CurrentBranch, AheadOfDefault, BehindDefault, AheadOfUpstream, and BehindUpstream.
    /// </summary>
    public class GitDivergenceCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.GitDivergence;

        /// <inheritdoc />
        public bool RequiresRepository => true;

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
            string path = context.EvaluatedPath!;

            context.Health.CurrentBranch = await context.Git.GetCurrentBranchAsync(path, token).ConfigureAwait(false);

            if (context.FetchFailed)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, VesselHealthDetailCodes.FetchFailed);

            DateTime? last = await context.Git.GetLastCommitUtcAsync(path, token).ConfigureAwait(false);
            if (last == null)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, VesselHealthDetailCodes.NoCommits);

            GitDivergenceCounts? upstream = null;
            if (!context.IsBare)
            {
                upstream = await context.Git.GetDivergenceAsync(path, "@{u}", "HEAD", token).ConfigureAwait(false);
                if (upstream != null)
                {
                    context.Health.AheadOfUpstream = upstream.Ahead;
                    context.Health.BehindUpstream = upstream.Behind;
                }
            }

            GitDivergenceCounts? counts = await context.Git.GetDivergenceAsync(path, "origin/" + context.DefaultBranch, "HEAD", token).ConfigureAwait(false);
            if (counts == null)
                counts = await context.Git.GetDivergenceAsync(path, context.DefaultBranch, "HEAD", token).ConfigureAwait(false);
            if (counts == null)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, VesselHealthDetailCodes.DefaultBranchMissing);

            context.Health.AheadOfDefault = counts.Ahead;
            context.Health.BehindDefault = counts.Behind;
            return Grade(counts, upstream, context.Settings.Thresholds.BehindWarn, context.Settings.Thresholds.BehindFail);
        }

        /// <summary>
        /// Grade divergence counts against thresholds.
        /// </summary>
        /// <param name="toDefault">Counts relative to the default branch.</param>
        /// <param name="toUpstream">Counts relative to the upstream, or null when there is no upstream.</param>
        /// <param name="behindWarn">Behind count at which the grade is Warn.</param>
        /// <param name="behindFail">Behind count at which the grade is Fail.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when toDefault is null.</exception>
        public static VesselHealthCriterionResult Grade(GitDivergenceCounts toDefault, GitDivergenceCounts? toUpstream, int behindWarn, int behindFail)
        {
            if (toDefault == null) throw new ArgumentNullException(nameof(toDefault));
            long ahead = toDefault.Ahead;
            long behind = toDefault.Behind;

            bool divergedDefault = toDefault.Ahead > 0 && toDefault.Behind > 0;
            bool divergedUpstream = toUpstream != null && toUpstream.Ahead > 0 && toUpstream.Behind > 0;
            if (divergedDefault || divergedUpstream)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Fail, VesselHealthDetailCodes.Diverged, ahead, behind);

            if (toDefault.Behind > 0)
            {
                VesselHealthStatusEnum status = toDefault.Behind >= behindFail
                    ? VesselHealthStatusEnum.Fail
                    : (toDefault.Behind >= behindWarn ? VesselHealthStatusEnum.Warn : VesselHealthStatusEnum.Pass);
                return new VesselHealthCriterionResult(status, VesselHealthDetailCodes.Behind, ahead, behind);
            }

            if (toDefault.Ahead > 0)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.Ahead, ahead, behind);

            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.Even, 0, 0);
        }

        #endregion
    }
}
