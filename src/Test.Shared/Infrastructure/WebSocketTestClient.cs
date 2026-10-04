namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Small WebSocket client used by end-to-end suites: connects to the Admiral's /ws endpoint with optional
    /// credentials (query token, Sec-WebSocket-Protocol token, or request headers), sends JSON messages, and
    /// collects received messages in the background so a case can assert both on what arrived and on what did not.
    /// </summary>
    public sealed class WebSocketTestClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The underlying socket.
        /// </summary>
        public ClientWebSocket Socket { get; }

        #endregion

        #region Private-Members

        private readonly List<string> _Received = new List<string>();
        private readonly object _Lock = new object();
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private Task? _ReceiveTask;

        #endregion

        #region Constructors-and-Factories

        private WebSocketTestClient(ClientWebSocket socket)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        /// <summary>
        /// Connect to the Admiral WebSocket endpoint.
        /// </summary>
        /// <param name="restPort">Admiral REST port.</param>
        /// <param name="queryToken">Optional token passed as the token query parameter.</param>
        /// <param name="headers">Optional request headers (for example X-Api-Key or Authorization).</param>
        /// <param name="subprotocols">Optional Sec-WebSocket-Protocol values.</param>
        /// <returns>The connected client; receiving starts immediately.</returns>
        /// <exception cref="WebSocketException">Thrown when the upgrade is rejected.</exception>
        public static async Task<WebSocketTestClient> ConnectAsync(int restPort, string? queryToken = null, Dictionary<string, string>? headers = null, List<string>? subprotocols = null)
        {
            ClientWebSocket socket = new ClientWebSocket();
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers) socket.Options.SetRequestHeader(header.Key, header.Value);
            }

            if (subprotocols != null)
            {
                foreach (string protocol in subprotocols) socket.Options.AddSubProtocol(protocol);
            }

            string url = "ws://127.0.0.1:" + restPort + "/ws";
            if (!String.IsNullOrEmpty(queryToken)) url += "?token=" + Uri.EscapeDataString(queryToken);

            using (CancellationTokenSource connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    await socket.ConnectAsync(new Uri(url), connectTimeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            WebSocketTestClient client = new WebSocketTestClient(socket);
            client._ReceiveTask = Task.Run(() => client.ReceiveLoopAsync());
            return client;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send an object serialized as JSON.
        /// </summary>
        /// <param name="message">Message to send.</param>
        public async Task SendAsync(object message)
        {
            string json = JsonSerializer.Serialize(message);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait until a received message satisfies the predicate.
        /// </summary>
        /// <param name="predicate">Predicate over the raw JSON text of a message.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>The first matching message, or null on timeout.</returns>
        public async Task<string?> WaitForAsync(Func<string, bool> predicate, int timeoutMs = 10000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                lock (_Lock)
                {
                    foreach (string message in _Received)
                    {
                        if (predicate(message)) return message;
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Snapshot of every message received so far.
        /// </summary>
        /// <returns>Received messages in arrival order.</returns>
        public List<string> Received()
        {
            lock (_Lock) return new List<string>(_Received);
        }

        /// <summary>
        /// Close and dispose the socket.
        /// </summary>
        public void Dispose()
        {
            try { _Cts.Cancel(); } catch { }
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token).GetAwaiter().GetResult();
                    }
                }
            }
            catch { }
            Socket.Dispose();
            _Cts.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[1048576];
            try
            {
                while (!_Cts.IsCancellationRequested && Socket.State == WebSocketState.Open)
                {
                    StringBuilder builder = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), _Cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    lock (_Lock) _Received.Add(builder.ToString());
                }
            }
            catch
            {
                // Socket closed or cancelled.
            }
        }

        #endregion
    }
}
