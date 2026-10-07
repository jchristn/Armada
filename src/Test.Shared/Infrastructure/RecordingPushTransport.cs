namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Push;

    /// <summary>
    /// Test double for <see cref="IPushTransport"/>: records every batch and receipt request, answers with scripted
    /// tickets and receipts, and can fail a number of sends first (transiently or permanently). Never calls the network.
    /// </summary>
    public sealed class RecordingPushTransport : IPushTransport
    {
        #region Public-Members

        /// <summary>
        /// Every send batch, in order.
        /// </summary>
        public List<List<PushMessage>> Batches
        {
            get { lock (_Lock) return _Batches.Select(b => new List<PushMessage>(b)).ToList(); }
        }

        /// <summary>
        /// Every message sent, in order.
        /// </summary>
        public List<PushMessage> Messages
        {
            get { lock (_Lock) return _Batches.SelectMany(b => b).ToList(); }
        }

        /// <summary>
        /// Access tokens passed with each send.
        /// </summary>
        public List<string?> AccessTokens
        {
            get { lock (_Lock) return new List<string?>(_AccessTokens); }
        }

        /// <summary>
        /// Every receipt request (ticket ids), in order.
        /// </summary>
        public List<List<string>> ReceiptRequests
        {
            get { lock (_Lock) return _ReceiptRequests.Select(r => new List<string>(r)).ToList(); }
        }

        /// <summary>
        /// Number of send calls, including failed ones.
        /// </summary>
        public int SendCalls
        {
            get { lock (_Lock) return _SendCalls; }
        }

        /// <summary>
        /// Number of upcoming send calls that throw.
        /// </summary>
        public int FailNextSends { get; set; } = 0;

        /// <summary>
        /// Whether scripted failures are transient.
        /// </summary>
        public bool FailTransient { get; set; } = true;

        /// <summary>
        /// When true, every send throws (a permanent transport failure) regardless of <see cref="FailNextSends"/>.
        /// </summary>
        public bool AlwaysThrow { get; set; } = false;

        /// <summary>
        /// Tokens (Expo push tokens) whose tickets come back as DeviceNotRegistered errors.
        /// </summary>
        public HashSet<string> NotRegisteredTokens { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Tokens whose receipts come back as DeviceNotRegistered errors (their tickets are ok).
        /// </summary>
        public HashSet<string> NotRegisteredReceiptTokens { get; } = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<List<PushMessage>> _Batches = new List<List<PushMessage>>();
        private readonly List<string?> _AccessTokens = new List<string?>();
        private readonly List<List<string>> _ReceiptRequests = new List<List<string>>();
        private readonly Dictionary<string, string> _TicketTokens = new Dictionary<string, string>(StringComparer.Ordinal);
        private int _SendCalls = 0;
        private int _NextTicket = 0;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<PushTicket>> SendAsync(List<PushMessage> messages, string? accessToken, CancellationToken token = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (messages.Count > ExpoPushTransport.MaxMessagesPerRequest) throw new ArgumentException("Too many messages in one batch: " + messages.Count);
            lock (_Lock)
            {
                _SendCalls++;
                if (AlwaysThrow) throw new PushTransportException("scripted permanent failure", false, 400);
                if (FailNextSends > 0)
                {
                    FailNextSends--;
                    throw new PushTransportException("scripted failure", FailTransient, FailTransient ? 503 : 400);
                }

                _Batches.Add(new List<PushMessage>(messages));
                _AccessTokens.Add(accessToken);
                List<PushTicket> tickets = new List<PushTicket>();
                foreach (PushMessage message in messages)
                {
                    if (NotRegisteredTokens.Contains(message.To))
                    {
                        tickets.Add(new PushTicket { Ok = false, Error = PushErrorCodeEnum.DeviceNotRegistered, Message = "not registered" });
                        continue;
                    }

                    string id = "tkt_" + (++_NextTicket);
                    _TicketTokens[id] = message.To;
                    tickets.Add(new PushTicket { Ok = true, TicketId = id });
                }

                return Task.FromResult(tickets);
            }
        }

        /// <inheritdoc />
        public Task<List<PushReceipt>> GetReceiptsAsync(List<string> ticketIds, string? accessToken, CancellationToken token = default)
        {
            if (ticketIds == null) throw new ArgumentNullException(nameof(ticketIds));
            lock (_Lock)
            {
                _ReceiptRequests.Add(new List<string>(ticketIds));
                List<PushReceipt> receipts = new List<PushReceipt>();
                foreach (string id in ticketIds)
                {
                    if (!_TicketTokens.TryGetValue(id, out string? to)) continue;
                    bool dead = NotRegisteredReceiptTokens.Contains(to);
                    receipts.Add(new PushReceipt { TicketId = id, Ok = !dead, Error = dead ? PushErrorCodeEnum.DeviceNotRegistered : (PushErrorCodeEnum?)null });
                }

                return Task.FromResult(receipts);
            }
        }

        #endregion
    }
}
