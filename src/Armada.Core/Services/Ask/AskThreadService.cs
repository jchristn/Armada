namespace Armada.Core.Services.Ask
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
    /// Ask Armada conversation threads: owner-scoped CRUD, message persistence and paging, unread tracking, automatic
    /// titles, summaries, proposals, and tracked work. Threads are private to the user who created them: every
    /// caller-facing method resolves the thread by (tenant, user, id), so another user's thread reads as not found.
    /// Persisted changes are announced through <see cref="OnUserEvent"/> (ask.message, ask.thread, ask.proposal,
    /// ask.work), which the server routes to the owner's sockets only.
    /// </summary>
    /// <remarks>Thread safety: stateless apart from injected services; safe for concurrent use. Message sequence
    /// numbers are assigned by the database under a per-thread row lock.</remarks>
    public class AskThreadService
    {
        #region Public-Members

        /// <summary>
        /// Default thread title until the first message names the thread.
        /// </summary>
        public const string DefaultTitle = "New conversation";

        /// <summary>
        /// Maximum length of an automatic title taken from the first user message. Default 60, minimum 10, maximum 200.
        /// </summary>
        public int AutoTitleLength
        {
            get => _AutoTitleLength;
            set => _AutoTitleLength = value < 10 ? 10 : (value > 200 ? 200 : value);
        }

        /// <summary>
        /// Sink for owner-scoped events: (tenantId, userId, eventType, payload). Null disables events.
        /// </summary>
        public Action<string, string, string, object>? OnUserEvent { get; set; } = null;

        /// <summary>
        /// Returns the id of the captain turn running in a thread, or null. Null resolver means no turn is ever reported.
        /// </summary>
        public Func<string, string?>? ActiveTurnResolver { get; set; } = null;

        /// <summary>
        /// Snapshot builder used to embed the latest work snapshot in thread details and messages.
        /// </summary>
        public AskWorkSnapshotBuilder Snapshots => _Snapshots;

        #endregion

        #region Private-Members

        private readonly string _Header = "[AskThreadService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly AskWorkSnapshotBuilder _Snapshots;
        private int _AutoTitleLength = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings (Ask section).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AskThreadService(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Snapshots = new AskWorkSnapshotBuilder(database);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a thread owned by the caller.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="request">Optional title, captain, and auto-approve flag.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created thread.</returns>
        /// <exception cref="ArgumentException">Thrown when the captain is not visible to the caller.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller has no tenant or user.</exception>
        public async Task<AskThread> CreateThreadAsync(AuthContext auth, AskThreadCreateRequest? request, CancellationToken token = default)
        {
            RequireOwner(auth);
            request ??= new AskThreadCreateRequest();

            AskThread thread = new AskThread();
            thread.TenantId = auth.TenantId;
            thread.UserId = auth.UserId;
            thread.Title = String.IsNullOrWhiteSpace(request.Title) ? DefaultTitle : request.Title!;
            thread.AutoApprove = request.AutoApprove == true;
            if (!String.IsNullOrWhiteSpace(request.CaptainId))
            {
                Captain? captain = await ReadVisibleCaptainAsync(auth, request.CaptainId!, token).ConfigureAwait(false);
                if (captain == null) throw new ArgumentException("Captain not found: " + request.CaptainId, nameof(request));
                thread.CaptainId = captain.Id;
            }

            thread = await _Database.AskThreads.CreateAsync(thread, token).ConfigureAwait(false);
            await DecorateAsync(thread, token).ConfigureAwait(false);
            Emit(thread, "ask.thread", new { threadId = thread.Id, thread });
            return thread;
        }

        /// <summary>
        /// Read one of the caller's threads.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread with ActiveWorkCount and ActiveTurnId, or null when not found or not owned.</returns>
        public async Task<AskThread?> GetThreadAsync(AuthContext auth, string threadId, CancellationToken token = default)
        {
            AskThread? thread = await ReadOwnedAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return null;
            await DecorateAsync(thread, token).ConfigureAwait(false);
            return thread;
        }

        /// <summary>
        /// Read a thread with its tracked work (each with its latest snapshot) and pending proposals.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The detail, or null when not found or not owned.</returns>
        public async Task<AskThreadDetail?> GetThreadDetailAsync(AuthContext auth, string threadId, CancellationToken token = default)
        {
            AskThread? thread = await ReadOwnedAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return null;

            AskThreadDetail detail = new AskThreadDetail();
            detail.TrackedWork = await _Database.AskTrackedWork.EnumerateByThreadAsync(thread.TenantId!, thread.Id, token).ConfigureAwait(false);
            foreach (AskTrackedWork work in detail.TrackedWork)
            {
                work.Snapshot = await TryBuildSnapshotAsync(work, token).ConfigureAwait(false);
            }

            detail.PendingProposals = await _Database.AskActionProposals.EnumerateByThreadAsync(thread.TenantId!, thread.Id, AskProposalStatusEnum.Pending, token).ConfigureAwait(false);
            foreach (AskActionProposal proposal in detail.PendingProposals) Decorate(proposal);

            thread.ActiveWorkCount = detail.TrackedWork.Count(w => w.State == AskTrackedWorkStateEnum.Active);
            thread.ActiveTurnId = ActiveTurnResolver?.Invoke(thread.Id);
            detail.Thread = thread;
            return detail;
        }

        /// <summary>
        /// Enumerate the caller's threads (pinned first, then most recent activity).
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="request">Paging, search, and archive filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of threads with ActiveWorkCount and ActiveTurnId.</returns>
        public async Task<EnumerationResult<AskThread>> EnumerateThreadsAsync(AuthContext auth, AskThreadEnumerateRequest? request, CancellationToken token = default)
        {
            RequireOwner(auth);
            EnumerationResult<AskThread> result = await _Database.AskThreads.EnumerateAsync(auth.TenantId!, auth.UserId!, request ?? new AskThreadEnumerateRequest(), token).ConfigureAwait(false);
            foreach (AskThread thread in result.Objects) await DecorateAsync(thread, token).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Update one of the caller's threads. Absent fields are unchanged; CaptainId present with null or empty clears it.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="request">Fields to change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated thread, or null when not found or not owned.</returns>
        /// <exception cref="ArgumentException">Thrown when the captain is not visible to the caller.</exception>
        public async Task<AskThread?> UpdateThreadAsync(AuthContext auth, string threadId, AskThreadUpdateRequest? request, CancellationToken token = default)
        {
            AskThread? thread = await ReadOwnedAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return null;
            request ??= new AskThreadUpdateRequest();

            if (request.Title != null) thread.Title = request.Title;
            if (request.AutoApprove.HasValue) thread.AutoApprove = request.AutoApprove.Value;
            if (request.Pinned.HasValue) thread.Pinned = request.Pinned.Value;
            if (request.Archived.HasValue) thread.Archived = request.Archived.Value;
            if (request.CaptainIdSpecified || request.CaptainId != null)
            {
                if (String.IsNullOrWhiteSpace(request.CaptainId))
                {
                    thread.CaptainId = null;
                }
                else
                {
                    Captain? captain = await ReadVisibleCaptainAsync(auth, request.CaptainId!, token).ConfigureAwait(false);
                    if (captain == null) throw new ArgumentException("Captain not found: " + request.CaptainId, nameof(request));
                    thread.CaptainId = captain.Id;
                }
            }

            thread = await _Database.AskThreads.UpdateAsync(thread, token).ConfigureAwait(false);
            await DecorateAsync(thread, token).ConfigureAwait(false);
            Emit(thread, "ask.thread", new { threadId = thread.Id, thread });
            return thread;
        }

        /// <summary>
        /// Delete one of the caller's threads with its messages, tool calls, proposals, and tracked work (the work itself
        /// is untouched).
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted; false when not found or not owned.</returns>
        public async Task<bool> DeleteThreadAsync(AuthContext auth, string threadId, CancellationToken token = default)
        {
            RequireOwner(auth);
            if (String.IsNullOrWhiteSpace(threadId)) return false;
            return await _Database.AskThreads.DeleteAsync(auth.TenantId!, auth.UserId!, threadId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Mark one of the caller's threads read (UnreadCount = 0).
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated thread, or null when not found or not owned.</returns>
        public async Task<AskThread?> MarkReadAsync(AuthContext auth, string threadId, CancellationToken token = default)
        {
            RequireOwner(auth);
            if (String.IsNullOrWhiteSpace(threadId)) return null;
            bool updated = await _Database.AskThreads.MarkReadAsync(auth.TenantId!, auth.UserId!, threadId, token).ConfigureAwait(false);
            if (!updated) return null;
            AskThread? thread = await GetThreadAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread != null) Emit(thread, "ask.thread", new { threadId = thread.Id, thread });
            return thread;
        }

        /// <summary>
        /// Read a page of a thread's messages, newest page first when BeforeSequence is null, each with its tool calls,
        /// proposal, and tracked work (with snapshot) populated. Messages within the page are in ascending sequence.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="request">Paging request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page, or null when the thread is not found or not owned.</returns>
        public async Task<AskMessagePage?> EnumerateMessagesAsync(AuthContext auth, string threadId, AskMessageEnumerateRequest? request, CancellationToken token = default)
        {
            AskThread? thread = await ReadOwnedAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null) return null;
            request ??= new AskMessageEnumerateRequest();

            AskMessagePage page = await _Database.AskMessages.EnumerateAsync(thread.TenantId!, thread.Id, request.BeforeSequence, request.PageSize, token).ConfigureAwait(false);
            await PopulateAsync(thread, page.Messages, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// Read the most recent messages of a thread (for prompt history), oldest first, without nested data.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="count">Maximum messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages in ascending sequence.</returns>
        public async Task<List<AskMessage>> ReadRecentMessagesAsync(AskThread thread, int count, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            AskMessagePage page = await _Database.AskMessages.EnumerateAsync(thread.TenantId!, thread.Id, null, count, token).ConfigureAwait(false);
            return page.Messages;
        }

        /// <summary>
        /// Read a thread by id regardless of owner (server-internal: MCP gate, work tracker, turn runner).
        /// </summary>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread, or null.</returns>
        public async Task<AskThread?> ReadThreadInternalAsync(string threadId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(threadId)) return null;
            return await _Database.AskThreads.ReadByIdAsync(threadId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Append a message to a thread, store its tool calls, and announce it (ask.message with nested data, then
        /// ask.thread with the new counters).
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="message">Message; tenant, user, and thread are filled in from the thread.</param>
        /// <param name="countsAsUnread">Whether the message increments the unread counter.</param>
        /// <param name="toolCalls">Tool calls to store with the message, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored message with its sequence and nested data.</returns>
        public async Task<AskMessage> AppendMessageAsync(AskThread thread, AskMessage message, bool countsAsUnread, List<AskMessageToolCall>? toolCalls = null, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (message == null) throw new ArgumentNullException(nameof(message));
            message.TenantId = thread.TenantId;
            message.UserId = thread.UserId;
            message.ThreadId = thread.Id;
            message = await _Database.AskMessages.CreateAsync(message, countsAsUnread, token).ConfigureAwait(false);

            if (toolCalls != null && toolCalls.Count > 0)
            {
                foreach (AskMessageToolCall call in toolCalls)
                {
                    call.TenantId = thread.TenantId;
                    call.UserId = thread.UserId;
                    call.ThreadId = thread.Id;
                    call.MessageId = message.Id;
                }

                await _Database.AskMessageToolCalls.CreateManyAsync(toolCalls, token).ConfigureAwait(false);
            }

            await PopulateAsync(thread, new List<AskMessage> { message }, token).ConfigureAwait(false);
            Emit(thread, "ask.message", new { threadId = thread.Id, message });
            await EmitThreadAsync(thread.Id, token).ConfigureAwait(false);
            return message;
        }

        /// <summary>
        /// Finish a message that was reserved earlier with <see cref="AppendMessageAsync"/> (for example the
        /// captain's reply, reserved when the turn starts so that confirm cards and work updates posted while the
        /// captain is still writing appear after it). Updates the content and kind, stores the tool calls, and
        /// emits <c>ask.message</c> with the final message.
        /// </summary>
        /// <param name="thread">Thread that owns the message.</param>
        /// <param name="message">The reserved message with its final content.</param>
        /// <param name="toolCalls">Tool calls made while producing it, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when thread or message is null.</exception>
        public async Task<AskMessage> CompleteMessageAsync(AskThread thread, AskMessage message, List<AskMessageToolCall>? toolCalls = null, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (message == null) throw new ArgumentNullException(nameof(message));
            message = await _Database.AskMessages.UpdateAsync(message, token).ConfigureAwait(false);

            if (toolCalls != null && toolCalls.Count > 0)
            {
                foreach (AskMessageToolCall call in toolCalls)
                {
                    call.TenantId = thread.TenantId;
                    call.UserId = thread.UserId;
                    call.ThreadId = thread.Id;
                    call.MessageId = message.Id;
                }

                await _Database.AskMessageToolCalls.CreateManyAsync(toolCalls, token).ConfigureAwait(false);
            }

            await PopulateAsync(thread, new List<AskMessage> { message }, token).ConfigureAwait(false);
            Emit(thread, "ask.message", new { threadId = thread.Id, message });
            await EmitThreadAsync(thread.Id, token).ConfigureAwait(false);
            return message;
        }

        /// <summary>
        /// Name a thread after its first user message when it still has the default title.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="content">First user message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread (renamed when applicable).</returns>
        public async Task<AskThread> ApplyAutoTitleAsync(AskThread thread, string content, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (!String.Equals(thread.Title, DefaultTitle, StringComparison.Ordinal)) return thread;
            string title = BuildAutoTitle(content, _AutoTitleLength);
            if (String.IsNullOrWhiteSpace(title)) return thread;
            thread.Title = title;
            return await _Database.AskThreads.UpdateAsync(thread, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Store a conversation summary on the thread and post it as a Summary message.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="summary">Summary text.</param>
        /// <param name="captainId">Captain that wrote it, or null for a deterministic summary.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The Summary message.</returns>
        public async Task<AskMessage> SetSummaryAsync(AskThread thread, string summary, string? captainId, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            AskThread? fresh = await _Database.AskThreads.ReadByIdAsync(thread.Id, token).ConfigureAwait(false) ?? thread;
            fresh.SummaryText = summary;
            fresh.SummaryUtc = DateTime.UtcNow;
            await _Database.AskThreads.UpdateAsync(fresh, token).ConfigureAwait(false);

            AskMessage message = new AskMessage();
            message.Role = captainId != null ? AskMessageRoleEnum.Assistant : AskMessageRoleEnum.System;
            message.Kind = AskMessageKindEnum.Summary;
            message.ContentText = summary;
            message.CaptainId = captainId;
            return await AppendMessageAsync(fresh, message, true, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a proposal and, unless suppressed, the ActionProposal message that renders its confirm card.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="proposal">Proposal; tenant, user, and thread are filled in.</param>
        /// <param name="postCard">Whether to post an ActionProposal message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored proposal.</returns>
        public async Task<AskActionProposal> CreateProposalAsync(AskThread thread, AskActionProposal proposal, bool postCard, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            proposal.TenantId = thread.TenantId;
            proposal.UserId = thread.UserId;
            proposal.ThreadId = thread.Id;
            proposal = await _Database.AskActionProposals.CreateAsync(proposal, token).ConfigureAwait(false);

            if (postCard)
            {
                AskMessage card = new AskMessage();
                card.Role = AskMessageRoleEnum.System;
                card.Kind = AskMessageKindEnum.ActionProposal;
                card.ContentText = proposal.SummaryText;
                card.ProposalId = proposal.Id;
                card.CaptainId = thread.CaptainId;
                card = await AppendMessageAsync(thread, card, true, null, token).ConfigureAwait(false);
                proposal.MessageId = card.Id;
                proposal = await _Database.AskActionProposals.UpdateAsync(proposal, token).ConfigureAwait(false);
            }

            Decorate(proposal);
            Emit(thread, "ask.proposal", new { threadId = thread.Id, proposal });
            return proposal;
        }

        /// <summary>
        /// Persist a proposal change and announce it (ask.proposal).
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="proposal">Proposal.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored proposal.</returns>
        public async Task<AskActionProposal> SaveProposalAsync(AskThread thread, AskActionProposal proposal, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            proposal = await _Database.AskActionProposals.UpdateAsync(proposal, token).ConfigureAwait(false);
            Decorate(proposal);
            Emit(thread, "ask.proposal", new { threadId = thread.Id, proposal });
            return proposal;
        }

        /// <summary>
        /// Announce a proposal without saving it (after a compare-and-set transition).
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="proposal">Proposal.</param>
        public void AnnounceProposal(AskThread thread, AskActionProposal proposal)
        {
            if (thread == null || proposal == null) return;
            Decorate(proposal);
            Emit(thread, "ask.proposal", new { threadId = thread.Id, proposal });
        }

        /// <summary>
        /// Start tracking work in a thread, or return the existing row.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="link">Work to track.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tracked work row.</returns>
        public async Task<AskTrackedWork> TrackWorkAsync(AskThread thread, AskWorkLink link, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (link == null) throw new ArgumentNullException(nameof(link));
            AskTrackedWork work = new AskTrackedWork();
            work.TenantId = thread.TenantId;
            work.UserId = thread.UserId;
            work.ThreadId = thread.Id;
            work.EntityType = link.EntityType;
            work.EntityId = link.EntityId;
            work.Title = link.EntityType + " " + link.EntityId;
            work.LastChangeUtc = DateTime.UtcNow;
            return await _Database.AskTrackedWork.CreateOrGetAsync(work, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Find the row tracking an entity in a thread, if any.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="link">Entity.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The row, or null.</returns>
        public async Task<AskTrackedWork?> FindTrackedWorkAsync(AskThread thread, AskWorkLink link, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (link == null) throw new ArgumentNullException(nameof(link));
            List<AskTrackedWork> rows = await _Database.AskTrackedWork.EnumerateByThreadAsync(thread.TenantId!, thread.Id, token).ConfigureAwait(false);
            return rows.FirstOrDefault(w => w.EntityType == link.EntityType && String.Equals(w.EntityId, link.EntityId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Read one tracked work item of one of the caller's threads with a freshly built snapshot.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="workId">Tracked work identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The snapshot, or null when the thread or item is not found or not owned.</returns>
        public async Task<AskWorkSnapshot?> GetWorkSnapshotAsync(AuthContext auth, string threadId, string workId, CancellationToken token = default)
        {
            AskThread? thread = await ReadOwnedAsync(auth, threadId, token).ConfigureAwait(false);
            if (thread == null || String.IsNullOrWhiteSpace(workId)) return null;
            AskTrackedWork? work = await _Database.AskTrackedWork.ReadAsync(thread.TenantId!, workId, token).ConfigureAwait(false);
            if (work == null || !String.Equals(work.ThreadId, thread.Id, StringComparison.Ordinal)) return null;
            return await _Snapshots.BuildAsync(work, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Announce a work snapshot to the owner (ask.work).
        /// </summary>
        /// <param name="work">Tracked work row.</param>
        /// <param name="snapshot">Snapshot.</param>
        public void AnnounceWork(AskTrackedWork work, AskWorkSnapshot snapshot)
        {
            if (work == null || snapshot == null) return;
            if (String.IsNullOrEmpty(work.TenantId) || String.IsNullOrEmpty(work.UserId)) return;
            work.Snapshot = snapshot;
            Send(work.TenantId!, work.UserId!, "ask.work", new { threadId = work.ThreadId, trackedWorkId = work.Id, snapshot, trackedWork = work });
        }

        /// <summary>
        /// Announce the current state of a thread to its owner (ask.thread).
        /// </summary>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EmitThreadAsync(string threadId, CancellationToken token = default)
        {
            AskThread? thread = await _Database.AskThreads.ReadByIdAsync(threadId, token).ConfigureAwait(false);
            if (thread == null) return;
            await DecorateAsync(thread, token).ConfigureAwait(false);
            Emit(thread, "ask.thread", new { threadId = thread.Id, thread });
        }

        /// <summary>
        /// Send an owner-scoped event for a thread.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="eventType">Event type.</param>
        /// <param name="payload">Payload.</param>
        public void Emit(AskThread thread, string eventType, object payload)
        {
            if (thread == null || String.IsNullOrEmpty(thread.TenantId) || String.IsNullOrEmpty(thread.UserId)) return;
            Send(thread.TenantId!, thread.UserId!, eventType, payload);
        }

        /// <summary>
        /// Fill in computed proposal fields (ExpiresUtc for pending proposals).
        /// </summary>
        /// <param name="proposal">Proposal.</param>
        public void Decorate(AskActionProposal proposal)
        {
            if (proposal == null) return;
            proposal.ExpiresUtc = proposal.Status == AskProposalStatusEnum.Pending
                ? proposal.CreatedUtc.AddMinutes(_Settings.Ask.ProposalExpiryMinutes)
                : (DateTime?)null;
        }

        /// <summary>
        /// Build an automatic title from a message: the first line, trimmed to a word boundary.
        /// </summary>
        /// <param name="content">Message text.</param>
        /// <param name="maxLength">Maximum length (10-200).</param>
        /// <returns>The title; empty when the message is blank.</returns>
        public static string BuildAutoTitle(string? content, int maxLength)
        {
            if (String.IsNullOrWhiteSpace(content)) return String.Empty;
            int max = maxLength < 10 ? 10 : (maxLength > 200 ? 200 : maxLength);
            string line = content.Trim().Split('\n')[0].Trim();
            line = System.Text.RegularExpressions.Regex.Replace(line, "\\s+", " ");
            if (line.Length <= max) return line;
            string cut = line.Substring(0, max);
            int space = cut.LastIndexOf(' ');
            if (space >= max / 2) cut = cut.Substring(0, space);
            return cut.TrimEnd(' ', ',', '.', ';', ':') + "...";
        }

        #endregion

        #region Private-Methods

        private static void RequireOwner(AuthContext auth)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrEmpty(auth.TenantId) || String.IsNullOrEmpty(auth.UserId))
                throw new UnauthorizedAccessException("Ask Armada threads require an authenticated user.");
        }

        private async Task<AskThread?> ReadOwnedAsync(AuthContext auth, string threadId, CancellationToken token)
        {
            RequireOwner(auth);
            if (String.IsNullOrWhiteSpace(threadId)) return null;
            return await _Database.AskThreads.ReadAsync(auth.TenantId!, auth.UserId!, threadId, token).ConfigureAwait(false);
        }

        private async Task<Captain?> ReadVisibleCaptainAsync(AuthContext auth, string captainId, CancellationToken token)
        {
            // Same visibility as captain chat: a global admin any, a tenant admin any in their tenant, a user their own.
            if (auth.IsAdmin) return await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin) return await _Database.Captains.ReadAsync(auth.TenantId!, captainId, token).ConfigureAwait(false);
            return await _Database.Captains.ReadAsync(auth.TenantId!, auth.UserId!, captainId, token).ConfigureAwait(false);
        }

        private async Task DecorateAsync(AskThread thread, CancellationToken token)
        {
            List<AskTrackedWork> work = await _Database.AskTrackedWork.EnumerateByThreadAsync(thread.TenantId!, thread.Id, token).ConfigureAwait(false);
            thread.ActiveWorkCount = work.Count(w => w.State == AskTrackedWorkStateEnum.Active);
            thread.ActiveTurnId = ActiveTurnResolver?.Invoke(thread.Id);
        }

        private async Task PopulateAsync(AskThread thread, List<AskMessage> messages, CancellationToken token)
        {
            if (messages.Count == 0) return;
            List<AskMessageToolCall> calls = await _Database.AskMessageToolCalls.EnumerateByMessagesAsync(thread.TenantId!, thread.Id, messages.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
            Dictionary<string, AskActionProposal?> proposals = new Dictionary<string, AskActionProposal?>(StringComparer.Ordinal);
            Dictionary<string, AskTrackedWork?> works = new Dictionary<string, AskTrackedWork?>(StringComparer.Ordinal);

            foreach (AskMessage message in messages)
            {
                message.ToolCalls = calls.Where(c => String.Equals(c.MessageId, message.Id, StringComparison.Ordinal)).ToList();

                if (!String.IsNullOrEmpty(message.ProposalId))
                {
                    if (!proposals.TryGetValue(message.ProposalId!, out AskActionProposal? proposal))
                    {
                        proposal = await _Database.AskActionProposals.ReadAsync(thread.TenantId!, message.ProposalId!, token).ConfigureAwait(false);
                        if (proposal != null) Decorate(proposal);
                        proposals[message.ProposalId!] = proposal;
                    }

                    message.Proposal = proposal;
                }

                if (!String.IsNullOrEmpty(message.TrackedWorkId))
                {
                    if (!works.TryGetValue(message.TrackedWorkId!, out AskTrackedWork? work))
                    {
                        work = await _Database.AskTrackedWork.ReadAsync(thread.TenantId!, message.TrackedWorkId!, token).ConfigureAwait(false);
                        if (work != null) work.Snapshot = await TryBuildSnapshotAsync(work, token).ConfigureAwait(false);
                        works[message.TrackedWorkId!] = work;
                    }

                    message.TrackedWork = work;
                }
            }
        }

        private async Task<AskWorkSnapshot?> TryBuildSnapshotAsync(AskTrackedWork work, CancellationToken token)
        {
            try
            {
                return await _Snapshots.BuildAsync(work, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "snapshot of " + work.EntityType + " " + work.EntityId + " failed: " + ex.Message);
                return null;
            }
        }

        private void Send(string tenantId, string userId, string eventType, object payload)
        {
            Action<string, string, string, object>? sink = OnUserEvent;
            if (sink == null) return;
            try { sink(tenantId, userId, eventType, payload); }
            catch (Exception ex) { _Logging.Warn(_Header + "event " + eventType + " delivery failed: " + ex.Message); }
        }

        #endregion
    }
}
