namespace Armada.Core.Services.Health
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;

    /// <summary>
    /// Informational criterion recording the age of the most recent commit (git log -1 --format=%cI). Grades Pass when
    /// a commit exists (LastCommitAge, ValueA = whole days) and Unknown when the repository has no commits. Not scored by
    /// default. Writes LastCommitUtc.
    /// </summary>
    public class CommitRecencyCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.CommitRecency;

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
            DateTime? last = await context.Git.GetLastCommitUtcAsync(context.EvaluatedPath!, token).ConfigureAwait(false);
            context.Health.LastCommitUtc = last;
            if (last == null)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, VesselHealthDetailCodes.NoCommits);

            long days = (long)Math.Floor((context.NowUtc - last.Value).TotalDays);
            if (days < 0) days = 0;
            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.LastCommitAge, days, null);
        }

        #endregion
    }
}
