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
    using SyslogLogging;

    /// <summary>
    /// Builds the operator's inbox: a single consolidated list of items across the fleet that require a
    /// human's attention or action, read one page at a time (see <see cref="EnumerateAsync"/>). Two kinds of item qualify:
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
        /// Ask settings' ProposalExpiryMinutes). 0 disables the age check. Clamped to [0, 1440]. When the service was
        /// built with settings, this reads the current <see cref="AskSettings.ProposalExpiryMinutes"/> on every call;
        /// assigning a value replaces that binding with the fixed value.
        /// </summary>
        public int AskProposalExpiryMinutes
        {
            get => _Settings != null ? Clamp(_Settings.Ask.ProposalExpiryMinutes) : _AskProposalExpiryMinutes;
            set
            {
                _Settings = null;
                _AskProposalExpiryMinutes = Clamp(value);
            }
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[InboxService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        // Pending CLI permission requests come from a query capped at 1000 (CliPermissionRequestQuery.Limit). They expire
        // within minutes, so that many waiting at once means something else is wrong; every other category is complete.
        private const int _MaxPendingCliPermissions = 1000;
        private int _AskProposalExpiryMinutes = 0;
        private ArmadaSettings? _Settings = null;

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

        /// <summary>
        /// Instantiate with the Admiral settings, so Ask proposals older than the configured
        /// <see cref="AskSettings.ProposalExpiryMinutes"/> are left out. Every surface that serves the inbox (REST and
        /// MCP) builds the service this way so they apply the same expiry.
        /// </summary>
        /// <param name="database">Database driver. Required.</param>
        /// <param name="logging">Logging module. Required.</param>
        /// <param name="settings">Admiral settings; null leaves the age check disabled.</param>
        public InboxService(DatabaseDriver database, LoggingModule logging, ArmadaSettings? settings)
            : this(database, logging)
        {
            _Settings = settings;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Kinds in the order the inbox lists them: decisions and critical failures first, then the other items. Within a
        /// kind, items are newest first.
        /// </summary>
        public static readonly IReadOnlyList<string> KindOrder = new List<string>
        {
            InboxItemKinds.Review,
            InboxItemKinds.LandingFailed,
            InboxItemKinds.MergeFailed,
            InboxItemKinds.DeploymentFailed,
            InboxItemKinds.DeploymentApproval,
            InboxItemKinds.AskProposal,
            InboxItemKinds.CliPermission,
            InboxItemKinds.Failed,
            InboxItemKinds.StalledCaptain,
            InboxItemKinds.ModelAlert
        };

        /// <summary>
        /// Read one page of the inbox. Each kind is read from its own source one page at a time (missions, merge entries,
        /// and deployments are paged and counted in the database under the caller's scope), and the kinds follow
        /// <see cref="KindOrder"/>, newest first within a kind. TotalRecords is the exact count across kinds.
        /// </summary>
        /// <param name="auth">Caller authentication context, used to scope inbox items to the caller.</param>
        /// <param name="query">Paging query (page number and size).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of inbox items.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task<EnumerationResult<InboxItem>> EnumerateAsync(AuthContext auth, EnumerationQuery query, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (query == null) throw new ArgumentNullException(nameof(query));
            return await SectionedPaging.PageAsync(query, BuildSections(auth, null), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Count the caller's inbox items of the given kinds without reading them (each source is counted with a
        /// one-row page).
        /// </summary>
        /// <param name="auth">Caller authentication context.</param>
        /// <param name="kinds">Kinds to count (InboxItemKinds values).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task<long> CountAsync(AuthContext auth, IEnumerable<string> kinds, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (kinds == null) throw new ArgumentNullException(nameof(kinds));
            EnumerationQuery probe = new EnumerationQuery { PageNumber = 1, PageSize = CollectionLimits.MinPageSize };
            EnumerationResult<InboxItem> page = await SectionedPaging.PageAsync(probe, BuildSections(auth, new HashSet<string>(kinds, StringComparer.Ordinal)), token).ConfigureAwait(false);
            return page.TotalRecords;
        }

        #endregion

        #region Private-Methods

        private List<PagedSection<InboxItem>> BuildSections(AuthContext auth, HashSet<string>? kinds)
        {
            List<PagedSection<InboxItem>> sections = new List<PagedSection<InboxItem>>();
            foreach (string kind in KindOrder)
            {
                if (kinds != null && !kinds.Contains(kind)) continue;
                switch (kind)
                {
                    case InboxItemKinds.Review:
                        sections.Add(MissionSection(auth, kind, MissionStatusEnum.Review, ReviewItemAsync));
                        break;
                    case InboxItemKinds.LandingFailed:
                        sections.Add(MissionSection(auth, kind, MissionStatusEnum.LandingFailed, (m, t) => Task.FromResult(LandingFailedItem(m))));
                        break;
                    case InboxItemKinds.Failed:
                        // An operator's own stop needs no attention (older stops were recorded as Failed); left out in
                        // the data source so the section's count stays exact.
                        sections.Add(MissionSection(auth, kind, MissionStatusEnum.Failed, (m, t) => Task.FromResult(FailedItem(m)), MissionFailureKindEnum.OperatorAction));
                        break;
                    case InboxItemKinds.MergeFailed:
                        sections.Add(MergeFailedSection(auth));
                        break;
                    case InboxItemKinds.DeploymentFailed:
                        sections.Add(DeploymentSection(auth, kind, DeploymentStatusEnum.Failed));
                        sections.Add(DeploymentSection(auth, kind, DeploymentStatusEnum.VerificationFailed));
                        break;
                    case InboxItemKinds.DeploymentApproval:
                        sections.Add(DeploymentSection(auth, kind, DeploymentStatusEnum.PendingApproval));
                        break;
                    case InboxItemKinds.AskProposal:
                        sections.Add(MemorySection(kind, async t => (await PendingAskProposalsAsync(auth, t).ConfigureAwait(false)).Select(AskProposalItem).ToList()));
                        break;
                    case InboxItemKinds.CliPermission:
                        sections.Add(MemorySection(kind, async t => (await PendingCliPermissionsAsync(auth, t).ConfigureAwait(false)).Select(r => CliPermissionItem(auth, r)).ToList()));
                        break;
                    case InboxItemKinds.ModelAlert:
                        sections.Add(ModelAlertSection(auth));
                        break;
                    case InboxItemKinds.StalledCaptain:
                        // Captains are a registered set (not operational rows that accumulate), so the stalled ones are read
                        // together and paged here.
                        sections.Add(MemorySection(kind, async t => (await CaptainsByStateAsync(auth, CaptainStateEnum.Stalled, t).ConfigureAwait(false))
                            .OrderByDescending(c => c.LastUpdateUtc).ThenBy(c => c.Id, StringComparer.Ordinal).Select(StalledCaptainItem).ToList()));
                        break;
                }
            }

            return sections;
        }

        private PagedSection<InboxItem> MissionSection(AuthContext auth, string kind, MissionStatusEnum status, Func<Mission, CancellationToken, Task<InboxItem>> map, MissionFailureKindEnum? excludeFailureKind = null)
        {
            return Guarded(kind, async (pageNumber, pageSize, token) =>
            {
                EnumerationQuery query = new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize, Status = status.ToString(), ExcludeFailureKind = excludeFailureKind };
                EnumerationResult<Mission> page = await EnumerationScope.EnumerateScopedAsync(auth, query,
                    q => _Database.Missions.EnumerateAsync(q, token),
                    (t, q) => _Database.Missions.EnumerateAsync(t, q, token),
                    (t, u, q) => _Database.Missions.EnumerateAsync(t, u, q, token)).ConfigureAwait(false);
                List<InboxItem> items = new List<InboxItem>();
                foreach (Mission mission in page.Objects) items.Add(await map(mission, token).ConfigureAwait(false));
                return EnumerationResult<InboxItem>.Create(query, items, page.TotalRecords);
            });
        }

        private PagedSection<InboxItem> MergeFailedSection(AuthContext auth)
        {
            return Guarded(InboxItemKinds.MergeFailed, async (pageNumber, pageSize, token) =>
            {
                EnumerationQuery query = new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize, Status = MergeStatusEnum.Failed.ToString() };
                EnumerationResult<MergeEntry> page = await EnumerationScope.EnumerateScopedAsync(auth, query,
                    q => _Database.MergeEntries.EnumerateAsync(q, token),
                    (t, q) => _Database.MergeEntries.EnumerateAsync(t, q, token),
                    (t, u, q) => _Database.MergeEntries.EnumerateAsync(t, u, q, token)).ConfigureAwait(false);
                List<InboxItem> items = new List<InboxItem>();
                foreach (MergeEntry entry in page.Objects)
                {
                    string? missionTitle = await MergeEntryMissionTitleAsync(entry, token).ConfigureAwait(false);
                    items.Add(new InboxItem
                    {
                        Kind = InboxItemKinds.MergeFailed,
                        Severity = InboxSeverityEnum.Critical,
                        Title = "Merge failed: " + (missionTitle ?? entry.BranchName) + " -> " + entry.TargetBranch,
                        EntityName = entry.TargetBranch,
                        Detail = MergeFailedDetail(entry, missionTitle),
                        EntityType = "merge_entry",
                        EntityId = entry.Id,
                        Href = "/merge-queue/" + entry.Id
                    });
                }

                return EnumerationResult<InboxItem>.Create(query, items, page.TotalRecords);
            });
        }

        private PagedSection<InboxItem> DeploymentSection(AuthContext auth, string kind, DeploymentStatusEnum status)
        {
            return Guarded(kind + ":" + status, async (pageNumber, pageSize, token) =>
            {
                DeploymentQuery query = new DeploymentQuery { Status = status, PageNumber = pageNumber, PageSize = pageSize };
                if (!auth.IsAdmin)
                {
                    query.TenantId = auth.TenantId;
                    if (!auth.IsTenantAdmin) query.UserId = auth.UserId;
                }

                EnumerationResult<Deployment> page = await _Database.Deployments.EnumerateAsync(query, token).ConfigureAwait(false);
                List<InboxItem> items = page.Objects.Select(d => status == DeploymentStatusEnum.PendingApproval ? DeploymentApprovalItem(d) : DeploymentFailedItem(d)).ToList();
                return EnumerationResult<InboxItem>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, items, page.TotalRecords);
            });
        }

        private PagedSection<InboxItem> ModelAlertSection(AuthContext auth)
        {
            // Model alerts are advisory and only an admin can apply their suggested actions, so only admins see them.
            return Guarded(InboxItemKinds.ModelAlert, async (pageNumber, pageSize, token) =>
            {
                EnumerationQuery paging = new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize };
                if (!auth.IsAdmin && !(auth.IsTenantAdmin && !String.IsNullOrEmpty(auth.TenantId)))
                    return EnumerationResult<InboxItem>.Create(paging, new List<InboxItem>(), 0);
                DateTime now = DateTime.UtcNow;
                ModelAlertQuery query = new ModelAlertQuery
                {
                    TenantId = auth.IsAdmin ? null : auth.TenantId,
                    Status = ModelAlertStatusEnum.Open,
                    NotIgnoredAtUtc = now,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                EnumerationResult<ModelAlert> page = await _Database.ModelAlerts.EnumerateAsync(query, token).ConfigureAwait(false);
                ModelAlertActionPlanner planner = new ModelAlertActionPlanner(_Database);
                int ignoreDays = _Settings?.ModelAlerts.DefaultIgnoreDays ?? 7;
                List<InboxItem> items = new List<InboxItem>();
                foreach (ModelAlert alert in page.Objects)
                {
                    alert.SuggestedActions = (await planner.PlanAsync(alert, auth, ignoreDays, token).ConfigureAwait(false)).Actions;
                    items.Add(ModelAlertItem(alert));
                }
                return EnumerationResult<InboxItem>.Create(paging, items, page.TotalRecords);
            });
        }

        private static InboxItem ModelAlertItem(ModelAlert alert)
        {
            return new InboxItem
            {
                Kind = InboxItemKinds.ModelAlert,
                Severity = InboxSeverityEnum.Info,
                Title = alert.Title,
                EntityName = String.IsNullOrWhiteSpace(alert.Model) ? alert.ProfileId : alert.Model,
                Detail = alert.Detail,
                EntityType = "model_alert",
                EntityId = alert.Id,
                Href = "/inbox?modelAlert=" + alert.Id,
                ModelAlert = alert
            };
        }

        private PagedSection<InboxItem> MemorySection(string kind, Func<CancellationToken, Task<List<InboxItem>>> load)
        {
            List<InboxItem>? loaded = null;
            return Guarded(kind, async (pageNumber, pageSize, token) =>
            {
                loaded ??= await load(token).ConfigureAwait(false);
                return EnumerationResult<InboxItem>.Page(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, loaded);
            });
        }

        private PagedSection<InboxItem> Guarded(string name, PageFetchDelegate<InboxItem> fetch)
        {
            // One failing source leaves its kind out of the inbox (with a warning) instead of failing the whole read.
            return new PagedSection<InboxItem>(name, async (pageNumber, pageSize, token) =>
            {
                try
                {
                    return await fetch(pageNumber, pageSize, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _Logging.Warn(_Header + "error reading inbox source " + name + ": " + ex.ToString());
                    return EnumerationResult<InboxItem>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, new List<InboxItem>(), 0);
                }
            });
        }

        private async Task<InboxItem> ReviewItemAsync(Mission mission, CancellationToken token)
        {
            Voyage? approvalVoyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
            RunApprovalOptions? approvalOptions = approvalVoyage?.ApprovalOptions;
            bool overdue = mission.ReviewDeadlineUtc.HasValue && mission.ReviewDeadlineUtc.Value < DateTime.UtcNow;
            return new InboxItem
            {
                Kind = InboxItemKinds.Review,
                ExpiresUtc = approvalOptions == null ? null : mission.ReviewDeadlineUtc,
                TimeoutDecision = approvalOptions?.TimeoutDecision,
                Severity = overdue ? InboxSeverityEnum.Critical : InboxSeverityEnum.Warning,
                Title = "Review: " + mission.Title,
                EntityName = mission.Title,
                Detail = overdue ? "Review is overdue -- awaiting your approval." : "Awaiting your review.",
                EntityType = "mission",
                EntityId = mission.Id,
                Href = "/missions/" + mission.Id
            };
        }

        private static InboxItem LandingFailedItem(Mission mission)
        {
            return new InboxItem
            {
                Kind = InboxItemKinds.LandingFailed,
                Severity = InboxSeverityEnum.Critical,
                Title = "Landing failed: " + mission.Title,
                EntityName = mission.Title,
                Detail = String.IsNullOrWhiteSpace(mission.FailureReason) ? "The work could not be landed." : mission.FailureReason!,
                EntityType = "mission",
                EntityId = mission.Id,
                Href = "/missions/" + mission.Id
            };
        }

        private static InboxItem FailedItem(Mission mission)
        {
            return new InboxItem
            {
                Kind = InboxItemKinds.Failed,
                Severity = InboxSeverityEnum.Warning,
                Title = "Failed: " + mission.Title,
                EntityName = mission.Title,
                Detail = String.IsNullOrWhiteSpace(mission.FailureReason) ? "The mission failed." : mission.FailureReason!,
                EntityType = "mission",
                EntityId = mission.Id,
                Href = "/missions/" + mission.Id
            };
        }

        private static InboxItem StalledCaptainItem(Captain captain)
        {
            return new InboxItem
            {
                Kind = InboxItemKinds.StalledCaptain,
                Severity = InboxSeverityEnum.Warning,
                Title = "Stalled captain: " + captain.Name,
                EntityName = captain.Name,
                Detail = "This captain is stalled and may need recovery or a dock reclaim.",
                EntityType = "captain",
                EntityId = captain.Id,
                Href = "/captains/" + captain.Id
            };
        }

        private static InboxItem DeploymentApprovalItem(Deployment deployment)
        {
            return new InboxItem
            {
                Kind = InboxItemKinds.DeploymentApproval,
                Severity = InboxSeverityEnum.Warning,
                Title = DeploymentApprovalLabel.Format(deployment.EnvironmentName, deployment.Title, deployment.Id),
                EntityName = String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!,
                EnvironmentName = String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? null : deployment.EnvironmentName,
                DeploymentTitle = String.IsNullOrWhiteSpace(deployment.Title) ? null : deployment.Title,
                Detail = "A deployment is waiting for your approval before it runs.",
                EntityType = "deployment",
                EntityId = deployment.Id,
                Href = "/deployments/" + deployment.Id
            };
        }

        private static InboxItem DeploymentFailedItem(Deployment deployment)
        {
            return new InboxItem
            {
                Kind = InboxItemKinds.DeploymentFailed,
                Severity = InboxSeverityEnum.Critical,
                Title = "Deployment failed: " + (String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!),
                EntityName = String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.Id : deployment.EnvironmentName!,
                EnvironmentName = String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? null : deployment.EnvironmentName,
                DeploymentTitle = String.IsNullOrWhiteSpace(deployment.Title) ? null : deployment.Title,
                Detail = deployment.Status == DeploymentStatusEnum.VerificationFailed
                    ? "Deployment verification failed and needs attention."
                    : "A deployment failed and needs attention.",
                EntityType = "deployment",
                EntityId = deployment.Id,
                Href = "/deployments/" + deployment.Id
            };
        }

        private static InboxItem AskProposalItem(AskActionProposal proposal)
        {
            string summary = String.IsNullOrWhiteSpace(proposal.SummaryText) ? proposal.ToolName : proposal.SummaryText;
            return new InboxItem
            {
                Kind = InboxItemKinds.AskProposal,
                Severity = InboxSeverityEnum.Warning,
                Title = "Ask approval: " + summary,
                EntityName = summary,
                Detail = "Armada proposed an action in an Ask conversation and is waiting for you to approve or reject it.",
                EntityType = "ask_proposal",
                EntityId = proposal.Id,
                Href = "/ask/" + proposal.ThreadId
            };
        }

        private static InboxItem CliPermissionItem(AuthContext auth, CliPermissionRequest request)
        {
            string where = !String.IsNullOrEmpty(request.MissionTitle) ? "mission " + request.MissionTitle
                : (!String.IsNullOrEmpty(request.ThreadId) ? "an Ask conversation" : "a captain session");
            bool ownThread = !String.IsNullOrEmpty(request.ThreadId) && String.Equals(request.UserId, auth.UserId, StringComparison.Ordinal);
            return new InboxItem
            {
                Kind = InboxItemKinds.CliPermission,
                Severity = InboxSeverityEnum.Warning,
                Title = "CLI permission: " + request.ToolName + " " + Clip(request.SummaryText, 120),
                EntityName = request.ToolName,
                Detail = (String.IsNullOrEmpty(request.CaptainName) ? "A captain" : "Captain " + request.CaptainName)
                    + (String.IsNullOrEmpty(request.VesselName) ? "" : " on " + request.VesselName)
                    + " (" + where + ") is waiting for permission to run " + request.ToolName + "."
                    + (request.CanDecide ? "" : " An admin must decide it."),
                EntityType = "cli_permission_request",
                EntityId = request.Id,
                Href = ownThread ? "/ask/" + request.ThreadId : "/server?tab=cli-permissions&request=" + request.Id,
                CliPermission = request,
                ExpiresUtc = request.ExpiresUtc
            };
        }

        private async Task<List<Captain>> CaptainsByStateAsync(AuthContext auth, CaptainStateEnum state, CancellationToken token)
        {
            if (auth.IsAdmin) return await _Database.Captains.EnumerateByStateAsync(state, token).ConfigureAwait(false);
            List<Captain> list = await _Database.Captains.EnumerateByStateAsync(auth.TenantId!, state, token).ConfigureAwait(false);
            return auth.IsTenantAdmin ? list : list.Where(c => String.Equals(c.UserId, auth.UserId, StringComparison.Ordinal)).ToList();
        }

        /// <summary>
        /// Pending, unexpired Ask proposals in the caller's own conversations. Ask threads belong to one user, and only
        /// the thread owner can approve, so admins are not shown other users' proposals.
        /// </summary>
        private async Task<List<AskActionProposal>> PendingAskProposalsAsync(AuthContext auth, CancellationToken token)
        {
            if (String.IsNullOrEmpty(auth.UserId)) return new List<AskActionProposal>();
            DateTime now = DateTime.UtcNow;
            int expiryMinutes = AskProposalExpiryMinutes;
            List<AskActionProposal> pending = await _Database.AskActionProposals.EnumeratePendingBeforeAsync(now.AddDays(1), token).ConfigureAwait(false);
            return pending
                .Where(p => String.Equals(p.TenantId, auth.TenantId, StringComparison.Ordinal)
                    && String.Equals(p.UserId, auth.UserId, StringComparison.Ordinal)
                    && (!p.ExpiresUtc.HasValue || p.ExpiresUtc.Value > now)
                    && (expiryMinutes == 0 || p.CreatedUtc.AddMinutes(expiryMinutes) >= now))
                .OrderBy(p => p.CreatedUtc)
                .ToList();
        }

        /// <summary>
        /// Pending, unexpired CLI permission requests the caller may see (admins of the tenant, and the owner), with
        /// names and the caller's CanDecide flag.
        /// </summary>
        private async Task<List<CliPermissionRequest>> PendingCliPermissionsAsync(AuthContext auth, CancellationToken token)
        {
            if (CliPermissionAccess.IsCaptainSession(auth)) return new List<CliPermissionRequest>();
            CliPermissionRequestQuery query = new CliPermissionRequestQuery { Status = CliPermissionRequestStatusEnum.Pending, Limit = _MaxPendingCliPermissions };
            if (!auth.IsAdmin)
            {
                query.TenantId = auth.TenantId;
                if (!auth.IsTenantAdmin) query.UserId = auth.UserId;
            }

            DateTime now = DateTime.UtcNow;
            List<CliPermissionRequest> pending = await _Database.CliPermissionRequests.EnumerateAsync(query, token).ConfigureAwait(false);
            List<CliPermissionRequest> visible = pending.Where(r => r.ExpiresUtc > now && CliPermissionAccess.CanView(auth, r)).OrderBy(r => r.CreatedUtc).ToList();
            CliPermissionSettings permissions = _Settings?.Permissions ?? new CliPermissionSettings();
            foreach (CliPermissionRequest request in visible)
            {
                if (!String.IsNullOrEmpty(request.CaptainId)) request.CaptainName = (await _Database.Captains.ReadAsync(request.CaptainId!, token).ConfigureAwait(false))?.Name;
                if (!String.IsNullOrEmpty(request.VesselId)) request.VesselName = (await _Database.Vessels.ReadAsync(request.VesselId!, token).ConfigureAwait(false))?.Name;
                if (!String.IsNullOrEmpty(request.MissionId)) request.MissionTitle = (await _Database.Missions.ReadAsync(request.MissionId!, token).ConfigureAwait(false))?.Title;
                request.CanDecide = CliPermissionAccess.CanDecide(auth, request, permissions);
                request.CanRemember = request.CanDecide && CliPermissionAccess.CanRemember(auth, request);
            }

            return visible;
        }

        private async Task<string?> MergeEntryMissionTitleAsync(MergeEntry entry, CancellationToken token)
        {
            if (String.IsNullOrEmpty(entry.MissionId)) return null;
            try
            {
                Mission? mission = !String.IsNullOrEmpty(entry.TenantId)
                    ? await _Database.Missions.ReadAsync(entry.TenantId, entry.MissionId, token).ConfigureAwait(false)
                    : await _Database.Missions.ReadAsync(entry.MissionId, token).ConfigureAwait(false);
                return mission == null || String.IsNullOrWhiteSpace(mission.Title) ? null : mission.Title;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not read mission " + entry.MissionId + " for merge entry " + entry.Id + ": " + ex.Message);
                return null;
            }
        }

        private static string MergeFailedDetail(MergeEntry entry, string? missionTitle)
        {
            string subject = missionTitle != null
                ? "Mission \"" + missionTitle + "\" (branch " + entry.BranchName + ")"
                : "Branch " + entry.BranchName;
            string detail = subject + " could not be merged into " + entry.TargetBranch + ".";
            if (!String.IsNullOrWhiteSpace(entry.TestOutput))
                detail += " " + Clip(entry.TestOutput!.Trim(), 500);
            else
                detail += " It failed testing or landing and needs attention.";
            return detail;
        }

        private static string Clip(string? text, int max)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text!.Length <= max ? text : text.Substring(0, max) + "...";
        }

        private static int Clamp(int minutes)
        {
            return minutes < 0 ? 0 : (minutes > 1440 ? 1440 : minutes);
        }

        #endregion
    }
}
