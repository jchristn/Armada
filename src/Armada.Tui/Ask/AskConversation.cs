namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// State of the open conversation and the rules that change it, ported from the dashboard's
    /// <c>conversationReducer</c> (<c>lib/askConversation.ts</c>): page loads, older pages, reconciliation refetches,
    /// socket events, optimistic sends, proposals (a decided proposal never regresses to a stale Pending or Approved
    /// copy), live work snapshots, and turn lifecycle. Events for other threads are ignored. <see cref="Version"/>
    /// increases on every change so views can cache their layout. Not thread-safe; use on the UI loop.
    /// </summary>
    public class AskConversation
    {
        #region Public-Members

        /// <summary>
        /// Prefix of optimistic (not yet persisted) message ids.
        /// </summary>
        public const string LocalPrefix = "local-";

        /// <summary>
        /// Open thread id, or null for a new (not yet created) conversation.
        /// </summary>
        public string? ThreadId { get; private set; } = null;

        /// <summary>
        /// Open thread, or null until loaded.
        /// </summary>
        public AskThread? Thread { get; private set; } = null;

        /// <summary>
        /// Messages in sequence order (optimistic ones last among equal sequences). Never null.
        /// </summary>
        public List<AskMessage> Messages { get; private set; } = new List<AskMessage>();

        /// <summary>
        /// Older messages exist on the server.
        /// </summary>
        public bool HasMore { get; private set; } = false;

        /// <summary>
        /// Tracked work in the conversation. Never null.
        /// </summary>
        public List<AskTrackedWork> TrackedWork { get; private set; } = new List<AskTrackedWork>();

        /// <summary>
        /// Latest snapshot per tracked work id. Never null.
        /// </summary>
        public Dictionary<string, AskWorkSnapshot> Snapshots { get; } = new Dictionary<string, AskWorkSnapshot>(StringComparer.Ordinal);

        /// <summary>
        /// Latest known state of every proposal by id. Never null.
        /// </summary>
        public Dictionary<string, AskActionProposal> Proposals { get; } = new Dictionary<string, AskActionProposal>(StringComparer.Ordinal);

        /// <summary>
        /// The reply being streamed, or null.
        /// </summary>
        public AskStreamingTurn? Streaming { get; private set; } = null;

        /// <summary>
        /// A captain turn is running.
        /// </summary>
        public bool TurnActive { get; private set; } = false;

        /// <summary>
        /// Last turn failure reason, shown until the next turn, or null.
        /// </summary>
        public string? TurnError { get; private set; } = null;

        /// <summary>
        /// Locally measured metrics of finished turns, by persisted message id. Never null.
        /// </summary>
        public Dictionary<string, AskTurnMetrics> Metrics { get; } = new Dictionary<string, AskTurnMetrics>(StringComparer.Ordinal);

        /// <summary>
        /// Increases on every change.
        /// </summary>
        public long Version { get; private set; } = 0;

        /// <summary>
        /// Most messages kept in memory (W8.5 memory cap). When live messages push the transcript past it, the oldest
        /// are dropped and <see cref="HasMore"/> is set so "Load earlier messages" can fetch them again. Older pages the
        /// user loads explicitly are kept. Default 10,000; clamped to 100..1,000,000.
        /// </summary>
        public int MaxMessages
        {
            get { return _MaxMessages; }
            set { _MaxMessages = Math.Clamp(value, 100, 1000000); }
        }

        #endregion

        #region Private-Members

        private int _MaxMessages = 10000;
        private readonly HashSet<string> _ClosedTurns = new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<AskProposalStatusEnum> _DecidedStatuses = new HashSet<AskProposalStatusEnum>
        {
            AskProposalStatusEnum.Executed, AskProposalStatusEnum.Failed, AskProposalStatusEnum.Rejected, AskProposalStatusEnum.Expired
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a thread (or a new conversation).
        /// </summary>
        /// <param name="threadId">Thread id, or null.</param>
        public AskConversation(string? threadId = null)
        {
            ThreadId = threadId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True for an optimistic message id.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>True when local.</returns>
        public static bool IsLocal(AskMessage message)
        {
            return message.Id.StartsWith(LocalPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// True for a decided proposal status (Executed, Failed, Rejected, Expired).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when decided.</returns>
        public static bool IsDecided(AskProposalStatusEnum status)
        {
            return _DecidedStatuses.Contains(status);
        }

        /// <summary>
        /// Merge two copies of a proposal: a decided copy never goes back to Pending or Approved from a stale copy.
        /// </summary>
        /// <param name="previous">Known copy, or null.</param>
        /// <param name="next">Incoming copy.</param>
        /// <returns>The copy to keep.</returns>
        public static AskActionProposal MergeProposal(AskActionProposal? previous, AskActionProposal next)
        {
            if (previous == null) return next;
            if (IsDecided(previous.Status) && !IsDecided(next.Status)) return previous;
            return next;
        }

        /// <summary>
        /// Merge messages by id (incoming wins), drop optimistic copies the server has now persisted (same text at or
        /// after their position), and sort by sequence.
        /// </summary>
        /// <param name="existing">Known messages.</param>
        /// <param name="incoming">Incoming messages.</param>
        /// <returns>Merged list.</returns>
        public static List<AskMessage> MergeMessages(IEnumerable<AskMessage> existing, IEnumerable<AskMessage> incoming)
        {
            Dictionary<string, AskMessage> byId = new Dictionary<string, AskMessage>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            foreach (AskMessage m in existing)
            {
                if (!byId.ContainsKey(m.Id)) order.Add(m.Id);
                byId[m.Id] = m;
            }

            List<AskMessage> incomingList = incoming.Where(m => m != null && !String.IsNullOrEmpty(m.Id)).ToList();
            foreach (AskMessage m in incomingList)
            {
                if (!byId.ContainsKey(m.Id)) order.Add(m.Id);
                byId[m.Id] = m;
            }

            List<AskMessage> persistedUsers = incomingList.Where(m => !IsLocal(m) && m.Role == AskMessageRoleEnum.User).ToList();
            List<AskMessage> merged = order.Select(id => byId[id])
                .Where(m => !(IsLocal(m) && persistedUsers.Any(p => p.ContentText.Trim() == m.ContentText.Trim() && p.Sequence >= m.Sequence)))
                .ToList();
            return merged
                .Select((m, i) => new KeyValuePair<int, AskMessage>(i, m))
                .OrderBy(p => p.Value.Sequence)
                .ThenBy(p => IsLocal(p.Value) ? 1 : 0)
                .ThenBy(p => p.Key)
                .Select(p => p.Value)
                .ToList();
        }

        /// <summary>
        /// The proposal to render for a message: the latest event-driven copy wins over the embedded one (subject to
        /// <see cref="MergeProposal"/>).
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>Proposal or null.</returns>
        public AskActionProposal? ProposalFor(AskMessage message)
        {
            string? id = message.ProposalId ?? message.Proposal?.Id;
            if (id != null && Proposals.TryGetValue(id, out AskActionProposal? known))
                return message.Proposal != null ? MergeProposal(message.Proposal, known) : known;
            return message.Proposal;
        }

        /// <summary>
        /// Which message hosts each tracked item's full live card: the first non-milestone message that references it,
        /// else the first milestone.
        /// </summary>
        /// <returns>Work id to message id.</returns>
        public Dictionary<string, string> WorkCardHosts()
        {
            Dictionary<string, string> hosts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (AskMessage m in Messages)
            {
                string? workId = m.TrackedWorkId ?? m.TrackedWork?.Id;
                if (workId == null || hosts.ContainsKey(workId) || m.Kind == AskMessageKindEnum.WorkUpdate) continue;
                hosts[workId] = m.Id;
            }

            foreach (AskMessage m in Messages)
            {
                string? workId = m.TrackedWorkId ?? m.TrackedWork?.Id;
                if (workId != null && !hosts.ContainsKey(workId)) hosts[workId] = m.Id;
            }

            return hosts;
        }

        /// <summary>
        /// The tracked work row for an id, or null.
        /// </summary>
        /// <param name="workId">Work id.</param>
        /// <returns>Row or null.</returns>
        public AskTrackedWork? Work(string workId)
        {
            return TrackedWork.FirstOrDefault(w => w.Id == workId);
        }

        /// <summary>
        /// The snapshot for a tracked item (the latest event or fetch, else the embedded one), or null.
        /// </summary>
        /// <param name="workId">Work id.</param>
        /// <returns>Snapshot or null.</returns>
        public AskWorkSnapshot? SnapshotFor(string workId)
        {
            if (Snapshots.TryGetValue(workId, out AskWorkSnapshot? s)) return s;
            return Work(workId)?.Snapshot;
        }

        /// <summary>
        /// Pending proposals in the conversation, oldest first.
        /// </summary>
        /// <returns>Proposals.</returns>
        public List<AskActionProposal> PendingProposals()
        {
            return Proposals.Values.Where(p => p.Status == AskProposalStatusEnum.Pending).OrderBy(p => p.CreatedUtc).ToList();
        }

        /// <summary>
        /// Start over for another thread (or a new conversation).
        /// </summary>
        /// <param name="threadId">Thread id, or null.</param>
        public void Reset(string? threadId)
        {
            ThreadId = threadId;
            Thread = null;
            Messages = new List<AskMessage>();
            HasMore = false;
            TrackedWork = new List<AskTrackedWork>();
            Snapshots.Clear();
            Proposals.Clear();
            Streaming = null;
            TurnActive = false;
            TurnError = null;
            Metrics.Clear();
            _ClosedTurns.Clear();
            Changed();
        }

        /// <summary>
        /// Apply a full load (thread detail and the newest page). Keeps optimistic messages the server has not
        /// persisted yet. The thread's active turn is authoritative for whether a turn is running.
        /// </summary>
        /// <param name="threadId">Thread id the load was for.</param>
        /// <param name="detail">Detail.</param>
        /// <param name="messages">Newest page.</param>
        /// <param name="hasMore">Older pages exist.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>False when the load was for another thread.</returns>
        public bool Loaded(string threadId, AskThreadDetail? detail, IEnumerable<AskMessage> messages, bool hasMore, DateTime nowUtc)
        {
            if (threadId != ThreadId) return false;
            List<AskMessage> incoming = messages.ToList();
            Messages = MergeMessages(Messages.Where(IsLocal).ToList(), incoming);
            TrackedWork = new List<AskTrackedWork>();
            foreach (AskTrackedWork w in detail?.TrackedWork ?? new List<AskTrackedWork>()) UpsertWork(w);
            IndexWork(Messages);
            if (detail?.Thread != null)
            {
                Thread = detail.Thread;
                string? activeTurn = String.IsNullOrEmpty(detail.Thread.ActiveTurnId) ? null : detail.Thread.ActiveTurnId;
                AskStreamingTurn? streaming = Streaming != null && !Streaming.Finished ? Streaming : null;
                TurnActive = activeTurn != null;
                Streaming = activeTurn != null ? (streaming != null && streaming.TurnId == activeTurn ? streaming : new AskStreamingTurn(activeTurn, nowUtc)) : null;
            }

            HasMore = hasMore;
            IndexProposals(Messages, detail?.PendingProposals);
            Changed();
            return true;
        }

        /// <summary>
        /// Apply an older page (prepended).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="messages">Messages.</param>
        /// <param name="hasMore">Even older pages exist.</param>
        /// <returns>False when for another thread.</returns>
        public bool Older(string threadId, IEnumerable<AskMessage> messages, bool hasMore)
        {
            if (threadId != ThreadId) return false;
            List<AskMessage> incoming = messages.ToList();
            Messages = MergeMessages(Messages, incoming);
            IndexWork(incoming);
            IndexProposals(incoming, null);
            HasMore = hasMore;
            Changed();
            return true;
        }

        /// <summary>
        /// Apply a reconciliation refetch of the newest page; drops a finished stream (its message is now persisted).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="messages">Messages.</param>
        /// <returns>False when for another thread.</returns>
        public bool Latest(string threadId, IEnumerable<AskMessage> messages)
        {
            if (threadId != ThreadId) return false;
            List<AskMessage> incoming = messages.ToList();
            Messages = MergeMessages(Messages, incoming);
            TrimOldest();
            IndexWork(incoming);
            IndexProposals(incoming, null);
            if (Streaming != null && Streaming.Finished) Streaming = null;
            Changed();
            return true;
        }

        /// <summary>
        /// Apply a thread detail refetch (tracked work, snapshots, pending proposals).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="detail">Detail.</param>
        /// <returns>False when for another thread.</returns>
        public bool Detail(string threadId, AskThreadDetail? detail)
        {
            if (threadId != ThreadId || detail == null) return false;
            foreach (AskTrackedWork w in detail.TrackedWork ?? new List<AskTrackedWork>()) UpsertWork(w);
            foreach (AskTrackedWork w in TrackedWork)
            {
                if (w.Snapshot != null) Snapshots[w.Id] = w.Snapshot;
            }

            if (detail.Thread != null) Thread = detail.Thread;
            IndexProposals(new List<AskMessage>(), detail.PendingProposals);
            Changed();
            return true;
        }

        /// <summary>
        /// Add an optimistic user message and mark a turn active.
        /// </summary>
        /// <param name="message">Message with a <see cref="LocalPrefix"/> id.</param>
        public void OptimisticUser(AskMessage message)
        {
            Messages = MergeMessages(Messages, new List<AskMessage> { message });
            TrimOldest();
            TurnActive = true;
            TurnError = null;
            Changed();
        }

        /// <summary>
        /// Confirm an optimistic message with the server's ids and start following the turn.
        /// </summary>
        /// <param name="localId">Local id.</param>
        /// <param name="messageId">Persisted message id, or null.</param>
        /// <param name="turnId">Turn id, or null.</param>
        /// <param name="nowUtc">Now.</param>
        public void ConfirmUser(string localId, string? messageId, string? turnId, DateTime nowUtc)
        {
            if (!String.IsNullOrEmpty(messageId))
            {
                foreach (AskMessage m in Messages)
                {
                    if (m.Id == localId) m.Id = messageId!;
                }
            }

            Messages = MergeMessages(new List<AskMessage>(), Messages);
            if (!String.IsNullOrEmpty(turnId) && (Streaming == null || Streaming.TurnId != turnId))
            {
                if (Streaming != null && !Streaming.Finished && Streaming.TextLength == 0 && Streaming.Tools.Count == 0)
                {
                    AskStreamingTurn replacement = new AskStreamingTurn(turnId!, Streaming.StartedUtc);
                    replacement.AppendThinking(Streaming.Thinking);
                    Streaming = replacement;
                }
                else
                {
                    Streaming = new AskStreamingTurn(turnId!, nowUtc);
                }
            }

            if (!String.IsNullOrEmpty(turnId)) TurnActive = true;
            Changed();
        }

        /// <summary>
        /// Remove an optimistic message after a failed send.
        /// </summary>
        /// <param name="localId">Local id.</param>
        public void DropOptimistic(string localId)
        {
            Messages = Messages.Where(m => m.Id != localId).ToList();
            TurnActive = false;
            Changed();
        }

        /// <summary>
        /// Record a proposal copy (from an event, a decision, or a quick action).
        /// </summary>
        /// <param name="proposal">Proposal.</param>
        public void ApplyProposal(AskActionProposal proposal)
        {
            if (proposal == null || String.IsNullOrEmpty(proposal.Id)) return;
            Proposals.TryGetValue(proposal.Id, out AskActionProposal? previous);
            Proposals[proposal.Id] = MergeProposal(previous, proposal);
            Changed();
        }

        /// <summary>
        /// Record a fetched snapshot.
        /// </summary>
        /// <param name="workId">Tracked work id.</param>
        /// <param name="snapshot">Snapshot.</param>
        public void ApplySnapshot(string workId, AskWorkSnapshot snapshot)
        {
            if (snapshot == null) return;
            Snapshots[workId] = snapshot;
            AskTrackedWork? w = Work(workId);
            if (w != null) AskWorkLogic.ApplySnapshot(w, snapshot);
            Changed();
        }

        /// <summary>
        /// Record an updated copy of the open thread.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <returns>False when for another thread.</returns>
        public bool ApplyThread(AskThread thread)
        {
            if (thread == null || thread.Id != ThreadId) return false;
            Thread = thread;
            Changed();
            return true;
        }

        /// <summary>
        /// Settle a turn locally (the stop grace period elapsed without an <c>ask.turn</c>).
        /// </summary>
        public void TurnEnded()
        {
            TurnActive = false;
            if (Streaming != null && Streaming.TextLength == 0 && Streaming.Tools.Count == 0) Streaming = null;
            Changed();
        }

        /// <summary>
        /// Apply a socket event for the open thread.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="nowUtc">Arrival time.</param>
        /// <returns>True when applied (false for other threads).</returns>
        public bool Apply(AskEvent e, DateTime nowUtc)
        {
            if (e == null || ThreadId == null || e.ThreadId != ThreadId) return false;
            switch (e.Type)
            {
                case "ask.chunk":
                case "ask.thinking":
                case "ask.tool":
                    return ApplyStream(e, nowUtc);
                case "ask.turn":
                    return ApplyTurn(e, nowUtc);
                case "ask.message":
                    return ApplyMessage(e);
                case "ask.proposal":
                    if (e.Proposal != null) ApplyProposal(e.Proposal);
                    return true;
                case "ask.work":
                    return ApplyWork(e);
                case "ask.thread":
                    if (e.Thread != null)
                    {
                        Thread = e.Thread;
                        Changed();
                    }

                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Private-Methods

        private void Changed()
        {
            Version++;
        }

        private void TrimOldest()
        {
            if (Messages.Count <= _MaxMessages) return;
            Messages = Messages.GetRange(Messages.Count - _MaxMessages, _MaxMessages);
            HasMore = true;
        }

        private bool ApplyStream(AskEvent e, DateTime nowUtc)
        {
            // Late events for a turn that already finished are ignored (even after its stream was replaced).
            if (_ClosedTurns.Contains(e.TurnId)) return false;
            AskStreamingTurn? stream = Streaming != null && Streaming.TurnId == e.TurnId ? Streaming : null;
            if (stream == null)
            {
                // A late event for a turn that already closed is ignored; otherwise follow the new turn.
                if (Streaming != null && Streaming.Finished && Streaming.TurnId == e.TurnId) return false;
                stream = new AskStreamingTurn(e.TurnId, nowUtc);
            }
            else if (stream.Finished)
            {
                return false;
            }

            if (e.Type == "ask.chunk") stream.AppendText(e.Delta, nowUtc);
            else if (e.Type == "ask.thinking") stream.AppendThinking(e.Delta);
            else ApplyTool(stream.Tools, e);
            Streaming = stream;
            TurnActive = true;
            Changed();
            return true;
        }

        private static void ApplyTool(List<AskToolChip> tools, AskEvent e)
        {
            if (String.IsNullOrEmpty(e.ToolId)) return;
            int idx = tools.FindIndex(t => t.Id == e.ToolId);
            if (e.ToolPhase == "started")
            {
                if (idx >= 0) return;
                AskToolChip chip = new AskToolChip();
                chip.Id = e.ToolId!;
                chip.Name = e.ToolName ?? "tool";
                chip.Status = AskToolChipStatusEnum.Running;
                chip.Arguments = e.ToolArguments;
                tools.Add(chip);
            }
            else if (e.ToolPhase == "completed")
            {
                AskToolChip? prior = idx >= 0 ? tools[idx] : null;
                AskToolChip done = new AskToolChip();
                done.Id = e.ToolId!;
                done.Name = !String.IsNullOrEmpty(e.ToolName) ? e.ToolName! : prior?.Name ?? "tool";
                done.Status = e.ToolOk == false ? AskToolChipStatusEnum.Failed : AskToolChipStatusEnum.Success;
                done.Arguments = prior?.Arguments;
                done.Result = e.ToolResult;
                done.ElapsedMs = e.ToolElapsedMs;
                if (idx >= 0) tools[idx] = done;
                else tools.Add(done);
            }
        }

        private bool ApplyTurn(AskEvent e, DateTime nowUtc)
        {
            if (e.State == "started")
            {
                _ClosedTurns.Remove(e.TurnId);
                if (Streaming == null || Streaming.TurnId != e.TurnId) Streaming = new AskStreamingTurn(e.TurnId, nowUtc);
                TurnActive = true;
                TurnError = null;
                Changed();
                return true;
            }

            _ClosedTurns.Add(e.TurnId);
            bool failed = e.State == "failed";
            bool alreadyPersisted = e.MessageId != null && Messages.Any(m => m.Id == e.MessageId);
            AskStreamingTurn? streaming = Streaming;
            if (streaming != null && streaming.TurnId == e.TurnId)
            {
                streaming.Finished = true;
                streaming.FinalMessageId = e.MessageId;
                streaming.FinishedUtc = nowUtc;
                if (e.MessageId != null) Metrics[e.MessageId] = streaming.Metrics(nowUtc);
            }

            if (alreadyPersisted || (streaming != null && streaming.Finished && streaming.TextLength == 0 && streaming.Tools.Count == 0)) streaming = null;
            Streaming = streaming;
            TurnActive = false;
            TurnError = failed ? (e.Error ?? "failed") : null;
            Changed();
            return true;
        }

        private bool ApplyMessage(AskEvent e)
        {
            if (e.Message == null) return false;
            List<AskMessage> one = new List<AskMessage> { e.Message };
            Messages = MergeMessages(Messages, one);
            TrimOldest();
            IndexWork(one);
            IndexProposals(one, null);
            if (ClosesStream(e.Message)) Streaming = null;
            Changed();
            return true;
        }

        private bool ClosesStream(AskMessage message)
        {
            if (Streaming == null) return false;
            if (Streaming.FinalMessageId != null && message.Id == Streaming.FinalMessageId) return true;
            return Streaming.Finished && message.Role == AskMessageRoleEnum.Assistant
                && (message.Kind == AskMessageKindEnum.Text || message.Kind == AskMessageKindEnum.Error);
        }

        private bool ApplyWork(AskEvent e)
        {
            if (e.TrackedWork != null) UpsertWork(e.TrackedWork);
            if (e.Snapshot != null)
            {
                if (Work(e.TrackedWorkId) == null)
                {
                    AskTrackedWork created = new AskTrackedWork();
                    created.Id = e.TrackedWorkId;
                    created.ThreadId = e.ThreadId;
                    created.EntityType = e.Snapshot.EntityType;
                    created.EntityId = e.Snapshot.EntityId;
                    created.Title = e.Snapshot.Title;
                    created.Status = e.Snapshot.Status;
                    created.State = e.Snapshot.State;
                    TrackedWork.Add(created);
                }

                AskWorkLogic.ApplySnapshot(Work(e.TrackedWorkId)!, e.Snapshot);
                Snapshots[e.TrackedWorkId] = e.Snapshot;
            }

            Changed();
            return true;
        }

        private void UpsertWork(AskTrackedWork work)
        {
            if (work == null) return;
            int idx = TrackedWork.FindIndex(w => w.Id == work.Id);
            if (idx < 0)
            {
                TrackedWork.Add(work);
                return;
            }

            AskTrackedWork prior = TrackedWork[idx];
            if (work.Snapshot == null && prior.Snapshot != null) work.Snapshot = prior.Snapshot;
            if (String.IsNullOrEmpty(work.Title)) work.Title = prior.Title;
            TrackedWork[idx] = work;
        }

        private void IndexWork(IEnumerable<AskMessage> messages)
        {
            foreach (AskMessage m in messages)
            {
                if (m.TrackedWork != null && !String.IsNullOrEmpty(m.TrackedWork.Id) && Work(m.TrackedWork.Id) == null) UpsertWork(m.TrackedWork);
            }

            foreach (AskTrackedWork w in TrackedWork)
            {
                if (w.Snapshot != null && !Snapshots.ContainsKey(w.Id)) Snapshots[w.Id] = w.Snapshot;
            }
        }

        private void IndexProposals(IEnumerable<AskMessage> messages, IEnumerable<AskActionProposal>? extra)
        {
            foreach (AskMessage m in messages)
            {
                if (m.Proposal != null && !String.IsNullOrEmpty(m.Proposal.Id))
                {
                    Proposals.TryGetValue(m.Proposal.Id, out AskActionProposal? previous);
                    Proposals[m.Proposal.Id] = MergeProposal(previous, m.Proposal);
                }
            }

            foreach (AskActionProposal p in extra ?? Enumerable.Empty<AskActionProposal>())
            {
                if (p == null || String.IsNullOrEmpty(p.Id)) continue;
                Proposals.TryGetValue(p.Id, out AskActionProposal? previous);
                Proposals[p.Id] = MergeProposal(previous, p);
            }
        }

        #endregion
    }
}
