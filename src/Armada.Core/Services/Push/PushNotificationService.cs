namespace Armada.Core.Services.Push
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Turns inbox-worthy occurrences into push notifications on the right users' active devices through an
    /// <see cref="IPushTransport"/> (the Expo Push Service in production).
    /// <para>
    /// The Admiral feeds it from the existing broadcast points (mission, voyage, captain, and deployment changes, CLI
    /// permission requests, and new Ask proposals); every hook only enqueues and returns at once, and a single background
    /// worker resolves recipients, applies categories, deduplication, and the per-user rate limit, sends in batches of
    /// at most 100 with retries and backoff on transient failures, and deactivates devices the push service reports as
    /// not registered (from tickets at once, from receipts on a later pass). A failure never reaches the operation that
    /// raised the occurrence.
    /// </para>
    /// <para>
    /// Recipients follow the inbox's visibility rules within the entity's tenant: Ask proposals go to the thread owner
    /// only; CLI permission requests, mission reviews, and deployment approvals go to the owner and the tenant's admins
    /// (users of the tenant with the tenant admin or global admin flag); failures, stalled captains, and finished
    /// voyages go to the owner, or to the tenant's admins when the entity has no active owner. Users of other tenants
    /// never receive a push about the entity.
    /// </para>
    /// </summary>
    public class PushNotificationService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Delay before the first retry of a transient send failure; doubled on each further attempt. Default 2 seconds.
        /// </summary>
        public TimeSpan RetryBaseDelay
        {
            get => _RetryBaseDelay;
            set => _RetryBaseDelay = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        /// <summary>
        /// Attempts per batch (the first try plus retries). Default 4, clamped to 1..10.
        /// </summary>
        public int MaxAttempts
        {
            get => _MaxAttempts;
            set => _MaxAttempts = value < 1 ? 1 : (value > 10 ? 10 : value);
        }

        /// <summary>
        /// Age a ticket must reach before its receipt is fetched (Expo recommends about 15 minutes). Default 15
        /// minutes.
        /// </summary>
        public TimeSpan ReceiptDelay
        {
            get => _ReceiptDelay;
            set => _ReceiptDelay = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        /// <summary>
        /// How often the background worker checks for due receipts. Default 1 minute.
        /// </summary>
        public TimeSpan ReceiptPollInterval
        {
            get => _ReceiptPollInterval;
            set => _ReceiptPollInterval = value < TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : value;
        }

        /// <summary>
        /// Number of tickets whose receipt has not been checked yet.
        /// </summary>
        public int PendingReceiptCount => _PendingReceipts.Count;

        #endregion

        #region Private-Members

        private readonly string _Header = "[PushNotificationService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly IPushTransport _Transport;
        private readonly TimeProvider _Time;
        private readonly Channel<PushOccurrence> _Queue;
        private readonly object _StateLock = new object();
        private readonly Dictionary<string, string> _NotifiedStates = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _Dedupe = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<DateTime>> _RateWindows = new Dictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
        private readonly Dictionary<string, PushPendingReceipt> _PendingReceipts = new Dictionary<string, PushPendingReceipt>(StringComparer.Ordinal);
        private readonly object _IdleLock = new object();
        private int _InFlight = 0;
        private TaskCompletionSource<bool> _Idle = NewCompletedIdle();
        private CancellationTokenSource? _WorkerCts = null;
        private Task? _Worker = null;
        private Task? _ReceiptLoop = null;
        private TimeSpan _RetryBaseDelay = TimeSpan.FromSeconds(2);
        private int _MaxAttempts = 4;
        private TimeSpan _ReceiptDelay = TimeSpan.FromMinutes(15);
        private TimeSpan _ReceiptPollInterval = TimeSpan.FromMinutes(1);
        private bool _Disposed = false;

        private const int _QueueCapacity = 1000;
        private const int _MaxPendingReceipts = 10000;
        private const int _MaxTrackedStates = 20000;

        private static readonly HashSet<string> _ApprovalKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            InboxItemKinds.Review, InboxItemKinds.DeploymentApproval, InboxItemKinds.AskProposal, InboxItemKinds.CliPermission
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. Call <see cref="Start"/> to run the background worker.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings (Push and Permissions are read live).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="transport">Push transport.</param>
        /// <param name="time">Clock for rate limiting, deduplication, and receipt ages; null uses the system clock.</param>
        public PushNotificationService(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, IPushTransport transport, TimeProvider? time = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _Time = time ?? TimeProvider.System;
            BoundedChannelOptions options = new BoundedChannelOptions(_QueueCapacity);
            options.FullMode = BoundedChannelFullMode.DropOldest;
            options.SingleReader = true;
            options.SingleWriter = false;
            _Queue = Channel.CreateBounded<PushOccurrence>(options, dropped => OnDropped(dropped));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the background worker (idempotent).
        /// </summary>
        public void Start()
        {
            lock (_IdleLock)
            {
                if (_Worker != null || _Disposed) return;
                _WorkerCts = new CancellationTokenSource();
                CancellationToken token = _WorkerCts.Token;
                _Worker = Task.Run(() => RunWorkerAsync(token));
                _ReceiptLoop = Task.Run(() => RunReceiptLoopAsync(token));
            }
        }

        /// <summary>
        /// Stop the background worker. Queued occurrences that were not processed are dropped.
        /// </summary>
        /// <returns>A task.</returns>
        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            Task? worker;
            Task? receipts;
            lock (_IdleLock)
            {
                cts = _WorkerCts;
                worker = _Worker;
                receipts = _ReceiptLoop;
                _WorkerCts = null;
                _Worker = null;
                _ReceiptLoop = null;
            }

            if (cts == null) return;
            cts.Cancel();
            try { if (worker != null) await worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
            try { if (receipts != null) await receipts.ConfigureAwait(false); } catch (OperationCanceledException) { }
            cts.Dispose();
        }

        /// <summary>
        /// Queue an occurrence for delivery. Never blocks and never throws; returns false when push is disabled or the
        /// occurrence has no tenant.
        /// </summary>
        /// <param name="occurrence">Occurrence.</param>
        /// <returns>True when queued.</returns>
        public bool Enqueue(PushOccurrence? occurrence)
        {
            try
            {
                if (occurrence == null || _Disposed) return false;
                if (!_Settings.Push.Enabled) return false;
                if (String.IsNullOrEmpty(occurrence.TenantId)) return false;
                lock (_IdleLock)
                {
                    if (_InFlight == 0) _Idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _InFlight++;
                }

                if (_Queue.Writer.TryWrite(occurrence)) return true;
                MarkDone();
                return false;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not queue a push: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Wait until every queued occurrence has been processed.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public Task WaitForIdleAsync(CancellationToken token = default)
        {
            Task idle;
            lock (_IdleLock) idle = _Idle.Task;
            return idle.WaitAsync(token);
        }

        /// <summary>
        /// Hook for a mission change: Review, LandingFailed, and Failed are announced once per entry into the status.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="status">Effective status when it differs from the mission's (a broadcast status override), or
        /// null.</param>
        public void OnMissionChanged(Mission? mission, MissionStatusEnum? status = null)
        {
            if (mission == null) return;
            PushOccurrence? occurrence = FromMission(mission, status);
            if (IsNewState("mission:" + mission.Id, occurrence?.Kind)) Enqueue(occurrence);
        }

        /// <summary>
        /// Hook for a voyage change: Complete and Failed are announced once.
        /// </summary>
        /// <param name="voyage">Voyage.</param>
        /// <param name="status">Effective status when it differs from the voyage's (a broadcast status override), or
        /// null.</param>
        public void OnVoyageChanged(Voyage? voyage, VoyageStatusEnum? status = null)
        {
            if (voyage == null) return;
            PushOccurrence? occurrence = FromVoyage(voyage, status);
            if (IsNewState("voyage:" + voyage.Id, occurrence == null ? null : occurrence.Title)) Enqueue(occurrence);
        }

        /// <summary>
        /// Hook for a captain change: Stalled is announced once per stall.
        /// </summary>
        /// <param name="captain">Captain.</param>
        public void OnCaptainChanged(Captain? captain)
        {
            if (captain == null) return;
            PushOccurrence? occurrence = FromCaptain(captain);
            if (IsNewState("captain:" + captain.Id, occurrence?.Kind)) Enqueue(occurrence);
        }

        /// <summary>
        /// Hook for a deployment change: PendingApproval is announced once.
        /// </summary>
        /// <param name="deployment">Deployment.</param>
        public void OnDeploymentChanged(Deployment? deployment)
        {
            if (deployment == null) return;
            PushOccurrence? occurrence = FromDeployment(deployment);
            if (IsNewState("deployment:" + deployment.Id, occurrence?.Kind)) Enqueue(occurrence);
        }

        /// <summary>
        /// Hook for a CLI permission event: a pending request (cli_permission.requested) is announced once.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <param name="request">Request.</param>
        public void OnCliPermissionEvent(string? eventType, CliPermissionRequest? request)
        {
            if (request == null || request.Status != CliPermissionRequestStatusEnum.Pending) return;
            if (!String.Equals(eventType, "cli_permission.requested", StringComparison.Ordinal)) return;
            PushOccurrence occurrence = FromCliPermission(request);
            if (IsNewState("cli_permission:" + request.Id, occurrence.Kind)) Enqueue(occurrence);
        }

        /// <summary>
        /// Hook for a new Ask proposal: a pending proposal is announced to the thread owner.
        /// </summary>
        /// <param name="proposal">Proposal.</param>
        public void OnAskProposalCreated(AskActionProposal? proposal)
        {
            if (proposal == null || proposal.Status != AskProposalStatusEnum.Pending) return;
            Enqueue(FromAskProposal(proposal));
        }

        /// <summary>
        /// Deliver one occurrence now: resolve recipients, filter devices by category, apply deduplication and the rate
        /// limit, compute badges, send, and process tickets. Used by the worker and by tests.
        /// </summary>
        /// <param name="occurrence">Occurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of messages the push service accepted.</returns>
        public async Task<int> DeliverAsync(PushOccurrence occurrence, CancellationToken token = default)
        {
            if (occurrence == null) throw new ArgumentNullException(nameof(occurrence));
            PushSettings push = _Settings.Push;
            if (!push.Enabled || String.IsNullOrEmpty(occurrence.TenantId)) return 0;

            PushDeviceQuery deviceQuery = new PushDeviceQuery { TenantId = occurrence.TenantId, ActiveOnly = true };
            List<PushDevice> devices = await _Database.PushDevices.EnumerateAsync(deviceQuery, token).ConfigureAwait(false);
            if (devices.Count == 0) return 0;

            List<PushRecipient> recipients = await ResolveRecipientsAsync(occurrence, token).ConfigureAwait(false);
            List<PushMessage> messages = new List<PushMessage>();
            List<PushDevice> targets = new List<PushDevice>();
            foreach (PushRecipient recipient in recipients)
            {
                List<PushDevice> userDevices = devices
                    .Where(d => d.Active
                        && String.Equals(d.TenantId, recipient.TenantId, StringComparison.Ordinal)
                        && String.Equals(d.UserId, recipient.UserId, StringComparison.Ordinal)
                        && d.Categories.Contains(occurrence.Category))
                    .ToList();
                if (userDevices.Count == 0) continue;
                if (!TryAdmit(recipient.UserId, occurrence.Kind, occurrence.EntityId, push)) continue;

                int? badge = await CountPendingApprovalsAsync(recipient, token).ConfigureAwait(false);
                foreach (PushDevice device in userDevices)
                {
                    messages.Add(BuildMessage(occurrence, recipient, device, badge));
                    targets.Add(device);
                }
            }

            if (messages.Count == 0) return 0;
            return await SendAllAsync(messages, targets, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The users who should receive a push for an occurrence (before device and category filtering).
        /// </summary>
        /// <param name="occurrence">Occurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recipients (each user once).</returns>
        public async Task<List<PushRecipient>> ResolveRecipientsAsync(PushOccurrence occurrence, CancellationToken token = default)
        {
            if (occurrence == null) throw new ArgumentNullException(nameof(occurrence));
            List<PushRecipient> result = new List<PushRecipient>();
            if (String.IsNullOrEmpty(occurrence.TenantId)) return result;
            string tenantId = occurrence.TenantId!;

            List<UserMaster> users = (await _Database.Users.EnumerateAsync(tenantId, token).ConfigureAwait(false))
                .Where(u => u.Active && String.Equals(u.TenantId, tenantId, StringComparison.Ordinal))
                .ToList();
            UserMaster? owner = String.IsNullOrEmpty(occurrence.OwnerUserId)
                ? null
                : users.FirstOrDefault(u => String.Equals(u.Id, occurrence.OwnerUserId, StringComparison.Ordinal));
            List<UserMaster> admins = users.Where(u => u.IsAdmin || u.IsTenantAdmin).ToList();

            List<UserMaster> chosen = new List<UserMaster>();
            switch (occurrence.Category)
            {
                case PushCategoryEnum.AskProposal:
                    if (owner != null) chosen.Add(owner);
                    break;
                case PushCategoryEnum.CliPermission:
                case PushCategoryEnum.MissionReview:
                case PushCategoryEnum.DeploymentApproval:
                    if (owner != null) chosen.Add(owner);
                    chosen.AddRange(admins);
                    break;
                default:
                    if (owner != null) chosen.Add(owner);
                    else chosen.AddRange(admins);
                    break;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            CliPermissionSettings permissions = _Settings.Permissions ?? new CliPermissionSettings();
            foreach (UserMaster user in chosen)
            {
                if (!seen.Add(user.Id)) continue;
                PushRecipient recipient = new PushRecipient();
                recipient.TenantId = tenantId;
                recipient.UserId = user.Id;
                recipient.IsAdmin = user.IsAdmin;
                recipient.IsTenantAdmin = user.IsTenantAdmin;
                recipient.IsOwner = owner != null && String.Equals(owner.Id, user.Id, StringComparison.Ordinal);
                if (occurrence.Category == PushCategoryEnum.AskProposal)
                {
                    recipient.CanAct = recipient.IsOwner;
                }
                else if (occurrence.Category == PushCategoryEnum.CliPermission && occurrence.CliPermission != null)
                {
                    AuthContext auth = AuthContext.Authenticated(tenantId, user.Id, user.IsAdmin, user.IsTenantAdmin, "Push");
                    if (!CliPermissionAccess.CanView(auth, occurrence.CliPermission)) continue;
                    recipient.CanAct = CliPermissionAccess.CanDecide(auth, occurrence.CliPermission, permissions);
                }

                result.Add(recipient);
            }

            return result;
        }

        /// <summary>
        /// Build the message for one device of one recipient.
        /// </summary>
        /// <param name="occurrence">Occurrence.</param>
        /// <param name="recipient">Recipient.</param>
        /// <param name="device">Device.</param>
        /// <param name="badge">Badge count, or null.</param>
        /// <returns>The message.</returns>
        public static PushMessage BuildMessage(PushOccurrence occurrence, PushRecipient recipient, PushDevice device, int? badge)
        {
            if (occurrence == null) throw new ArgumentNullException(nameof(occurrence));
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));
            if (device == null) throw new ArgumentNullException(nameof(device));
            PushMessage message = new PushMessage();
            message.To = device.ExpoPushToken;
            message.Title = PushContentFormatter.Title(occurrence.Title);
            message.Body = PushContentFormatter.Body(occurrence.Body);
            message.Data.Url = !recipient.IsOwner && !String.IsNullOrEmpty(occurrence.ApproverUrl) ? occurrence.ApproverUrl! : occurrence.Url;
            message.Data.Kind = occurrence.Kind;
            message.Data.EntityId = occurrence.EntityId;
            message.Data.Category = occurrence.Category.ToString();
            message.Data.ThreadId = recipient.IsOwner ? occurrence.ThreadId : null;
            message.Sound = "default";
            message.Badge = badge;
            bool actionable = recipient.CanAct
                && (occurrence.Category == PushCategoryEnum.AskProposal || occurrence.Category == PushCategoryEnum.CliPermission);
            message.CategoryId = actionable ? PushNotificationKinds.ApproveDenyCategoryId : null;
            message.Priority = _ApprovalKinds.Contains(occurrence.Kind) ? "high" : "default";
            return message;
        }

        /// <summary>
        /// Send a test push to one device now (bypasses categories and deduplication; counts toward the rate limit).
        /// </summary>
        /// <param name="device">Device.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        public async Task<PushTestResult> SendTestAsync(PushDevice device, CancellationToken token = default)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            PushTestResult result = new PushTestResult();
            result.DeviceId = device.Id;
            if (!_Settings.Push.Enabled)
            {
                result.Status = PushTestStatusEnum.Disabled;
                result.Message = "Push notifications are disabled in the server settings";
                return result;
            }

            if (!device.Active)
            {
                result.Status = PushTestStatusEnum.DeviceInactive;
                result.Message = "The device is inactive; open the app to register it again";
                return result;
            }

            if (!TryAdmitRate(device.UserId ?? String.Empty, _Settings.Push))
            {
                result.Status = PushTestStatusEnum.RateLimited;
                result.Message = "Too many pushes for this user in the last minute";
                return result;
            }

            PushMessage message = new PushMessage();
            message.To = device.ExpoPushToken;
            message.Title = "Armada test notification";
            message.Body = PushContentFormatter.Body("Push notifications from this Admiral reach " + (String.IsNullOrEmpty(device.DeviceName) ? "this device" : device.DeviceName) + ".");
            message.Data.Url = "/";
            message.Data.Kind = PushNotificationKinds.Test;
            message.Data.EntityId = device.Id;
            message.Data.Category = "Test";

            try
            {
                List<PushTicket> tickets = await SendBatchWithRetryAsync(new List<PushMessage> { message }, token).ConfigureAwait(false);
                PushTicket ticket = tickets.Count > 0 ? tickets[0] : new PushTicket { Ok = false, Error = PushErrorCodeEnum.Unknown };
                await ProcessTicketsAsync(tickets, new List<PushDevice> { device }, token).ConfigureAwait(false);
                if (ticket.Ok)
                {
                    result.Status = PushTestStatusEnum.Sent;
                    result.TicketId = ticket.TicketId;
                    result.Message = "Accepted by the Expo Push Service";
                }
                else
                {
                    result.Status = ticket.Error == PushErrorCodeEnum.DeviceNotRegistered ? PushTestStatusEnum.DeviceNotRegistered : PushTestStatusEnum.Failed;
                    result.Error = ticket.Error;
                    result.Message = ticket.Message;
                }
            }
            catch (PushTransportException ex)
            {
                _Logging.Warn(_Header + "test push to device " + device.Id + " failed: " + ex.Message);
                result.Status = PushTestStatusEnum.Failed;
                result.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Fetch receipts of tickets older than <see cref="ReceiptDelay"/> (or every pending ticket when
        /// <paramref name="all"/> is true), deactivate devices reported as not registered, and forget the checked
        /// tickets. Tickets whose receipt is not available yet stay pending until an hour past the delay.
        /// </summary>
        /// <param name="all">Check every pending ticket regardless of age.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of devices deactivated.</returns>
        public async Task<int> CheckReceiptsAsync(bool all = false, CancellationToken token = default)
        {
            DateTime now = _Time.GetUtcNow().UtcDateTime;
            List<PushPendingReceipt> due;
            lock (_StateLock)
            {
                due = _PendingReceipts.Values.Where(p => all || now - p.SentUtc >= _ReceiptDelay).ToList();
            }

            if (due.Count == 0) return 0;
            int deactivated = 0;
            string? accessToken = AccessToken();
            for (int i = 0; i < due.Count; i += ExpoPushTransport.MaxReceiptIdsPerRequest)
            {
                List<PushPendingReceipt> chunk = due.Skip(i).Take(ExpoPushTransport.MaxReceiptIdsPerRequest).ToList();
                List<PushReceipt> receipts;
                try
                {
                    receipts = await _Transport.GetReceiptsAsync(chunk.Select(p => p.TicketId).ToList(), accessToken, token).ConfigureAwait(false);
                }
                catch (PushTransportException ex)
                {
                    _Logging.Warn(_Header + "could not fetch push receipts: " + ex.Message);
                    continue;
                }

                Dictionary<string, PushReceipt> byTicket = new Dictionary<string, PushReceipt>(StringComparer.Ordinal);
                foreach (PushReceipt receipt in receipts) byTicket[receipt.TicketId] = receipt;
                foreach (PushPendingReceipt pending in chunk)
                {
                    if (!byTicket.TryGetValue(pending.TicketId, out PushReceipt? receipt))
                    {
                        if (now - pending.SentUtc > _ReceiptDelay + TimeSpan.FromHours(1)) Forget(pending.TicketId);
                        continue;
                    }

                    Forget(pending.TicketId);
                    if (receipt.Ok) continue;
                    if (receipt.Error == PushErrorCodeEnum.DeviceNotRegistered)
                    {
                        if (await DeactivateAsync(pending.DeviceId, "receipt", token).ConfigureAwait(false)) deactivated++;
                    }
                    else
                    {
                        _Logging.Warn(_Header + "push to device " + pending.DeviceId + " failed with " + receipt.Error + ": " + receipt.Message);
                    }
                }
            }

            return deactivated;
        }

        /// <summary>
        /// The recipient's pending approvals (mission reviews, deployment approvals, Ask proposals, and CLI permission
        /// requests in their inbox), or null when it cannot be computed.
        /// </summary>
        /// <param name="recipient">Recipient.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The count, or null.</returns>
        public async Task<int?> CountPendingApprovalsAsync(PushRecipient recipient, CancellationToken token = default)
        {
            if (recipient == null) return null;
            try
            {
                AuthContext auth = AuthContext.Authenticated(recipient.TenantId, recipient.UserId, recipient.IsAdmin, recipient.IsTenantAdmin, "Push");
                InboxService inbox = new InboxService(_Database, _Logging, _Settings);
                List<InboxItem> items = await inbox.GetInboxAsync(auth, token).ConfigureAwait(false);
                return items.Count(i => _ApprovalKinds.Contains(i.Kind));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "badge count failed for user " + recipient.UserId + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Occurrence for a mission, or null when its status is not announced.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="status">Effective status, or null for the mission's status.</param>
        /// <returns>The occurrence, or null.</returns>
        public static PushOccurrence? FromMission(Mission mission, MissionStatusEnum? status = null)
        {
            if (mission == null) return null;
            string name = Quote(mission.Title);
            PushOccurrence occurrence = new PushOccurrence();
            switch (status ?? mission.Status)
            {
                case MissionStatusEnum.Review:
                    occurrence.Category = PushCategoryEnum.MissionReview;
                    occurrence.Kind = PushNotificationKinds.Review;
                    occurrence.Title = "Review needed";
                    occurrence.Body = "Mission " + name + " is awaiting review.";
                    break;
                case MissionStatusEnum.LandingFailed:
                    occurrence.Category = PushCategoryEnum.LandingFailed;
                    occurrence.Kind = PushNotificationKinds.LandingFailed;
                    occurrence.Title = "Landing failed";
                    occurrence.Body = "The work of mission " + name + " could not be landed.";
                    break;
                case MissionStatusEnum.Failed:
                    occurrence.Category = PushCategoryEnum.MissionFailed;
                    occurrence.Kind = PushNotificationKinds.Failed;
                    occurrence.Title = "Mission failed";
                    occurrence.Body = "Mission " + name + " failed.";
                    break;
                default:
                    return null;
            }

            occurrence.EntityId = mission.Id;
            occurrence.TenantId = mission.TenantId;
            occurrence.OwnerUserId = mission.UserId;
            occurrence.Url = "/missions/" + mission.Id;
            return occurrence;
        }

        /// <summary>
        /// Occurrence for a voyage, or null unless it completed or failed.
        /// </summary>
        /// <param name="voyage">Voyage.</param>
        /// <param name="status">Effective status, or null for the voyage's status.</param>
        /// <returns>The occurrence, or null.</returns>
        public static PushOccurrence? FromVoyage(Voyage voyage, VoyageStatusEnum? status = null)
        {
            if (voyage == null) return null;
            VoyageStatusEnum effective = status ?? voyage.Status;
            if (effective != VoyageStatusEnum.Complete && effective != VoyageStatusEnum.Failed) return null;
            PushOccurrence occurrence = new PushOccurrence();
            occurrence.Category = PushCategoryEnum.VoyageFinished;
            occurrence.Kind = PushNotificationKinds.VoyageFinished;
            occurrence.Title = effective == VoyageStatusEnum.Complete ? "Voyage complete" : "Voyage failed";
            occurrence.Body = "Voyage " + Quote(voyage.Title) + (effective == VoyageStatusEnum.Complete ? " finished." : " finished with failures.");
            occurrence.EntityId = voyage.Id;
            occurrence.TenantId = voyage.TenantId;
            occurrence.OwnerUserId = voyage.UserId;
            occurrence.Url = "/voyages/" + voyage.Id;
            return occurrence;
        }

        /// <summary>
        /// Occurrence for a captain, or null unless it is stalled.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <returns>The occurrence, or null.</returns>
        public static PushOccurrence? FromCaptain(Captain captain)
        {
            if (captain == null || captain.State != CaptainStateEnum.Stalled) return null;
            PushOccurrence occurrence = new PushOccurrence();
            occurrence.Category = PushCategoryEnum.CaptainStalled;
            occurrence.Kind = PushNotificationKinds.StalledCaptain;
            occurrence.Title = "Captain stalled";
            occurrence.Body = "Captain " + Quote(captain.Name) + " is stalled and may need recovery.";
            occurrence.EntityId = captain.Id;
            occurrence.TenantId = captain.TenantId;
            occurrence.OwnerUserId = captain.UserId;
            occurrence.Url = "/captains/" + captain.Id;
            return occurrence;
        }

        /// <summary>
        /// Occurrence for a deployment, or null unless it is waiting for approval.
        /// </summary>
        /// <param name="deployment">Deployment.</param>
        /// <returns>The occurrence, or null.</returns>
        public static PushOccurrence? FromDeployment(Deployment deployment)
        {
            if (deployment == null || deployment.Status != DeploymentStatusEnum.PendingApproval) return null;
            string target = !String.IsNullOrWhiteSpace(deployment.EnvironmentName) ? deployment.EnvironmentName! : deployment.Id;
            PushOccurrence occurrence = new PushOccurrence();
            occurrence.Category = PushCategoryEnum.DeploymentApproval;
            occurrence.Kind = PushNotificationKinds.DeploymentApproval;
            occurrence.Title = "Deployment approval";
            occurrence.Body = "A deployment to " + Quote(target) + " is waiting for approval.";
            occurrence.EntityId = deployment.Id;
            occurrence.TenantId = deployment.TenantId;
            occurrence.OwnerUserId = deployment.UserId;
            occurrence.Url = "/deployments/" + deployment.Id;
            return occurrence;
        }

        /// <summary>
        /// Occurrence for a pending CLI permission request. The body names the tool and a truncated, redacted summary
        /// of what it wants to do (never the full input).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>The occurrence.</returns>
        public static PushOccurrence FromCliPermission(CliPermissionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string summary = PushContentFormatter.Summary(request.SummaryText);
            string tool = PushContentFormatter.Clean(request.ToolName, 40);
            PushOccurrence occurrence = new PushOccurrence();
            occurrence.Category = PushCategoryEnum.CliPermission;
            occurrence.Kind = PushNotificationKinds.CliPermission;
            occurrence.Title = "Permission requested";
            occurrence.Body = (String.IsNullOrEmpty(request.CaptainName) ? "A captain" : "Captain " + Quote(request.CaptainName))
                + " wants to use " + (String.IsNullOrEmpty(tool) ? "a tool" : tool)
                + (String.IsNullOrEmpty(summary) ? "." : ": " + summary);
            occurrence.EntityId = request.Id;
            occurrence.TenantId = request.TenantId;
            occurrence.OwnerUserId = request.UserId;
            string pageUrl = "/cli-permissions?request=" + request.Id;
            if (!String.IsNullOrEmpty(request.ThreadId))
            {
                occurrence.Url = "/ask/" + request.ThreadId;
                occurrence.ApproverUrl = pageUrl;
                occurrence.ThreadId = request.ThreadId;
            }
            else
            {
                occurrence.Url = pageUrl;
            }

            occurrence.CliPermission = request;
            return occurrence;
        }

        /// <summary>
        /// Occurrence for a pending Ask proposal (announced to the thread owner).
        /// </summary>
        /// <param name="proposal">Proposal.</param>
        /// <returns>The occurrence.</returns>
        public static PushOccurrence FromAskProposal(AskActionProposal proposal)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            string summary = PushContentFormatter.Summary(String.IsNullOrWhiteSpace(proposal.SummaryText) ? proposal.ToolName : proposal.SummaryText);
            PushOccurrence occurrence = new PushOccurrence();
            occurrence.Category = PushCategoryEnum.AskProposal;
            occurrence.Kind = PushNotificationKinds.AskProposal;
            occurrence.Title = "Approval needed";
            occurrence.Body = "Ask Armada proposes: " + summary;
            occurrence.EntityId = proposal.Id;
            occurrence.TenantId = proposal.TenantId;
            occurrence.OwnerUserId = proposal.UserId;
            occurrence.Url = "/ask/" + proposal.ThreadId;
            occurrence.ThreadId = proposal.ThreadId;
            return occurrence;
        }

        /// <summary>
        /// Stop the worker and release resources.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
            _Queue.Writer.TryComplete();
        }

        #endregion

        #region Private-Methods

        private static TaskCompletionSource<bool> NewCompletedIdle()
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.TrySetResult(true);
            return tcs;
        }

        private void OnDropped(PushOccurrence dropped)
        {
            _Logging.Warn(_Header + "push queue full; dropped " + dropped.Kind + " for " + dropped.EntityId);
            MarkDone();
        }

        private void MarkDone()
        {
            lock (_IdleLock)
            {
                if (_InFlight > 0) _InFlight--;
                if (_InFlight == 0) _Idle.TrySetResult(true);
            }
        }

        private async Task RunWorkerAsync(CancellationToken token)
        {
            try
            {
                while (await _Queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_Queue.Reader.TryRead(out PushOccurrence? occurrence))
                    {
                        try
                        {
                            await DeliverAsync(occurrence, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn(_Header + "push delivery for " + occurrence.Kind + " " + occurrence.EntityId + " failed: " + ex.Message);
                        }
                        finally
                        {
                            MarkDone();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private async Task RunReceiptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_ReceiptPollInterval, _Time, token).ConfigureAwait(false);
                    try
                    {
                        await CheckReceiptsAsync(false, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "push receipt check failed: " + ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private bool IsNewState(string key, string? state)
        {
            lock (_StateLock)
            {
                if (state == null)
                {
                    _NotifiedStates.Remove(key);
                    return false;
                }

                if (_NotifiedStates.TryGetValue(key, out string? previous) && String.Equals(previous, state, StringComparison.Ordinal)) return false;
                if (_NotifiedStates.Count >= _MaxTrackedStates) _NotifiedStates.Clear();
                _NotifiedStates[key] = state;
                return true;
            }
        }

        private bool TryAdmit(string userId, string kind, string entityId, PushSettings push)
        {
            DateTime now = _Time.GetUtcNow().UtcDateTime;
            string key = userId + "|" + kind + "|" + entityId;
            lock (_StateLock)
            {
                if (push.DedupeWindowSeconds > 0
                    && _Dedupe.TryGetValue(key, out DateTime last)
                    && now - last < TimeSpan.FromSeconds(push.DedupeWindowSeconds))
                {
                    _Logging.Debug(_Header + "suppressed duplicate " + kind + " for " + entityId + " to user " + userId);
                    return false;
                }

                if (!TryAdmitRateLocked(userId, push, now)) return false;
                if (push.DedupeWindowSeconds > 0)
                {
                    if (_Dedupe.Count >= _MaxTrackedStates) PruneDedupeLocked(now, push.DedupeWindowSeconds);
                    _Dedupe[key] = now;
                }

                return true;
            }
        }

        private bool TryAdmitRate(string userId, PushSettings push)
        {
            DateTime now = _Time.GetUtcNow().UtcDateTime;
            lock (_StateLock) return TryAdmitRateLocked(userId, push, now);
        }

        private bool TryAdmitRateLocked(string userId, PushSettings push, DateTime now)
        {
            if (!_RateWindows.TryGetValue(userId, out Queue<DateTime>? window))
            {
                window = new Queue<DateTime>();
                _RateWindows[userId] = window;
            }

            while (window.Count > 0 && now - window.Peek() >= TimeSpan.FromMinutes(1)) window.Dequeue();
            if (window.Count >= push.MaxPerUserPerMinute)
            {
                _Logging.Warn(_Header + "push rate limit reached for user " + userId + "; dropping a push");
                return false;
            }

            window.Enqueue(now);
            return true;
        }

        private void PruneDedupeLocked(DateTime now, int windowSeconds)
        {
            List<string> expired = _Dedupe.Where(kvp => now - kvp.Value >= TimeSpan.FromSeconds(windowSeconds)).Select(kvp => kvp.Key).ToList();
            foreach (string key in expired) _Dedupe.Remove(key);
            if (_Dedupe.Count >= _MaxTrackedStates) _Dedupe.Clear();
        }

        private async Task<int> SendAllAsync(List<PushMessage> messages, List<PushDevice> targets, CancellationToken token)
        {
            int accepted = 0;
            for (int i = 0; i < messages.Count; i += ExpoPushTransport.MaxMessagesPerRequest)
            {
                List<PushMessage> batch = messages.Skip(i).Take(ExpoPushTransport.MaxMessagesPerRequest).ToList();
                List<PushDevice> batchTargets = targets.Skip(i).Take(ExpoPushTransport.MaxMessagesPerRequest).ToList();
                try
                {
                    List<PushTicket> tickets = await SendBatchWithRetryAsync(batch, token).ConfigureAwait(false);
                    accepted += await ProcessTicketsAsync(tickets, batchTargets, token).ConfigureAwait(false);
                }
                catch (PushTransportException ex)
                {
                    _Logging.Warn(_Header + "push batch of " + batch.Count + " failed: " + ex.Message);
                }
            }

            return accepted;
        }

        private async Task<List<PushTicket>> SendBatchWithRetryAsync(List<PushMessage> batch, CancellationToken token)
        {
            string? accessToken = AccessToken();
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await _Transport.SendAsync(batch, accessToken, token).ConfigureAwait(false);
                }
                catch (PushTransportException ex) when (ex.Transient && attempt < _MaxAttempts)
                {
                    TimeSpan delay = TimeSpan.FromTicks(_RetryBaseDelay.Ticks * (1L << Math.Min(attempt - 1, 10)));
                    _Logging.Debug(_Header + "transient push failure (attempt " + attempt + " of " + _MaxAttempts + "): " + ex.Message + "; retrying in " + delay.TotalSeconds + "s");
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, _Time, token).ConfigureAwait(false);
                }
            }
        }

        private async Task<int> ProcessTicketsAsync(List<PushTicket> tickets, List<PushDevice> targets, CancellationToken token)
        {
            int accepted = 0;
            DateTime now = _Time.GetUtcNow().UtcDateTime;
            for (int i = 0; i < tickets.Count && i < targets.Count; i++)
            {
                PushTicket ticket = tickets[i];
                PushDevice device = targets[i];
                if (ticket.Ok)
                {
                    accepted++;
                    if (!String.IsNullOrEmpty(ticket.TicketId))
                    {
                        lock (_StateLock)
                        {
                            if (_PendingReceipts.Count < _MaxPendingReceipts)
                                _PendingReceipts[ticket.TicketId!] = new PushPendingReceipt { TicketId = ticket.TicketId!, DeviceId = device.Id, SentUtc = now };
                        }
                    }

                    continue;
                }

                if (ticket.Error == PushErrorCodeEnum.DeviceNotRegistered)
                {
                    await DeactivateAsync(device.Id, "ticket", token).ConfigureAwait(false);
                }
                else
                {
                    _Logging.Warn(_Header + "push to device " + device.Id + " rejected with " + ticket.Error + ": " + ticket.Message);
                }
            }

            return accepted;
        }

        private async Task<bool> DeactivateAsync(string deviceId, string source, CancellationToken token)
        {
            try
            {
                bool updated = await _Database.PushDevices.SetActiveAsync(deviceId, false, token).ConfigureAwait(false);
                if (updated) _Logging.Info(_Header + "deactivated device " + deviceId + " (DeviceNotRegistered " + source + ")");
                return updated;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not deactivate device " + deviceId + ": " + ex.Message);
                return false;
            }
        }

        private void Forget(string ticketId)
        {
            lock (_StateLock) _PendingReceipts.Remove(ticketId);
        }

        private string? AccessToken()
        {
            string? token = _Settings.Push.ExpoAccessToken;
            return String.IsNullOrWhiteSpace(token) ? null : token;
        }

        private static string Quote(string? text)
        {
            string clean = PushContentFormatter.Summary(text);
            return "\"" + (String.IsNullOrEmpty(clean) ? "(untitled)" : clean) + "\"";
        }

        #endregion
    }
}
