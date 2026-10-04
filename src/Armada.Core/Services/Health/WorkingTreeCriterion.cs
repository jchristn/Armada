namespace Armada.Core.Services.Health
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades the working tree (git status --porcelain): Pass when clean, Warn when only untracked files exist, Fail
    /// when tracked files are modified. Not applicable to a bare clone. Writes IsDirty and UntrackedCount.
    /// </summary>
    public class WorkingTreeCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.WorkingTree;

        /// <inheritdoc />
        public bool RequiresRepository => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<bool> AppliesAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Task.FromResult(!context.IsBare);
        }

        /// <inheritdoc />
        public async Task<VesselHealthCriterionResult> EvaluateAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            GitWorkingTreeStatus status = await context.Git.GetWorkingTreeStatusAsync(context.EvaluatedPath!, token).ConfigureAwait(false);
            context.Health.IsDirty = status.ModifiedCount > 0;
            context.Health.UntrackedCount = status.UntrackedCount;

            if (status.ModifiedCount > 0)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Fail, VesselHealthDetailCodes.Modified, status.ModifiedCount, status.UntrackedCount);
            if (status.UntrackedCount > 0)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Warn, VesselHealthDetailCodes.UntrackedOnly, 0, status.UntrackedCount);
            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.Clean, 0, 0);
        }

        #endregion
    }
}
