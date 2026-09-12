namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text;

    /// <summary>
    /// A Harbor transport over a client WebSocket. The Harbor dials the Admiral (client-to-server), which is
    /// why this is a client socket: it works from behind NAT and from a container-published port without the
    /// Admiral reaching into the host.
    /// </summary>
    public class WebSocketHarborTransport : IHarborTransport, IDisposable
    {
        #region Private-Members

        private readonly Uri _ServerUri;
        private readonly IReadOnlyDictionary<string, string>? _Headers;
        private readonly int _ReceiveBufferBytes = 65536;
        private ClientWebSocket? _Socket;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="serverUri">The Admiral link URI (ws:// or wss://).</param>
        /// <param name="headers">Optional headers to set on the upgrade request (for example auth material).</param>
        public WebSocketHarborTransport(Uri serverUri, IReadOnlyDictionary<string, string>? headers = null)
        {
            _ServerUri = serverUri ?? throw new ArgumentNullException(nameof(serverUri));
            _Headers = headers;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ConnectAsync(CancellationToken token)
        {
            _Socket = new ClientWebSocket();
            if (_Headers != null)
            {
                foreach (KeyValuePair<string, string> header in _Headers)
                    _Socket.Options.SetRequestHeader(header.Key, header.Value);
            }

            await _Socket.ConnectAsync(_ServerUri, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SendAsync(string text, CancellationToken token)
        {
            if (_Socket == null) throw new InvalidOperationException("Transport is not connected.");
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await _Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string?> ReceiveAsync(CancellationToken token)
        {
            if (_Socket == null) throw new InvalidOperationException("Transport is not connected.");

            byte[] buffer = new byte[_ReceiveBufferBytes];
            StringBuilder message = new StringBuilder();
            while (true)
            {
                WebSocketReceiveResult result = await _Socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, token).ConfigureAwait(false);
                    return null;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage) return message.ToString();
            }
        }

        /// <inheritdoc />
        public async Task CloseAsync(CancellationToken token)
        {
            if (_Socket == null) return;
            if (_Socket.State == WebSocketState.Open)
            {
                try
                {
                    await _Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, token).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Socket?.Dispose();
            _Disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
