namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Batched landing (RS-9.4, <see cref="Settings.ArmadaSettings.MergeQueueBatchSize"/> above 1): several entries of
    /// one group are merged into one integration worktree, verified once, and published with one push. A batch whose
    /// verification fails is split in half and each half is landed on its own, down to single entries, which take the
    /// ordinary one-entry path; so a failing entry fails alone and the others still land.
    /// </summary>
    public partial class MergeQueueService
    {
        #region Private-Methods

        // An entry joins a batch when it is processed in the same repository with the same test command. An entry whose
        // own command differs would be verified with the wrong checks.
        private static bool CanJoinBatch(MergeEntry first, MergeQueueHost firstHost, MergeEntry next, MergeQueueHost nextHost)
        {
            return String.Equals(firstHost.HarborId, nextHost.HarborId, StringComparison.Ordinal)
                && String.Equals(firstHost.RepoPath, nextHost.RepoPath, StringComparison.Ordinal)
                && String.Equals(first.TestCommand, next.TestCommand, StringComparison.Ordinal);
        }

        private async Task ProcessBatchAsync(List<MergeEntry> batch, MergeQueueHost host, MergeQueueGroupState state, CancellationToken token)
        {
            List<MergeEntry> testing = await MarkTestingAsync(batch, token).ConfigureAwait(false);
            if (testing.Count == 0) return;
            _Logging.Info(_Header + "landing a batch of " + testing.Count + " entries into " + testing[0].TargetBranch + ": " + String.Join(", ", testing.Select(e => e.Id)));
            await LandBatchAsync(testing, host, state, 0, token).ConfigureAwait(false);
        }

        // Mark each entry Testing; an entry that changed meanwhile drops out.
        private async Task<List<MergeEntry>> MarkTestingAsync(List<MergeEntry> entries, CancellationToken token)
        {
            List<MergeEntry> testing = new List<MergeEntry>();
            foreach (MergeEntry entry in entries)
            {
                entry.Status = MergeStatusEnum.Testing;
                entry.TestOutput = null;
                entry.TestExitCode = null;
                entry.TestStartedUtc = DateTime.UtcNow;
                if (await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) testing.Add(entry);
            }
            return testing;
        }

        // Every entry passed in is Testing. One entry takes the ordinary path.
        private async Task LandBatchAsync(List<MergeEntry> entries, MergeQueueHost host, MergeQueueGroupState state, int requeues, CancellationToken token)
        {
            if (entries.Count == 0) return;
            if (entries.Count == 1)
            {
                await ProcessResolvedEntryAsync(entries[0], host, state, token).ConfigureAwait(false);
                return;
            }

            MergeEntry first = entries[0];
            string worktreeId = "mq_batch_" + first.Id + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string integrationBranch = _IntegrationBranchPrefix + worktreeId;
            string integrationPath = host.IntegrationPath(worktreeId);
            _ActiveIntegrationBranches.TryAdd(integrationBranch, 0);
            bool worktreeCreated = false;
            List<MergeEntry> merged = new List<MergeEntry>();
            try
            {
                string? targetCommit;
                string verifiedCommit;
                try
                {
                    await CreateIntegrationWorktreeAsync(host, state, integrationPath, integrationBranch, first.TargetBranch, token).ConfigureAwait(false);
                    worktreeCreated = true;
                    targetCommit = await ResolveHeadAsync(host, integrationPath).ConfigureAwait(false);
                    foreach (MergeEntry entry in entries)
                    {
                        string landingRef = await LandingRefAsync(entry, token).ConfigureAwait(false);
                        string? landedCommit = await AlreadyLandedCommitAsync(entry, host, integrationPath, landingRef, token).ConfigureAwait(false);
                        if (landedCommit != null)
                        {
                            await CompleteAlreadyLandedAsync(entry, landedCommit, token).ConfigureAwait(false);
                            continue;
                        }

                        // An entry that conflicts with the target or with an earlier entry of the batch fails as it would
                        // have when landed after that entry; the merge is aborted and the batch goes on without it.
                        MergeQueueMergeResult merge = await MergeBranchAsync(host, integrationPath, landingRef, token).ConfigureAwait(false);
                        if (!merge.Merged)
                        {
                            await FailMergeAsync(entry, merge, targetCommit, token).ConfigureAwait(false);
                            continue;
                        }
                        merged.Add(entry);
                    }

                    if (merged.Count < 2)
                    {
                        await CleanupWorktreeAsync(host, host.RepoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
                        worktreeCreated = false;
                        if (merged.Count == 1) await ProcessResolvedEntryAsync(merged[0], host, state, token).ConfigureAwait(false);
                        return;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    await FallBackToSingleAsync(StillTesting(entries), host, state, "preparing the batch failed: " + ex.Message, integrationPath, integrationBranch, worktreeCreated, token).ConfigureAwait(false);
                    worktreeCreated = false;
                    return;
                }

                try
                {
                    verifiedCommit = await VerifyIntegrationAsync(merged[0], host, integrationPath, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    if (HarborGone(host))
                    {
                        await FallBackToSingleAsync(merged, host, state, "the Harbor disconnected during verification", integrationPath, integrationBranch, worktreeCreated, token).ConfigureAwait(false);
                        worktreeCreated = false;
                        return;
                    }

                    // Bisect: land each half on its own; the half with the failing entry splits again.
                    _Logging.Info(_Header + "batch of " + merged.Count + " failed verification (" + ex.Message + "); splitting it");
                    await CleanupWorktreeAsync(host, host.RepoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
                    worktreeCreated = false;
                    int half = merged.Count / 2;
                    await LandBatchAsync(merged.Take(half).ToList(), host, state, 0, token).ConfigureAwait(false);
                    await LandBatchAsync(merged.Skip(half).ToList(), host, state, 0, token).ConfigureAwait(false);
                    return;
                }

                MergeQueuePushResult push;
                try
                {
                    await HostGit(host).ValidatePublishedAncestryAsync(integrationPath, host.RepoPath, first.TargetBranch, verifiedCommit, token).ConfigureAwait(false);
                    foreach (MergeEntry entry in merged)
                    {
                        if (!await PrepareLandingAsync(entry, verifiedCommit, token).ConfigureAwait(false))
                            throw new InvalidOperationException("merge entry " + entry.Id + " changed before the batch was published");
                    }
                    push = await PushVerifiedAsync(host, verifiedCommit, first.TargetBranch, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    await FallBackToSingleAsync(StillTesting(merged), host, state, "publishing the batch failed: " + ex.Message, integrationPath, integrationBranch, worktreeCreated, token).ConfigureAwait(false);
                    worktreeCreated = false;
                    return;
                }

                await CleanupWorktreeAsync(host, host.RepoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
                worktreeCreated = false;
                if (push.Outcome == MergeQueuePushOutcomeEnum.NonFastForward && requeues < _Settings.MergeQueueRequeueLimit)
                {
                    List<MergeEntry> requeued = new List<MergeEntry>();
                    foreach (MergeEntry entry in merged)
                        if (await RequeueAfterTargetMovedAsync(entry, host, state, requeues, token).ConfigureAwait(false)) requeued.Add(entry);
                    await LandBatchAsync(await MarkTestingAsync(requeued, token).ConfigureAwait(false), host, state, requeues + 1, token).ConfigureAwait(false);
                    return;
                }

                if (push.Outcome != MergeQueuePushOutcomeEnum.Pushed)
                {
                    if (push.Outcome == MergeQueuePushOutcomeEnum.NonFastForward) state.MarkStale(host);
                    string reason = push.Outcome == MergeQueuePushOutcomeEnum.NonFastForward
                        ? "Landing failed: " + first.TargetBranch + " moved before the batch push " + (requeues + 1) + " time(s) in a row (mergeQueueRequeueLimit "
                            + _Settings.MergeQueueRequeueLimit + "); the push was not a fast-forward. " + push.Message
                        : "Landing failed: the remote refused the batch push: " + push.Message;
                    foreach (MergeEntry entry in merged) await FailLandingAsync(entry, reason.Trim(), null, targetCommit, token).ConfigureAwait(false);
                    return;
                }

                foreach (MergeEntry entry in merged) await CompleteLandedEntryAsync(entry, token).ConfigureAwait(false);
                _Logging.Info(_Header + "landed a batch of " + merged.Count + " entries as " + verifiedCommit);
            }
            finally
            {
                if (worktreeCreated) await CleanupWorktreeAsync(host, host.RepoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
                _ActiveIntegrationBranches.TryRemove(integrationBranch, out byte _);
            }
        }

        private static List<MergeEntry> StillTesting(List<MergeEntry> entries)
        {
            return entries.Where(e => e.Status == MergeStatusEnum.Testing).ToList();
        }

        // Something other than a failing check went wrong with the batch: land each remaining entry on its own, where
        // the failure is classified as it would be without batching. A Harbor that went away leaves them Queued.
        private async Task FallBackToSingleAsync(List<MergeEntry> entries, MergeQueueHost host, MergeQueueGroupState state, string reason, string integrationPath, string integrationBranch, bool worktreeCreated, CancellationToken token)
        {
            _Logging.Warn(_Header + "batch of " + entries.Count + " entries: " + reason + "; landing them one at a time");
            if (worktreeCreated) await CleanupWorktreeAsync(host, host.RepoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
            foreach (MergeEntry entry in entries)
            {
                if (HarborGone(host))
                {
                    await WaitForHarborAsync(entry, await HarborWaitReasonAsync(entry, host.HarborId!, host.RepoPath, token).ConfigureAwait(false), token).ConfigureAwait(false);
                    continue;
                }
                await ProcessResolvedEntryAsync(entry, host, state, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
