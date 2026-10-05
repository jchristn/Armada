namespace Armada.Tui.Approvals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// Feeds <see cref="ApprovalService"/> from the server (W3.3): every inbox poll (mission reviews, failed landings,
    /// stalled captains, deployments pending approval, CLI permission prompts) replaces those kinds, and
    /// <c>mission.changed</c>, <c>captain.changed</c>, <c>deployment.changed</c>, <c>cli_permission.requested</c>, and
    /// <c>cli_permission.resolved</c> events add or remove items immediately (an item added from an event survives an
    /// older inbox result for <see cref="LiveGraceSeconds"/>). The first poll after sign-in does not
    /// ring the bell. Ask proposals come from the Ask controller. Call on the UI loop.
    /// </summary>
    public class ApprovalSources
    {
        #region Public-Members

        /// <summary>
        /// Kinds this source owns.
        /// </summary>
        public static IReadOnlyList<ApprovalKindEnum> Kinds { get; } = new List<ApprovalKindEnum>
        {
            ApprovalKindEnum.MissionReview, ApprovalKindEnum.DeploymentApproval, ApprovalKindEnum.FailedLanding, ApprovalKindEnum.StalledCaptain,
            ApprovalKindEnum.CliPermission
        };

        /// <summary>
        /// Seconds an event-added item is kept even when an inbox result does not list it yet. Default 15; clamped to
        /// 0..300.
        /// </summary>
        public int LiveGraceSeconds
        {
            get { return _LiveGraceSeconds; }
            set { _LiveGraceSeconds = Math.Clamp(value, 0, 300); }
        }

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private readonly Dictionary<string, KeyValuePair<DateTime, ApprovalItem>> _Live = new Dictionary<string, KeyValuePair<DateTime, ApprovalItem>>(StringComparer.Ordinal);
        private int _LiveGraceSeconds = 15;
        private bool _FirstSync = true;
        private int _LastInboxHash = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and subscribe.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public ApprovalSources(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            context.Status.Changed += (s, e) => SyncInbox();
            context.Events.Subscribe(ArmadaEventTypes.MissionChanged, OnEntityChanged);
            context.Events.Subscribe(ArmadaEventTypes.CaptainChanged, OnEntityChanged);
            context.Events.Subscribe(ArmadaEventTypes.DeploymentChanged, OnEntityChanged);
            context.Events.Subscribe(ArmadaEventTypes.CliPermissionRequested, OnCliPermission);
            context.Events.Subscribe(ArmadaEventTypes.CliPermissionResolved, OnCliPermission);
            context.Session.SignedIn += (s, e) =>
            {
                _FirstSync = true;
                _LastInboxHash = 0;
                _Live.Clear();
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Map an inbox item to an approval item, or null for kinds that need no decision.
        /// </summary>
        /// <param name="inbox">Inbox item.</param>
        /// <returns>Approval item or null.</returns>
        public static ApprovalItem? FromInbox(InboxItem inbox)
        {
            return FromInbox(inbox, null);
        }

        /// <summary>
        /// Map an inbox item to an approval item (null for kinds this source does not own), localizing the deployment
        /// approval label ("Deploy to {environment}: {title}") through <paramref name="loc"/>.
        /// </summary>
        /// <param name="inbox">Inbox item.</param>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <returns>Approval item or null.</returns>
        public static ApprovalItem? FromInbox(InboxItem inbox, ITextLocalizer? loc)
        {
            if (inbox == null || String.IsNullOrEmpty(inbox.EntityId)) return null;
            if (inbox.Kind == InboxItemKinds.CliPermission)
            {
                if (inbox.CliPermission == null) return null;
                ApprovalItem? cli = FromCliPermission(inbox.CliPermission, loc, null);
                if (cli == null) return null;
                if (inbox.ExpiresUtc != null) cli.ExpiresUtc = inbox.ExpiresUtc;
                if (!String.IsNullOrEmpty(inbox.CliPermission.ThreadId) && String.Equals(inbox.Href, "/ask/" + inbox.CliPermission.ThreadId, StringComparison.Ordinal)) cli.Route = inbox.Href;
                return cli;
            }

            ApprovalItem item = new ApprovalItem();
            switch (inbox.Kind)
            {
                case InboxItemKinds.Review:
                    item.Kind = ApprovalKindEnum.MissionReview;
                    break;
                case InboxItemKinds.LandingFailed:
                    item.Kind = ApprovalKindEnum.FailedLanding;
                    break;
                case InboxItemKinds.StalledCaptain:
                    item.Kind = ApprovalKindEnum.StalledCaptain;
                    break;
                case InboxItemKinds.DeploymentApproval:
                    item.Kind = ApprovalKindEnum.DeploymentApproval;
                    break;
                default:
                    return null;
            }

            item.EntityName = !String.IsNullOrEmpty(inbox.EntityName) ? inbox.EntityName! : inbox.Title;
            item.EntityId = inbox.EntityId!;
            item.Title = inbox.Title;
            if (item.Kind == ApprovalKindEnum.DeploymentApproval)
            {
                string label = DeploymentApprovalText.ForInbox(loc, inbox);
                item.Title = label;
                item.EntityName = label;
            }

            item.Detail = inbox.Detail;
            item.Route = String.IsNullOrEmpty(inbox.Href) ? null : inbox.Href;
            item.Urgency = inbox.Severity == InboxSeverityEnum.Critical ? 2 : 1;
            item.Source = "Needs You";
            return item;
        }

        /// <summary>
        /// Map a pending CLI permission request to an approval item (null when it is no longer pending): the title is the
        /// tool and command, the detail names the captain, vessel, and mission or conversation (and that an admin must
        /// decide when the user cannot), and Enter opens the mission, the user's own conversation, or the captain.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="currentUserId">Signed-in user id (a conversation opens only for its owner), or null.</param>
        /// <returns>Approval item or null.</returns>
        public static ApprovalItem? FromCliPermission(CliPermissionRequest request, ITextLocalizer? loc, string? currentUserId)
        {
            if (request == null || String.IsNullOrEmpty(request.Id) || request.Status != CliPermissionRequestStatusEnum.Pending) return null;
            ApprovalItem item = new ApprovalItem();
            item.Kind = ApprovalKindEnum.CliPermission;
            item.EntityId = request.Id;
            item.Title = CliPermissionText.Title(request);
            item.EntityName = String.IsNullOrEmpty(request.ToolName) ? request.Id : request.ToolName;
            item.ToolName = request.ToolName;
            item.Arguments = request.InputText;
            item.ExpiresUtc = request.ExpiresUtc;
            item.ParentId = request.ThreadId;
            string where = CliPermissionText.Where(loc, request);
            string admin = request.CanDecide ? "" : (loc == null ? "An admin must decide this request." : loc.T("An admin must decide this request."));
            item.Detail = (where + (where.Length > 0 && admin.Length > 0 ? "  " : "") + admin).Trim();
            if (!String.IsNullOrEmpty(request.MissionId)) item.Route = "/missions/" + request.MissionId;
            else if (!String.IsNullOrEmpty(request.ThreadId) && !String.IsNullOrEmpty(currentUserId) && String.Equals(request.UserId, currentUserId, StringComparison.Ordinal)) item.Route = "/ask/" + request.ThreadId;
            else if (!String.IsNullOrEmpty(request.CaptainId)) item.Route = "/captains/" + request.CaptainId;
            item.Urgency = 2;
            item.Source = "Needs You";
            item.CliPermission = request;
            return item;
        }

        /// <summary>
        /// Replace the polled kinds with the latest inbox.
        /// </summary>
        public void SyncInbox()
        {
            if (!_Context.Session.IsSignedIn || !_Context.Status.InboxChecked) return;
            List<InboxItem> inbox = _Context.Status.Inbox;
            int hash = Hash(inbox);
            if (hash == _LastInboxHash && !_FirstSync && _Live.Count == 0) return;
            _LastInboxHash = hash;
            DateTime now = _Context.Clock.UtcNow;
            List<ApprovalItem> items = inbox.Select(i => FromInbox(i, _Context.Loc)).Where(i => i != null).Select(i => i!).ToList();
            foreach (string key in _Live.Keys.ToList())
            {
                KeyValuePair<DateTime, ApprovalItem> live = _Live[key];
                if ((now - live.Key).TotalSeconds > _LiveGraceSeconds || items.Any(i => i.Key == key)) _Live.Remove(key);
                else items.Add(live.Value);
            }

            _Context.Approvals.Sync(Kinds, items, !_FirstSync);
            _FirstSync = false;
        }

        /// <summary>
        /// Apply an entity change event (mission Review or LandingFailed, captain Stalled, deployment PendingApproval).
        /// </summary>
        /// <param name="message">Socket message.</param>
        public void OnEntityChanged(ArmadaSocketMessage message)
        {
            EntityChangedEvent? data = message?.GetData<EntityChangedEvent>();
            if (data == null || String.IsNullOrEmpty(data.Id)) return;
            string id = data.Id!;
            if (message!.Type == ArmadaEventTypes.MissionChanged)
            {
                MissionStatusEnum? status = data.MissionStatus;
                string title = data.Title ?? id;
                Apply(ApprovalKindEnum.MissionReview, id, status == MissionStatusEnum.Review, "Review: " + title, title, "/missions/" + id, 1);
                Apply(ApprovalKindEnum.FailedLanding, id, status == MissionStatusEnum.LandingFailed, "Landing failed: " + title, title, "/missions/" + id, 2);
            }
            else if (message.Type == ArmadaEventTypes.CaptainChanged)
            {
                CaptainStateEnum? state = data.CaptainState;
                if (state == null) return;
                string name = data.Name ?? id;
                Apply(ApprovalKindEnum.StalledCaptain, id, state == CaptainStateEnum.Stalled, "Stalled captain: " + name, name, "/captains/" + id, 1);
            }
            else if (message.Type == ArmadaEventTypes.DeploymentChanged)
            {
                DeploymentStatusEnum? status = data.DeploymentStatus;
                if (status == null) return;
                string label = DeploymentApprovalText.Label(_Context.Loc, data.EnvironmentName, data.Title, id);
                Apply(ApprovalKindEnum.DeploymentApproval, id, status == DeploymentStatusEnum.PendingApproval, label, label, "/deployments/" + id, 1);
            }
        }

        /// <summary>
        /// Apply a <c>cli_permission.requested</c> or <c>cli_permission.resolved</c> event: a pending request joins the
        /// queue; any other status removes it.
        /// </summary>
        /// <param name="message">Socket message.</param>
        public void OnCliPermission(ArmadaSocketMessage message)
        {
            CliPermissionEvent? data = message?.GetData<CliPermissionEvent>();
            if (data == null) return;
            string? id = !String.IsNullOrEmpty(data.Request?.Id) ? data.Request!.Id : data.RequestId;
            if (String.IsNullOrEmpty(id)) return;
            CliPermissionRequestStatusEnum status = data.Status ?? data.Request?.Status ?? CliPermissionRequestStatusEnum.Pending;
            string key = ApprovalKindEnum.CliPermission + ":" + id;
            ApprovalItem? item = data.Request != null && status == CliPermissionRequestStatusEnum.Pending ? FromCliPermission(data.Request, _Context.Loc, _Context.Session.Identity?.User?.Id) : null;
            if (item == null)
            {
                _Live.Remove(key);
                _Context.Approvals.Remove(ApprovalKindEnum.CliPermission, id!);
                return;
            }

            bool isNew = _Context.Approvals.Find(ApprovalKindEnum.CliPermission, id!) == null;
            item.Source = "Live";
            _Live[key] = new KeyValuePair<DateTime, ApprovalItem>(_Context.Clock.UtcNow, item);
            _Context.Approvals.Upsert(item, isNew);
        }

        #endregion

        #region Private-Methods

        private void Apply(ApprovalKindEnum kind, string id, bool needed, string title, string name, string route, int urgency)
        {
            string key = kind + ":" + id;
            if (!needed)
            {
                _Live.Remove(key);
                _Context.Approvals.Remove(kind, id);
                return;
            }

            ApprovalItem item = new ApprovalItem();
            item.Kind = kind;
            item.EntityId = id;
            item.Title = title;
            item.EntityName = name;
            item.Route = route;
            item.Urgency = urgency;
            item.Source = "Live";
            ApprovalItem? existing = _Context.Approvals.Find(kind, id);
            if (existing != null)
            {
                item.Detail = existing.Detail;
                item.Source = existing.Source;
            }

            _Live[key] = new KeyValuePair<DateTime, ApprovalItem>(_Context.Clock.UtcNow, item);
            _Context.Approvals.Upsert(item, existing == null);
        }

        private static int Hash(List<InboxItem> inbox)
        {
            unchecked
            {
                int h = 17;
                foreach (InboxItem i in inbox) h = h * 31 + (i.Kind + "|" + i.EntityId + "|" + i.Title + "|" + i.Severity).GetHashCode();
                return h * 31 + inbox.Count;
            }
        }

        #endregion
    }
}
