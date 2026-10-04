namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client.Socket;

    /// <summary>
    /// Owns the <see cref="ArmadaSocket"/> and fans its events out to the UI (W1.11): every message, connection
    /// change, and reconnect is marshaled through <see cref="IUiDispatcher.Post"/>; coalesced subscribers run at most
    /// once per window (default 250 ms) however many events arrive, so a burst refreshes each screen once. Messages can
    /// also be injected (tests, scripted event sources). Thread-safe for event arrival; subscriber callbacks run on the
    /// UI loop.
    /// </summary>
    public class EventPump : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// True while the socket is connected (the header's Live indicator).
        /// </summary>
        public bool IsLive { get; private set; } = false;

        /// <summary>
        /// Reconnects since start.
        /// </summary>
        public int ReconnectCount
        {
            get { return _Socket?.ReconnectCount ?? 0; }
        }

        /// <summary>
        /// Coalescing window in milliseconds. Default 250; clamped to 0..5000.
        /// </summary>
        public int CoalesceMs
        {
            get { return _CoalesceMs; }
            set { _CoalesceMs = Math.Clamp(value, 0, 5000); }
        }

        /// <summary>
        /// Messages delivered (diagnostics).
        /// </summary>
        public long Delivered { get; private set; } = 0;

        /// <summary>
        /// Raised on the UI loop when the connection state changes.
        /// </summary>
        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>
        /// Raised on the UI loop after a reconnect (screens refetch).
        /// </summary>
        public event EventHandler? Reconnected;

        #endregion

        #region Private-Members

        private readonly IUiDispatcher _Dispatcher;
        private readonly object _Lock = new object();
        private readonly List<EventPumpSubscription> _Subscriptions = new List<EventPumpSubscription>();
        private ArmadaSocket? _Socket = null;
        private int _CoalesceMs = 250;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dispatcher">UI dispatcher.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="dispatcher"/> is null.</exception>
        public EventPump(IUiDispatcher dispatcher)
        {
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Connect to a server (replacing any previous connection).
        /// </summary>
        /// <param name="options">Socket options.</param>
        public void Start(ArmadaSocketOptions options)
        {
            Stop();
            ArmadaSocket socket = new ArmadaSocket(options);
            socket.MessageReceived += (s, m) => Inject(m);
            socket.ConnectionChanged += (s, live) => _Dispatcher.Post(() =>
            {
                IsLive = live;
                ConnectionChanged?.Invoke(this, live);
            });
            socket.Reconnected += (s, e) => _Dispatcher.Post(() => Reconnected?.Invoke(this, EventArgs.Empty));
            _Socket = socket;
            socket.Start();
        }

        /// <summary>
        /// Disconnect.
        /// </summary>
        public void Stop()
        {
            ArmadaSocket? socket = _Socket;
            _Socket = null;
            if (socket != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await socket.StopAsync().ConfigureAwait(false); } catch (Exception) { }
                    socket.Dispose();
                });
            }

            if (IsLive)
            {
                IsLive = false;
                _Dispatcher.Post(() => ConnectionChanged?.Invoke(this, false));
            }
        }

        /// <summary>
        /// Subscribe to messages whose type equals <paramref name="typeOrPrefix"/>, starts with it when it ends with
        /// <c>.</c> or <c>*</c>, or every message for <c>*</c>. The handler runs on the UI loop for every message.
        /// </summary>
        /// <param name="typeOrPrefix">Type, prefix, or <c>*</c>.</param>
        /// <param name="handler">Handler.</param>
        /// <returns>Disposable subscription.</returns>
        public IDisposable Subscribe(string typeOrPrefix, Action<ArmadaSocketMessage> handler)
        {
            EventPumpSubscription sub = new EventPumpSubscription(typeOrPrefix, handler, null, Remove);
            lock (_Lock) _Subscriptions.Add(sub);
            return sub;
        }

        /// <summary>
        /// Subscribe a coalesced callback: however many matching messages arrive within the window, the callback runs
        /// once on the UI loop after it.
        /// </summary>
        /// <param name="typeOrPrefix">Type, prefix, or <c>*</c>.</param>
        /// <param name="callback">Callback.</param>
        /// <returns>Disposable subscription.</returns>
        public IDisposable SubscribeCoalesced(string typeOrPrefix, Action callback)
        {
            EventPumpSubscription sub = new EventPumpSubscription(typeOrPrefix, null, callback, Remove);
            lock (_Lock) _Subscriptions.Add(sub);
            return sub;
        }

        /// <summary>
        /// Deliver a message as if it arrived on the socket (thread-safe).
        /// </summary>
        /// <param name="message">Message.</param>
        public void Inject(ArmadaSocketMessage message)
        {
            if (message == null) return;
            List<EventPumpSubscription> matches;
            lock (_Lock) matches = _Subscriptions.Where(s => s.Matches(message.Type)).ToList();
            _Dispatcher.Post(() => Delivered++);
            foreach (EventPumpSubscription sub in matches)
            {
                if (sub.Handler != null)
                {
                    Action<ArmadaSocketMessage> handler = sub.Handler;
                    _Dispatcher.Post(() => { if (!sub.Disposed) handler(message); });
                }
                else if (sub.Callback != null && sub.TryArm())
                {
                    Action callback = sub.Callback;
                    int window = _CoalesceMs;
                    if (window == 0)
                    {
                        sub.Disarm();
                        _Dispatcher.Post(() => { if (!sub.Disposed) callback(); });
                    }
                    else
                    {
                        _ = Task.Delay(window).ContinueWith(_ =>
                        {
                            sub.Disarm();
                            _Dispatcher.Post(() => { if (!sub.Disposed) callback(); });
                        }, TaskScheduler.Default);
                    }
                }
            }
        }

        /// <summary>
        /// Stop and release the socket.
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
        /// <param name="disposing">True from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) Stop();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void Remove(EventPumpSubscription sub)
        {
            lock (_Lock) _Subscriptions.Remove(sub);
        }

        #endregion
    }
}
