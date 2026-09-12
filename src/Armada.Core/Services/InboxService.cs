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
    using SyslogLogging;

    /// <summary>
    /// Builds the operator's inbox: a single consolidated list of items across the fleet that require a
    /// human's attention or action, ordered most-urgent first. Two kinds of item qualify:
    ///
    /// - Awaiting your decision (human-in-the-loop): a mission in Review, or a deployment pending approval.
    /// - Failed and needs intervention (autonomous work that could not finish on its own): a failed
    ///   mission, a mission whose work could not land, a failed merge, a failed or verification-failed
    ///   deployment, or a stalled captain.
    ///
    /// Purely informational state changes (completions, normal progress) are deliberately excluded --
    /// the inbox answers "is there anything waiting on me / that needs my attention?", not "what happened?".
    /// </summary>
    public class InboxService
    {
        #region Private-Members

        private readonly string _Header = "[InboxService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private const int _MaxPerCategory = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver. Required.</param>
        /// <param name="logging">Logging module. Required.</param>
        public InboxService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the inbox: actionable items ordered most-urgent first.
        /// </summary>
        /// <param name="auth">Caller authentication context, used to scope inbox items to the caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The list of inbox items.</returns>
        public async Task<List<InboxItem>> GetInboxAsync(AuthContext auth, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            List<InboxItem> items = new List<InboxItem>();

            try
            {
                List<Mission> reviews = await MissionsByStatusAsync(auth, MissionStatusEnum.Review, token).ConfigureAwait(false);
                foreach (Mission mission in reviews.Take(_MaxPerCategory))
                {
                    bool overdue = mission.ReviewDeadlineUtc.HasValue && mission.ReviewDeadlineUtc.Value < DateTime.UtcNow;
                    items.Add(new InboxItem
                    {
                        Kind = "review",
                        Severity = overdue ? InboxSeverityEnum.Critical : InboxSeverityEnum.Warning,
                        Title = "Review: " + mission.Title,
                        Detail = overdue ? "Review is overdue -- awaiting your approval." : "Awaiting your review.",
                        EntityType = "mission",
                        EntityId = mission.Id,
                        Href = "/missions/" + mission.Id
                    });
                }

                List<Mission> landingFailed = await MissionsByStatusAsync(auth, MissionStatusEnum.LandingFailed, token).ConfigureAwait(false);
                foreach (Mission mission in landingFailed.Take(_MaxPerCategory))
                {
                    items.Add(new InboxItem
                    {
                        Kind = "landing_failed",
                        Severity = InboxSeverityEnum.Critical,
                        Title = "Landing failed: " + mission.Title,
                        Detail = String.IsNullOrWhiteSpace(mission.FailureReason) ? "The work could not be landed." : mission.FailureReason!,
                        EntityType = "mission",
                        EntityId = mission.Id,
                        Href = "/missions/" + mission.Id
                    });
                }

                List<Mission> failed = await MissionsByStatusAsync(auth, MissionStatusEnum.Failed, token).ConfigureAwait(false);
                foreach (Mission mission in failed.Take(_MaxPerCategory))
                {
                    items.Add(new InboxItem
                    {
                        Kind = "failed",
                        Severity = InboxSeverityEnum.Warning,
                        Title = "Failed: " + mission.Title,
                        Detail = String.IsNullOrWhiteSpace(mission.FailureReason) ? "The mission failed." : mission.FailureReason!,
                        EntityType = "mission",
                        EntityId = mission.Id,
                        Href = "/missions/" + mission.Id
                    });
                }

                List<Captain> stalled = await CaptainsByStateAsync(auth, CaptainStateEnum.Stalled, token).ConfigureAwait(false);
                foreach (Captain captain in stalled.Take(_MaxPerCategory))
                {
                    items.Add(new InboxItem
                    {
                        Kind = "stalled_captain",
                        Severity = InboxSeverityEnum.Warning,
                        Title = "Stalled captain: " + captain.Name,
                        Detail = "This captain is stalled and may need recovery or a dock reclaim.",
                        EntityType = "captain",
                        EntityId = captain.Id,
                        Href = "/captains/" + captain.Id
                    });
                }

                List<MergeEntry> mergeFailed = await MergeEntriesByStatusAsync(auth, MergeStatusEnum.Failed, token).ConfigureAwait(false);
                foreach (MergeEntry entry in mergeFailed.Take(_MaxPerCategory))
                {
                    items.Add(new InboxItem
                    {
                        Kind = "merge_failed",
                        Severity = InboxSeverityEnum.Critical,
                        Title = "Merge failed: " + entry.TargetBranch,
                        Detail = "A queued merge failed testing or landing and needs attention.",
                        EntityType = "merge_entry",
                        EntityId = entry.Id,
                        Href = "/merge-queue/" + entry.Id
                    });
                }

                List<Deployment> deployments = ScopeList(auth,
                    await _Database.Deployments.EnumerateAllAsync(new DeploymentQuery(), token).ConfigureAwait(false),
                    d => d.TenantId, d => d.UserId);

                foreach (Deployment deployment in deployments.Where(d => d.Status == DeploymentStatusEnum.PendingApproval).Take(_MaxPerCategory))
                {
                    items.Add(new InboxItem
                    {
                        Kind = "deployment_approval",
                        Severity = InboxSeverityEnum.Warning,
                        Title = "Deployment awaiting approval: " + (String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!),
                        Detail = "A deployment is waiting for your approval before it runs.",
                        EntityType = "deployment",
                        EntityId = deployment.Id,
                        Href = "/deployments/" + deployment.Id
                    });
                }

                foreach (Deployment deployment in deployments.Where(d => d.Status == DeploymentStatusEnum.Failed || d.Status == DeploymentStatusEnum.VerificationFailed).Take(_MaxPerCategory))
                {
                    items.Add(new InboxItem
                    {
                        Kind = "deployment_failed",
                        Severity = InboxSeverityEnum.Critical,
                        Title = "Deployment failed: " + (String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!),
                        Detail = deployment.Status == DeploymentStatusEnum.VerificationFailed
                            ? "Deployment verification failed and needs attention."
                            : "A deployment failed and needs attention.",
                        EntityType = "deployment",
                        EntityId = deployment.Id,
                        Href = "/deployments/" + deployment.Id
                    });
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error building inbox: " + ex.ToString());
            }

            return items
                .OrderByDescending(item => (int)item.Severity)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        #endregion

        #region Private-Methods

        private async Task<List<Mission>> MissionsByStatusAsync(AuthContext auth, MissionStatusEnum status, CancellationToken token)
        {
            if (auth.IsAdmin) return await _Database.Missions.EnumerateByStatusAsync(status, token).ConfigureAwait(false);
            List<Mission> list = await _Database.Missions.EnumerateByStatusAsync(auth.TenantId!, status, token).ConfigureAwait(false);
            return auth.IsTenantAdmin ? list : list.Where(m => String.Equals(m.UserId, auth.UserId, StringComparison.Ordinal)).ToList();
        }

        private async Task<List<Captain>> CaptainsByStateAsync(AuthContext auth, CaptainStateEnum state, CancellationToken token)
        {
            if (auth.IsAdmin) return await _Database.Captains.EnumerateByStateAsync(state, token).ConfigureAwait(false);
            List<Captain> list = await _Database.Captains.EnumerateByStateAsync(auth.TenantId!, state, token).ConfigureAwait(false);
            return auth.IsTenantAdmin ? list : list.Where(c => String.Equals(c.UserId, auth.UserId, StringComparison.Ordinal)).ToList();
        }

        private async Task<List<MergeEntry>> MergeEntriesByStatusAsync(AuthContext auth, MergeStatusEnum status, CancellationToken token)
        {
            if (auth.IsAdmin) return await _Database.MergeEntries.EnumerateByStatusAsync(status, token).ConfigureAwait(false);
            List<MergeEntry> list = await _Database.MergeEntries.EnumerateByStatusAsync(auth.TenantId!, status, token).ConfigureAwait(false);
            return auth.IsTenantAdmin ? list : list.Where(e => String.Equals(e.UserId, auth.UserId, StringComparison.Ordinal)).ToList();
        }

        /// <summary>
        /// Apply three-tier scoping to an in-memory list: a global admin sees all, a tenant admin sees the
        /// tenant, and a regular user sees only their own rows.
        /// </summary>
        private static List<T> ScopeList<T>(AuthContext auth, List<T> list, Func<T, string?> tenantOf, Func<T, string?> userOf)
        {
            if (auth.IsAdmin) return list;
            List<T> scoped = list.Where(x => String.Equals(tenantOf(x), auth.TenantId, StringComparison.Ordinal)).ToList();
            if (auth.IsTenantAdmin) return scoped;
            return scoped.Where(x => String.Equals(userOf(x), auth.UserId, StringComparison.Ordinal)).ToList();
        }

        #endregion
    }
}
