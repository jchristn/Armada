namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Per-screen auto-refresh (W1.11): intervals off, 15, 30, 60, 120, 180, 300 seconds (the dashboard's choices;
    /// default 15), persisted per screen; paused while a modal or drawer is open; <c>F5</c> refreshes now. A timer
    /// ticks every second and posts due refreshes to the UI loop. Call configuration methods on the UI loop thread.
    /// </summary>
    public class RefreshService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Allowed intervals in seconds (0 is off).
        /// </summary>
        public static IReadOnlyList<int> Intervals { get; } = new List<int> { 0, 15, 30, 60, 120, 180, 300 };

        /// <summary>
        /// Default interval in seconds (15).
        /// </summary>
        public const int DefaultInterval = 15;

        /// <summary>
        /// Active screen key, or null.
        /// </summary>
        public string? ScreenKey { get; private set; } = null;

        /// <summary>
        /// Interval for the active screen in seconds (0 is off).
        /// </summary>
        public int IntervalSeconds
        {
            get { return ScreenKey == null ? 0 : IntervalFor(ScreenKey); }
        }

        /// <summary>
        /// True while refreshes are paused (a modal is open).
        /// </summary>
        public bool Paused
        {
            get { return _IsPaused(); }
        }

        /// <summary>
        /// Status text: "auto 15s", "paused", or "manual".
        /// </summary>
        public string StatusText
        {
            get
            {
                if (_Refresh == null) return "";
                if (Paused) return "paused";
                int s = IntervalSeconds;
                return s == 0 ? "manual" : "auto " + s + "s";
            }
        }

        #endregion

        #region Private-Members

        private readonly PreferencesService _Prefs;
        private readonly IUiDispatcher _Dispatcher;
        private readonly IClock _Clock;
        private readonly Func<bool> _IsPaused;
        private Action? _Refresh = null;
        private DateTime _NextDue = DateTime.MaxValue;
        private Timer? _Timer = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="prefs">Preferences.</param>
        /// <param name="dispatcher">Dispatcher.</param>
        /// <param name="clock">Clock.</param>
        /// <param name="isPaused">Returns true while refresh should pause.</param>
        public RefreshService(PreferencesService prefs, IUiDispatcher dispatcher, IClock clock, Func<bool> isPaused)
        {
            _Prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _IsPaused = isPaused ?? (() => false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the one-second ticker.
        /// </summary>
        public void Start()
        {
            if (_Timer != null) return;
            _Timer = new Timer(_ => _Dispatcher.Post(Tick), null, 1000, 1000);
        }

        /// <summary>
        /// Attach the active screen's refresh action (null detaches).
        /// </summary>
        /// <param name="screenKey">Screen key, or null.</param>
        /// <param name="refresh">Refresh action, or null.</param>
        public void Attach(string? screenKey, Action? refresh)
        {
            ScreenKey = screenKey;
            _Refresh = refresh;
            Schedule();
        }

        /// <summary>
        /// Interval for a screen.
        /// </summary>
        /// <param name="screenKey">Screen key.</param>
        /// <returns>Seconds (0 is off).</returns>
        public int IntervalFor(string screenKey)
        {
            return _Prefs.Current.RefreshIntervals.TryGetValue(screenKey, out int s) ? s : DefaultInterval;
        }

        /// <summary>
        /// Set and persist the active screen's interval (snapped to the allowed values).
        /// </summary>
        /// <param name="seconds">Seconds.</param>
        public void SetInterval(int seconds)
        {
            if (ScreenKey == null) return;
            int snapped = Intervals[0];
            foreach (int v in Intervals) if (Math.Abs(v - seconds) < Math.Abs(snapped - seconds)) snapped = v;
            _Prefs.Current.RefreshIntervals[ScreenKey] = snapped;
            _Prefs.Save();
            Schedule();
        }

        /// <summary>
        /// Refresh now (F5) and restart the interval.
        /// </summary>
        /// <returns>True when a refresh action was attached.</returns>
        public bool RefreshNow()
        {
            if (_Refresh == null) return false;
            _Refresh();
            Schedule();
            return true;
        }

        /// <summary>
        /// Run a due refresh (called every second on the UI loop; tests call it directly).
        /// </summary>
        public void Tick()
        {
            if (_Refresh == null || Paused) return;
            if (_Clock.UtcNow < _NextDue) return;
            _Refresh();
            Schedule();
        }

        /// <summary>
        /// Stop the ticker.
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
            if (disposing) _Timer?.Dispose();
            _Timer = null;
        }

        #endregion

        #region Private-Methods

        private void Schedule()
        {
            int s = IntervalSeconds;
            _NextDue = _Refresh == null || s == 0 ? DateTime.MaxValue : _Clock.UtcNow.AddSeconds(s);
        }

        #endregion
    }
}
