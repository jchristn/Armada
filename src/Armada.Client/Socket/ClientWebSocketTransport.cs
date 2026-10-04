namespace Armada.Client.Socket
{
    using System;
    using System.IO;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// <see cref="IArmadaSocketTransport"/> over <see cref="ClientWebSocket"/>. Not thread-safe for concurrent sends;
    /// <see cref="ArmadaSocket"/> serializes sends.
    /// </summary>
    public class ClientWebSocketTransport : IArmadaSocketTransport
    {
        #region Private-Members

        private readonly ClientWebSocket _Socket = new ClientWebSocket();
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ClientWebSocketTransport()
        {
            _Socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task ConnectAsync(Uri uri, CancellationToken token)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            return _Socket.ConnectAsync(uri, token);
        }

        /// <inheritdoc />
        public async Task<string?> ReceiveTextAsync(CancellationToken token)
        {
            byte[] buffer = new byte[16384];
            using (MemoryStream stream = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result = await _Socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return null;
                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        if (result.MessageType != WebSocketMessageType.Text)
                        {
                            stream.SetLength(0);
                            continue;
                        }

                        return Encoding.UTF8.GetString(stream.ToArray());
                    }
                }
            }
        }

        /// <inheritdoc />
        public Task SendTextAsync(string text, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            return _Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        /// <inheritdoc />
        public async Task CloseAsync(CancellationToken token)
        {
            if (_Socket.State == WebSocketState.Open || _Socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await _Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", token).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    // Already gone.
                }
            }
        }

        /// <summary>
        /// Dispose the socket.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Socket.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
