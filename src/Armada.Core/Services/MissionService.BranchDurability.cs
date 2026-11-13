namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Branch durability at completion: the mission branch in the dock's repository must hold the produced commit, so a
    /// landing, a retry or a dock repair can always rebuild the work from the branch even if the dock directory is lost.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Methods

        /// <summary>
        /// Check that the mission branch in the owning repository points at the produced commit, fast-forward it when the
        /// captain left the commit ahead of the branch (a detached HEAD, for example), and record the outcome as a
        /// mission.branch_verified or mission.branch_unverified event. Never pushes, so vessels without a remote and
        /// LandingMode None are unaffected. Best effort: a failure is recorded, not thrown.
        /// </summary>
        private async Task EnsureBranchHoldsCommitAsync(Mission mission, Dock dock, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.CommitHash) || String.IsNullOrEmpty(dock.WorktreePath)) return;
            string? branch = !String.IsNullOrEmpty(mission.BranchName) ? mission.BranchName : dock.BranchName;
            if (String.IsNullOrEmpty(branch)) return;

            DockHost? harborHost = HarborHostFor(dock);
            IGitService? git = harborHost != null ? harborHost.Git : _Git;
            if (git == null) return;

            string? repository = dock.RepositoryPath;
            if (String.IsNullOrEmpty(repository) && harborHost == null && !String.IsNullOrEmpty(mission.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                repository = vessel?.LocalPath;
            }
            if (String.IsNullOrEmpty(repository)) repository = dock.WorktreePath;

            string outcome;
            bool verified = false;
            try
            {
                string? tip = await BranchTipAsync(git, repository, branch, token).ConfigureAwait(false);
                if (String.Equals(tip, mission.CommitHash, StringComparison.OrdinalIgnoreCase))
                {
                    verified = true;
                    outcome = "Branch " + branch + " in " + repository + " holds the produced commit " + mission.CommitHash + ".";
                }
                else
                {
                    // Only a branch behind the commit is moved (a fast-forward); a missing branch, one that went elsewhere,
                    // or one whose tip cannot be read is left alone and reported.
                    bool behind = !String.IsNullOrEmpty(tip)
                        && await git.IsAncestorAsync(repository, tip, mission.CommitHash!, token).ConfigureAwait(false) == true;
                    bool advanced = behind
                        && await git.ForceAdvanceBranchAsync(dock.WorktreePath!, branch, mission.CommitHash!, token).ConfigureAwait(false);
                    string? after = advanced ? await BranchTipAsync(git, repository, branch, token).ConfigureAwait(false) : tip;
                    verified = String.Equals(after, mission.CommitHash, StringComparison.OrdinalIgnoreCase);
                    outcome = verified
                        ? "Branch " + branch + " in " + repository + " was at " + (tip ?? "nothing") + " and now holds the produced commit " + mission.CommitHash + "."
                        : "Branch " + branch + " in " + repository + " is at " + (after ?? "nothing") + ", not the produced commit " + mission.CommitHash
                            + "; the work exists only in the dock until the branch is updated.";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = "Could not confirm that branch " + branch + " in " + repository + " holds the produced commit " + mission.CommitHash + ": " + ex.Message;
            }

            if (!verified) _Logging.Warn(_Header + "mission " + mission.Id + ": " + outcome);
            try
            {
                ArmadaEvent evt = new ArmadaEvent(verified ? "mission.branch_verified" : "mission.branch_unverified", outcome);
                evt.TenantId = mission.TenantId;
                evt.UserId = mission.UserId;
                evt.EntityType = "mission";
                evt.EntityId = mission.Id;
                evt.MissionId = mission.Id;
                evt.CaptainId = mission.CaptainId;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "could not record the branch check for mission " + mission.Id + ": " + ex.Message);
            }
        }

        private static async Task<string?> BranchTipAsync(IGitService git, string repository, string branch, CancellationToken token)
        {
            if (!await git.BranchExistsAsync(repository, branch, token).ConfigureAwait(false)) return null;
            return await git.ResolveBranchTipAsync(repository, branch, token).ConfigureAwait(false);
        }

        #endregion
    }
}
