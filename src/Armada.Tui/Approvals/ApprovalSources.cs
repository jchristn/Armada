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
    /// stalled captains, deployments pending approval) replaces those kinds, and <c>mission.changed</c>,
    /// <c>captain.changed</c>, and <c>deployment.changed</c> events add or remove items immediately (an item added from an
    /// event survives an older inbox result for <see cref="LiveGraceSeconds"/>). The first poll after sign-in does not
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
            ApprovalKindEnum.MissionReview, ApprovalKindEnum.DeploymentApproval, ApprovalKindEnum.FailedLanding, ApprovalKindEnum.StalledCaptain
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
            if (inbox == null || String.IsNullOrEmpty(inbox.EntityId)) return null;
            ApprovalItem item = new ApprovalItem();
            switch (inbox.Kind)
            {
                case "review":
                    item.Kind = ApprovalKindEnum.MissionReview;
                    item.EntityName = Strip(inbox.Title, "Review: ");
                    break;
                case "landing_failed":
                    item.Kind = ApprovalKindEnum.FailedLanding;
                    item.EntityName = Strip(inbox.Title, "Landing failed: ");
                    break;
                case "stalled_captain":
                    item.Kind = ApprovalKindEnum.StalledCaptain;
                    item.EntityName = Strip(inbox.Title, "Stalled captain: ");
                    break;
                case "deployment_approval":
                    item.Kind = ApprovalKindEnum.DeploymentApproval;
                    item.EntityName = Strip(inbox.Title, "Deployment awaiting approval: ");
                    break;
                default:
                    return null;
            }

            item.EntityId = inbox.EntityId!;
            item.Title = inbox.Title;
            item.Detail = inbox.Detail;
            item.Route = String.IsNullOrEmpty(inbox.Href) ? null : inbox.Href;
            item.Urgency = inbox.Severity == InboxSeverityEnum.Critical ? 2 : 1;
            item.Source = "Needs You";
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
            List<ApprovalItem> items = inbox.Select(FromInbox).Where(i => i != null).Select(i => i!).ToList();
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
                string status = data.Status ?? "";
                string title = data.Title ?? id;
                Apply(ApprovalKindEnum.MissionReview, id, status == "Review", "Review: " + title, title, "/missions/" + id, 1);
                Apply(ApprovalKindEnum.FailedLanding, id, status == "LandingFailed", "Landing failed: " + title, title, "/missions/" + id, 2);
            }
            else if (message.Type == ArmadaEventTypes.CaptainChanged)
            {
                string state = data.State ?? data.Status ?? "";
                if (state.Length == 0) return;
                string name = data.Name ?? id;
                Apply(ApprovalKindEnum.StalledCaptain, id, state == "Stalled", "Stalled captain: " + name, name, "/captains/" + id, 1);
            }
            else if (message.Type == ArmadaEventTypes.DeploymentChanged)
            {
                string status = data.Status ?? "";
                if (status.Length == 0) return;
                string title = data.Title ?? id;
                Apply(ApprovalKindEnum.DeploymentApproval, id, status == "PendingApproval", "Deployment awaiting approval: " + title, title, "/deployments/" + id, 1);
            }
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

        private static string Strip(string? text, string prefix)
        {
            string t = text ?? "";
            return t.StartsWith(prefix, StringComparison.Ordinal) ? t.Substring(prefix.Length) : t;
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
