namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Services;

    /// <summary>
    /// An in-memory Harbor link: the Harbor side of a real <see cref="HarborLinkClient"/> talks to a real
    /// <see cref="HarborConnectionManager"/> exactly as the WebSocket endpoint would wire them. Every frame is serialized
    /// and deserialized through <see cref="HarborProtocol"/>, the first frame must be the handshake, and the Admiral's
    /// messages to the Harbor arrive through the send delegate the manager was given at the handshake.
    /// </summary>
    public sealed class LoopbackHarborTransport : IHarborTransport
    {
        #region Private-Members

        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;
        private readonly string? _TenantId;
        private readonly string? _UserId;
        private readonly Channel<string> _Inbound = Channel.CreateUnbounded<string>();
        private readonly HarborSendDelegate _Link;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="manager">Admiral-side connection manager.</param>
        /// <param name="harborId">Harbor identifier the client handshakes with.</param>
        /// <param name="tenantId">Tenant the link authenticates as, or null.</param>
        /// <param name="userId">User the link authenticates as, or null.</param>
        public LoopbackHarborTransport(HarborConnectionManager manager, string harborId, string? tenantId, string? userId)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _HarborId = harborId ?? throw new ArgumentNullException(nameof(harborId));
            _TenantId = tenantId;
            _UserId = userId;
            _Link = async (message, token) =>
            {
                await _Inbound.Writer.WriteAsync(HarborProtocol.Serialize(message), CancellationToken.None).ConfigureAwait(false);
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task ConnectAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task SendAsync(string text, CancellationToken token)
        {
            HarborMessage message = HarborProtocol.Deserialize(text);
            if (message is HarborHandshake handshake)
            {
                HarborHandshakeAck ack = await _Manager.OnHandshakeAsync(handshake, _TenantId, _UserId, _Link, token).ConfigureAwait(false);
                await _Inbound.Writer.WriteAsync(HarborProtocol.Serialize(ack), token).ConfigureAwait(false);
                return;
            }

            await _Manager.OnMessageAsync(_HarborId, message, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string?> ReceiveAsync(CancellationToken token)
        {
            try
            {
                if (await _Inbound.Reader.WaitToReadAsync(token).ConfigureAwait(false) && _Inbound.Reader.TryRead(out string? text))
                    return text;
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public async Task CloseAsync(CancellationToken token)
        {
            _Inbound.Writer.TryComplete();
            await _Manager.OnDisconnectedAsync(_HarborId, _Link, token).ConfigureAwait(false);
        }

        /// <summary>
        /// End the link from the Admiral side (the Harbor's receive loop then ends).
        /// </summary>
        public void Complete()
        {
            _Inbound.Writer.TryComplete();
        }

        #endregion
    }
}
