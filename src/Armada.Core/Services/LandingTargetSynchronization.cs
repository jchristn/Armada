namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>Integrate the latest known remote and local target before landing a mission branch.</summary>
    public static class LandingTargetSynchronization
    {
        /// <summary>
        /// Fetch current target refs and merge them into the mission dock. Return the dock head before and after and the
        /// target tips integrated against; <see cref="LandingTargetSynchronizationResult.Advanced"/> says whether it changed.
        /// </summary>
        /// <exception cref="GitMergeConflictException">Thrown when git reports a merge conflict integrating the target.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the target cannot be resolved or merged for another reason.</exception>
        public static async Task<LandingTargetSynchronizationResult> SynchronizeAsync(IGitService git, string worktree, string targetBranch, string? targetCheckout, CancellationToken token = default)
        {
            LandingTargetSynchronizationResult result = new LandingTargetSynchronizationResult();
            string? before = await git.GetHeadCommitHashAsync(worktree, token).ConfigureAwait(false);
            result.Before = before;
            await git.FetchRemotesAsync(worktree, token).ConfigureAwait(false);
            string? remoteTip = await git.ResolveBranchTipAsync(worktree, "origin/" + targetBranch, token).ConfigureAwait(false);
            result.RemoteTargetTip = String.IsNullOrEmpty(remoteTip) ? null : remoteTip;
            if (!String.IsNullOrEmpty(remoteTip) && remoteTip != before)
            {
                GitCommitMergeResult merged = await git.MergeCommitAsync(worktree, remoteTip, targetBranch, "Integrate current landing target " + targetBranch, token).ConfigureAwait(false);
                if (merged.Outcome == GitCommitMergeOutcomeEnum.Conflicted)
                    throw new GitMergeConflictException("Cannot integrate current landing target: " + merged.Message + " " + String.Join(", ", merged.ConflictedFiles), merged.ConflictedFiles);
                if (merged.Outcome != GitCommitMergeOutcomeEnum.Merged && merged.Outcome != GitCommitMergeOutcomeEnum.AlreadyContained)
                    throw new InvalidOperationException("Cannot integrate current landing target: " + merged.Message + " " + String.Join(", ", merged.ConflictedFiles));
            }
            string? localTip = String.IsNullOrEmpty(targetCheckout) ? null : await git.ResolveBranchTipAsync(targetCheckout, targetBranch, token).ConfigureAwait(false);
            result.LocalTargetTip = String.IsNullOrEmpty(localTip) ? null : localTip;
            if (!String.IsNullOrEmpty(localTip) && localTip != before && localTip != remoteTip)
                await git.MergeBranchLocalAsync(worktree, targetCheckout!, targetBranch, token: token).ConfigureAwait(false);
            else if (String.IsNullOrEmpty(remoteTip) && String.IsNullOrEmpty(localTip))
                throw new InvalidOperationException("Current landing target could not be resolved; refresh the target refs and retry landing.");
            result.After = await git.GetHeadCommitHashAsync(worktree, token).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Build the <c>mission.landing_target_advanced</c> event payload from a synchronization result.
        /// </summary>
        /// <param name="result">The synchronization result.</param>
        /// <param name="targetBranch">The target branch.</param>
        /// <returns>The payload.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public static LandingTargetAdvancedPayload ToAdvancedPayload(LandingTargetSynchronizationResult result, string? targetBranch)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            LandingTargetAdvancedPayload payload = new LandingTargetAdvancedPayload();
            payload.TargetBranch = targetBranch;
            payload.Before = result.Before;
            payload.After = result.After;
            payload.RemoteTargetTip = result.RemoteTargetTip;
            payload.LocalTargetTip = result.LocalTargetTip;
            return payload;
        }
    }
}
