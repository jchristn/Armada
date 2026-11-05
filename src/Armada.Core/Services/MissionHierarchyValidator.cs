namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Checks a mission's <see cref="Mission.ParentMissionId"/> on create and update (D14, D15, D17): the parent is a
    /// visible mission in the same voyage (or, standalone, on the same vessel); a stage reference is normalized to its
    /// root; no cycle; depth within <c>Concurrency.MaxHierarchyDepth</c>; and a reparent only while the mission and its
    /// descendants are Pending. Sets <see cref="Mission.Depth"/> and recomputes descendant depths on reparent.
    /// </summary>
    public sealed class MissionHierarchyValidator
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public MissionHierarchyValidator(DatabaseDriver database, ArmadaSettings settings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>Validate and normalize the parent of a mission being created or updated.</summary>
        /// <param name="mission">Mission with its requested <see cref="Mission.ParentMissionId"/>.</param>
        /// <param name="isUpdate">True for an update of a stored mission, false for a create.</param>
        /// <param name="previousParentId">The stored parent before this update; ignored for a create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mission"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the parent violates the hierarchy rules.</exception>
        public async Task NormalizeAsync(Mission mission, bool isUpdate, string? previousParentId, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            bool update = isUpdate;
            bool changed = !update || !String.Equals(previousParentId ?? "", mission.ParentMissionId ?? "", StringComparison.Ordinal);
            if (String.IsNullOrEmpty(mission.ParentMissionId))
            {
                mission.ParentMissionId = null;
                if (changed && update) await RequirePendingSubtreeAsync(mission, token).ConfigureAwait(false);
                await SetDepthAsync(mission, 0, update && changed, token).ConfigureAwait(false);
                return;
            }
            if (!changed && update) return;
            if (!String.IsNullOrEmpty(mission.DependsOnMissionId) && !await IsStageOfAsync(mission, token).ConfigureAwait(false))
                throw new ArgumentException("A mission cannot have both ParentMissionId and DependsOnMissionId; the parent link already orders it after its parent.", nameof(mission));

            Mission? parent = await ReadVisibleAsync(mission.TenantId, mission.ParentMissionId!, token).ConfigureAwait(false)
                ?? throw new ArgumentException("Parent mission " + mission.ParentMissionId + " was not found.", nameof(mission));
            for (int hops = 0; !String.IsNullOrEmpty(parent.DependsOnMissionId) && hops < 64; hops++)
                parent = await ReadVisibleAsync(mission.TenantId, parent.DependsOnMissionId!, token).ConfigureAwait(false)
                    ?? throw new ArgumentException("Parent mission's pipeline root was not found.", nameof(mission));
            if (!String.IsNullOrEmpty(mission.VoyageId) || !String.IsNullOrEmpty(parent.VoyageId))
            {
                if (!String.Equals(mission.VoyageId, parent.VoyageId, StringComparison.Ordinal))
                    throw new ArgumentException("A parent must be in the same voyage as its child.", nameof(mission));
            }
            else if (!String.Equals(mission.VesselId, parent.VesselId, StringComparison.Ordinal))
                throw new ArgumentException("A standalone mission's parent must be a standalone mission on the same vessel.", nameof(mission));
            if (update)
            {
                for (Mission? ancestor = parent; ancestor != null; ancestor = String.IsNullOrEmpty(ancestor.ParentMissionId) ? null
                    : await ReadVisibleAsync(mission.TenantId, ancestor.ParentMissionId!, token).ConfigureAwait(false))
                    if (ancestor.Id == mission.Id) throw new ArgumentException("That parent would make a cycle: it is this mission or one of its descendants.", nameof(mission));
                await RequirePendingSubtreeAsync(mission, token).ConfigureAwait(false);
            }
            int depth = parent.Depth + 1;
            int max = _Settings.Concurrency.MaxHierarchyDepth;
            if (depth > max) throw new ArgumentException("This mission would be at hierarchy depth " + depth + "; the maximum is " + max + " (Concurrency.MaxHierarchyDepth).", nameof(mission));
            mission.ParentMissionId = parent.Id;
            await SetDepthAsync(mission, depth, update, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Descendants that are not terminal (Complete, Failed, Cancelled or LandingFailed). D17: a parent with any of these
        /// cannot be purged unless the request cascades.
        /// </summary>
        /// <param name="mission">Parent mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The unfinished descendants, nearest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mission"/> is null.</exception>
        public async Task<List<Mission>> UnfinishedDescendantsAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return (await DescendantsAsync(mission, token).ConfigureAwait(false))
                .Where(m => m.Status != MissionStatusEnum.Complete && m.Status != MissionStatusEnum.Failed
                    && m.Status != MissionStatusEnum.Cancelled && m.Status != MissionStatusEnum.LandingFailed).ToList();
        }

        /// <summary>Every descendant of a mission (children, their children, and so on), nearest first.</summary>
        /// <param name="mission">Parent mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Descendants.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mission"/> is null.</exception>
        public Task<List<Mission>> AllDescendantsAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return DescendantsAsync(mission, token);
        }

        #endregion

        #region Private-Methods

        private async Task<Mission?> ReadVisibleAsync(string? tenantId, string id, CancellationToken token)
            => String.IsNullOrEmpty(tenantId)
                ? await _Database.Missions.ReadAsync(id, token).ConfigureAwait(false)
                : await _Database.Missions.ReadAsync(tenantId!, id, token).ConfigureAwait(false);

        // A pipeline stage legitimately carries its root's ParentMissionId alongside its DependsOn link (D14).
        private async Task<bool> IsStageOfAsync(Mission mission, CancellationToken token)
        {
            Mission? upstream = await ReadVisibleAsync(mission.TenantId, mission.DependsOnMissionId!, token).ConfigureAwait(false);
            return upstream != null && String.Equals(upstream.ParentMissionId, mission.ParentMissionId, StringComparison.Ordinal);
        }

        private async Task RequirePendingSubtreeAsync(Mission mission, CancellationToken token)
        {
            if (mission.Status != MissionStatusEnum.Pending)
                throw new ArgumentException("A mission's parent can only change while it is Pending (current: " + mission.Status + ").", nameof(mission));
            foreach (Mission descendant in await DescendantsAsync(mission, token).ConfigureAwait(false))
                if (descendant.Status != MissionStatusEnum.Pending)
                    throw new ArgumentException("A mission's parent can only change while all of its descendants are Pending; '" + descendant.Title + "' is " + descendant.Status + ".", nameof(mission));
        }

        private async Task SetDepthAsync(Mission mission, int depth, bool cascade, CancellationToken token)
        {
            int delta = depth - mission.Depth;
            mission.Depth = depth;
            if (!cascade || delta == 0) return;
            int max = _Settings.Concurrency.MaxHierarchyDepth;
            List<Mission> descendants = await DescendantsAsync(mission, token).ConfigureAwait(false);
            foreach (Mission descendant in descendants)
                if (descendant.Depth + delta > max)
                    throw new ArgumentException("Moving this mission would put '" + descendant.Title + "' at depth " + (descendant.Depth + delta) + "; the maximum is " + max + ".", nameof(mission));
            foreach (Mission descendant in descendants)
            {
                descendant.Depth += delta;
                descendant.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(descendant, token).ConfigureAwait(false);
            }
        }

        private async Task<List<Mission>> DescendantsAsync(Mission root, CancellationToken token)
        {
            List<Mission> scope = !String.IsNullOrEmpty(root.VoyageId)
                ? await _Database.Missions.EnumerateByVoyageAsync(root.VoyageId!, token).ConfigureAwait(false)
                : String.IsNullOrEmpty(root.VesselId) ? new List<Mission>() : await _Database.Missions.EnumerateByVesselAsync(root.VesselId!, token).ConfigureAwait(false);
            List<Mission> result = new List<Mission>();
            Queue<string> parents = new Queue<string>();
            parents.Enqueue(root.Id);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { root.Id };
            while (parents.Count > 0)
            {
                string parentId = parents.Dequeue();
                foreach (Mission child in scope.Where(m => m.ParentMissionId == parentId && seen.Add(m.Id)))
                {
                    result.Add(child);
                    parents.Enqueue(child.Id);
                }
            }
            return result;
        }

        #endregion
    }
}
