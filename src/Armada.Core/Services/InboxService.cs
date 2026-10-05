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
    /// - Awaiting your decision (human-in-the-loop): a mission in Review, a deployment pending approval, or an
    ///   Ask Armada action proposal pending approval in one of your conversations.
    /// - Failed and needs intervention (autonomous work that could not finish on its own): a failed
    ///   mission, a mission whose work could not land, a failed merge, a failed or verification-failed
    ///   deployment, or a stalled captain.
    ///
    /// Purely informational state changes (completions, normal progress) are deliberately excluded --
    /// the inbox answers "is there anything waiting on me / that needs my attention?", not "what happened?".
    /// </summary>
    public class InboxService
    {
        #region Public-Members

        /// <summary>
        /// Age in minutes after which a still-Pending Ask proposal is treated as expired and left out (the
        /// Ask settings' ProposalExpiryMinutes). 0 disables the age check. Clamped to [0, 1440].
        /// </summary>
        public int AskProposalExpiryMinutes
        {
            get => _AskProposalExpiryMinutes;
            set => _AskProposalExpiryMinutes = value < 0 ? 0 : (value > 1440 ? 1440 : value);
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[InboxService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private const int _MaxPerCategory = 100;
        private int _AskProposalExpiryMinutes = 0;

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
                        Kind = InboxItemKinds.Review,
                        Severity = overdue ? InboxSeverityEnum.Critical : InboxSeverityEnum.Warning,
                        Title = "Review: " + mission.Title,
                        EntityName = mission.Title,
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
                        Kind = InboxItemKinds.LandingFailed,
                        Severity = InboxSeverityEnum.Critical,
                        Title = "Landing failed: " + mission.Title,
                        EntityName = mission.Title,
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
                        Kind = InboxItemKinds.Failed,
                        Severity = InboxSeverityEnum.Warning,
                        Title = "Failed: " + mission.Title,
                        EntityName = mission.Title,
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
                        Kind = InboxItemKinds.StalledCaptain,
                        Severity = InboxSeverityEnum.Warning,
                        Title = "Stalled captain: " + captain.Name,
                        EntityName = captain.Name,
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
                        Kind = InboxItemKinds.MergeFailed,
                        Severity = InboxSeverityEnum.Critical,
                        Title = "Merge failed: " + entry.TargetBranch,
                        EntityName = entry.TargetBranch,
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
                        Kind = InboxItemKinds.DeploymentApproval,
                        Severity = InboxSeverityEnum.Warning,
                        Title = "Deployment awaiting approval: " + (String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!),
                        EntityName = String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!,
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
                        Kind = InboxItemKinds.DeploymentFailed,
                        Severity = InboxSeverityEnum.Critical,
                        Title = "Deployment failed: " + (String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!),
                        EntityName = String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!,
                        Detail = deployment.Status == DeploymentStatusEnum.VerificationFailed
                            ? "Deployment verification failed and needs attention."
                            : "A deployment failed and needs attention.",
                        EntityType = "deployment",
                        EntityId = deployment.Id,
                        Href = "/deployments/" + deployment.Id
                    });
                }

                foreach (AskActionProposal proposal in (await PendingAskProposalsAsync(auth, token).ConfigureAwait(false)).Take(_MaxPerCategory))
                {
                    string summary = String.IsNullOrWhiteSpace(proposal.SummaryText) ? proposal.ToolName : proposal.SummaryText;
                    items.Add(new InboxItem
                    {
                        Kind = InboxItemKinds.AskProposal,
                        Severity = InboxSeverityEnum.Warning,
                        Title = "Ask approval: " + summary,
                        EntityName = summary,
                        Detail = "Armada proposed an action in an Ask conversation and is waiting for you to approve or reject it.",
                        EntityType = "ask_proposal",
                        EntityId = proposal.Id,
                        Href = "/ask/" + proposal.ThreadId
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
        /// Pending, unexpired Ask proposals in the caller's own conversations. Ask threads belong to one user, and only
        /// the thread owner can approve, so admins are not shown other users' proposals.
        /// </summary>
        private async Task<List<AskActionProposal>> PendingAskProposalsAsync(AuthContext auth, CancellationToken token)
        {
            if (String.IsNullOrEmpty(auth.UserId)) return new List<AskActionProposal>();
            DateTime now = DateTime.UtcNow;
            List<AskActionProposal> pending = await _Database.AskActionProposals.EnumeratePendingBeforeAsync(now.AddDays(1), token).ConfigureAwait(false);
            return pending
                .Where(p => String.Equals(p.TenantId, auth.TenantId, StringComparison.Ordinal)
                    && String.Equals(p.UserId, auth.UserId, StringComparison.Ordinal)
                    && (!p.ExpiresUtc.HasValue || p.ExpiresUtc.Value > now)
                    && (_AskProposalExpiryMinutes == 0 || p.CreatedUtc.AddMinutes(_AskProposalExpiryMinutes) >= now))
                .OrderBy(p => p.CreatedUtc)
                .ToList();
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
