namespace Armada.Client.Socket
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The Armada WebSocket connection, matching the dashboard's <c>lib/armadaSocket.ts</c>: the session token travels as
    /// the <c>token</c> query parameter; on open it sends <c>{ Route: "subscribe" }</c>; dropped connections reconnect
    /// with exponential backoff (1 s doubling to a 30 s cap, plus or minus 20 percent jitter) that resets once a
    /// connection opens; every open after the first raises <see cref="Reconnected"/> and increments
    /// <see cref="ReconnectCount"/>.
    /// Events are raised on a background thread; UI consumers must marshal (the TUI's EventPump posts to the loop).
    /// Thread-safe.
    /// </summary>
    public class ArmadaSocket : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// First reconnect delay in milliseconds (1000).
        /// </summary>
        public const int ReconnectBaseMs = 1000;

        /// <summary>
        /// Maximum reconnect delay in milliseconds (30000).
        /// </summary>
        public const int ReconnectMaxMs = 30000;

        /// <summary>
        /// Raised for every parsed message.
        /// </summary>
        public event EventHandler<ArmadaSocketMessage>? MessageReceived;

        /// <summary>
        /// Raised when the connection opens (true) or drops (false).
        /// </summary>
        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>
        /// Raised after a reconnect (every open after the first), so consumers can refetch what they missed.
        /// </summary>
        public event EventHandler? Reconnected;

        /// <summary>
        /// True while a connection is open.
        /// </summary>
        public bool IsConnected
        {
            get { return Volatile.Read(ref _Connected) == 1; }
        }

        /// <summary>
        /// Number of successful reconnects since <see cref="Start"/>.
        /// </summary>
        public int ReconnectCount
        {
            get { return Volatile.Read(ref _ReconnectCount); }
        }

        /// <summary>
        /// Consecutive failed attempts since the last successful open (drives the backoff).
        /// </summary>
        public int FailedAttempts
        {
            get { return Volatile.Read(ref _Attempt); }
        }

        /// <summary>
        /// True between <see cref="Start"/> and <see cref="StopAsync"/>.
        /// </summary>
        public bool IsRunning
        {
            get { return _Loop != null && !_Loop.IsCompleted; }
        }

        #endregion

        #region Private-Members

        private static readonly Random _SharedRandom = new Random();
        private static readonly object _RandomLock = new object();

        private readonly ArmadaSocketOptions _Options;
        private readonly object _Lock = new object();
        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1, 1);
        private readonly List<ArmadaSocketSubscription> _Subscriptions = new List<ArmadaSocketSubscription>();
        private CancellationTokenSource? _Cts = null;
        private Task? _Loop = null;
        private IArmadaSocketTransport? _Transport = null;
        private int _Connected = 0;
        private int _ReconnectCount = 0;
        private int _Attempt = 0;
        private bool _Opened = false;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public ArmadaSocket(ArmadaSocketOptions options)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The dashboard's reconnect delay for an attempt: <c>min(30000, 1000 * 2^attempt)</c> scaled by a jitter factor
        /// in [0.8, 1.2), capped at 30000.
        /// </summary>
        /// <param name="attempt">Zero-based attempt; clamped to 0..10.</param>
        /// <param name="random">Jitter source value in [0, 1).</param>
        /// <returns>Delay in milliseconds.</returns>
        public static int ReconnectDelayMs(int attempt, double random)
        {
            int safeAttempt = Math.Max(0, Math.Min(attempt, 10));
            double raw = Math.Min(ReconnectMaxMs, ReconnectBaseMs * Math.Pow(2, safeAttempt));
            double jitter = 0.8 + Math.Clamp(random, 0.0, 1.0) * 0.4;
            return (int)Math.Round(Math.Min(ReconnectMaxMs, raw * jitter));
        }

        /// <summary>
        /// Build the socket URL for a REST base URL: http becomes ws, https becomes wss, and the token is appended as
        /// the <c>token</c> query parameter when present.
        /// </summary>
        /// <param name="baseUrl">Admiral REST base URL.</param>
        /// <param name="token">Session token or API key, or null.</param>
        /// <returns>The socket URI.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is not an absolute http(s) URL.</exception>
        public static Uri BuildSocketUri(string baseUrl, string? token)
        {
            if (!Uri.TryCreate((baseUrl ?? "").TrimEnd('/'), UriKind.Absolute, out Uri? parsed))
                throw new ArgumentException("Base URL must be absolute: " + baseUrl, nameof(baseUrl));
            string scheme = parsed.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            string url = scheme + "://" + parsed.Authority + "/ws";
            if (!String.IsNullOrEmpty(token)) url += "?token=" + Uri.EscapeDataString(token);
            return new Uri(url);
        }

        /// <summary>
        /// Subscribe to one event type with a typed handler. The handler runs on the socket's background thread.
        /// </summary>
        /// <typeparam name="T">Payload type (see <see cref="ArmadaEventTypes.PayloadTypes"/>).</typeparam>
        /// <param name="eventType">Event type, or <c>*</c> for every event.</param>
        /// <param name="handler">Handler receiving the typed payload (null when it does not match) and the message.</param>
        /// <returns>A token that unsubscribes when disposed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public IDisposable On<T>(string eventType, Action<T?, ArmadaSocketMessage> handler) where T : class
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ArmadaSocketSubscription subscription = new ArmadaSocketSubscription(eventType, msg => handler(msg.GetData<T>(), msg), Unsubscribe);
            lock (_Lock) _Subscriptions.Add(subscription);
            return subscription;
        }

        /// <summary>
        /// Start connecting (no-op when already running).
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        public void Start()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(ArmadaSocket));
            lock (_Lock)
            {
                if (_Loop != null && !_Loop.IsCompleted) return;
                _Cts = new CancellationTokenSource();
                _Opened = false;
                Volatile.Write(ref _Attempt, 0);
                Volatile.Write(ref _ReconnectCount, 0);
                CancellationToken token = _Cts.Token;
                _Loop = Task.Run(() => RunAsync(token));
            }
        }

        /// <summary>
        /// Close the connection and stop reconnecting.
        /// </summary>
        /// <returns>A task that completes when the loop has exited.</returns>
        public async Task StopAsync()
        {
            Task? loop;
            CancellationTokenSource? cts;
            lock (_Lock)
            {
                loop = _Loop;
                cts = _Cts;
                _Loop = null;
                _Cts = null;
            }

            if (cts == null) return;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            IArmadaSocketTransport? transport = _Transport;
            if (transport != null)
            {
                try
                {
                    using (CancellationTokenSource closeCts = new CancellationTokenSource(2000))
                    {
                        await transport.CloseAsync(closeCts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }

            if (loop != null)
            {
                try { await loop.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            cts.Dispose();
            SetConnected(false);
        }

        /// <summary>
        /// Send a JSON command when connected; dropped silently otherwise.
        /// </summary>
        /// <param name="payload">Payload serialized with the client's JSON options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when sent.</returns>
        public async Task<bool> SendAsync(object payload, CancellationToken token = default)
        {
            IArmadaSocketTransport? transport = _Transport;
            if (transport == null || !IsConnected) return false;
            await _SendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await transport.SendTextAsync(ArmadaJson.Serialize(payload), token).ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _SendLock.Release();
            }
        }

        /// <summary>
        /// Stop and release resources.
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
            if (disposing)
            {
                try { StopAsync().GetAwaiter().GetResult(); } catch (Exception) { }
                _SendLock.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                IArmadaSocketTransport transport = _Options.TransportFactory != null
                    ? _Options.TransportFactory()
                    : new ClientWebSocketTransport();
                _Transport = transport;
                bool opened = false;
                try
                {
                    string? sessionToken = _Options.TokenProvider != null ? _Options.TokenProvider() : null;
                    Uri uri = BuildSocketUri(_Options.BaseUrl, sessionToken);
                    using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        connectCts.CancelAfter(_Options.ConnectTimeoutMs);
                        await transport.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                    }

                    opened = true;
                    bool isReconnect = _Opened;
                    _Opened = true;
                    Volatile.Write(ref _Attempt, 0);
                    SetConnected(true);

                    object subscribe = _Options.AllTenants
                        ? (object)new SubscribeAllMessage()
                        : new SubscribeMessage();
                    await transport.SendTextAsync(ArmadaJson.Serialize(subscribe), token).ConfigureAwait(false);

                    if (isReconnect)
                    {
                        Interlocked.Increment(ref _ReconnectCount);
                        RaiseReconnected();
                    }

                    while (!token.IsCancellationRequested)
                    {
                        string? text = await transport.ReceiveTextAsync(token).ConfigureAwait(false);
                        if (text == null) break;
                        ArmadaSocketMessage? message = ArmadaSocketMessage.Parse(text);
                        if (message != null) Dispatch(message);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // Connection failed or dropped; fall through to the reconnect delay.
                }
                finally
                {
                    _Transport = null;
                    try { transport.Dispose(); } catch (Exception) { }
                    if (opened) SetConnected(false);
                }

                if (token.IsCancellationRequested) break;

                int attempt = Interlocked.Increment(ref _Attempt) - 1;
                int delay = ReconnectDelayMs(attempt, NextRandom());
                try
                {
                    if (_Options.Delay != null) await _Options.Delay(delay, token).ConfigureAwait(false);
                    else await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void Dispatch(ArmadaSocketMessage message)
        {
            EventHandler<ArmadaSocketMessage>? handler = MessageReceived;
            if (handler != null)
            {
                try { handler(this, message); }
                catch (Exception) { }
            }

            List<ArmadaSocketSubscription> snapshot;
            lock (_Lock) snapshot = new List<ArmadaSocketSubscription>(_Subscriptions);
            foreach (ArmadaSocketSubscription subscription in snapshot)
            {
                if (subscription.EventType != "*" && !String.Equals(subscription.EventType, message.Type, StringComparison.Ordinal)) continue;
                try { subscription.Handler(message); }
                catch (Exception) { }
            }
        }

        private void SetConnected(bool connected)
        {
            int next = connected ? 1 : 0;
            int previous = Interlocked.Exchange(ref _Connected, next);
            if (previous == next) return;
            EventHandler<bool>? handler = ConnectionChanged;
            if (handler == null) return;
            try { handler(this, connected); }
            catch (Exception) { }
        }

        private void RaiseReconnected()
        {
            EventHandler? handler = Reconnected;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { }
        }

        private double NextRandom()
        {
            if (_Options.Random != null) return _Options.Random();
            lock (_RandomLock) return _SharedRandom.NextDouble();
        }

        private void Unsubscribe(ArmadaSocketSubscription subscription)
        {
            lock (_Lock) _Subscriptions.Remove(subscription);
        }

        #endregion
    }
}
