namespace Armada.Tui.Shell
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using TUIKit;

    /// <summary>
    /// Decides when <see cref="TuiRunLoop"/> composes a frame and how long it sleeps (W8.5 idle CPU). TUIKit's own loop
    /// composes and diffs every frame at a fixed 60 per second; with the governor enabled a frame is composed only when
    /// something could have changed (input was read, posted work ran, the terminal was resized, or
    /// <see cref="Invalidate"/> was called), every frame during a short hot window after input or a change (so escape
    /// sequences that complete on a later tick and follow-up posts show at once), and otherwise on a slow idle tick
    /// (clocks, relative times, toast expiry): <see cref="IdleIntervalMs"/> while the terminal has focus and
    /// <see cref="UnfocusedIntervalMs"/> while it does not. While idle the loop polls input every
    /// <see cref="IdlePollMs"/> instead of every frame. Disabled, every tick composes a frame at
    /// <see cref="ActiveFrameMs"/>, like TUIKit's loop. <see cref="Invalidate"/> and <see cref="NoteInput"/> are
    /// thread-safe; <see cref="ShouldRender"/> and <see cref="NextDelayMs"/> are called on the loop thread.
    /// </summary>
    public class FrameGovernor
    {
        #region Public-Members

        /// <summary>
        /// Gate frames. Default false (every tick composes a frame).
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Idle redraw interval while the terminal has focus, in milliseconds (default 250, 4 frames per second).
        /// Clamped to 16..5000.
        /// </summary>
        public int IdleIntervalMs
        {
            get { return _IdleIntervalMs; }
            set { _IdleIntervalMs = Math.Clamp(value, 16, 5000); }
        }

        /// <summary>
        /// Idle redraw interval while the terminal is unfocused, in milliseconds (default 1000). Clamped to 16..10000.
        /// </summary>
        public int UnfocusedIntervalMs
        {
            get { return _UnfocusedIntervalMs; }
            set { _UnfocusedIntervalMs = Math.Clamp(value, 16, 10000); }
        }

        /// <summary>
        /// Frame interval while active (and always while disabled), in milliseconds (default 16, about 60 per second).
        /// Clamped to 4..100.
        /// </summary>
        public int ActiveFrameMs
        {
            get { return _ActiveFrameMs; }
            set { _ActiveFrameMs = Math.Clamp(value, 4, 100); }
        }

        /// <summary>
        /// Input polling interval while idle, in milliseconds (default 25, the worst-case added key latency). Clamped
        /// to 4..200.
        /// </summary>
        public int IdlePollMs
        {
            get { return _IdlePollMs; }
            set { _IdlePollMs = Math.Clamp(value, 4, 200); }
        }

        /// <summary>
        /// Keep composing every frame for this long after input or a change, in milliseconds (default 150). Clamped to
        /// 0..2000.
        /// </summary>
        public int HotWindowMs
        {
            get { return _HotWindowMs; }
            set { _HotWindowMs = Math.Clamp(value, 0, 2000); }
        }

        /// <summary>
        /// The terminal has focus (focus reporting); unfocused terminals use the slower idle interval.
        /// </summary>
        public bool TerminalFocused { get; set; } = true;

        /// <summary>
        /// Frames composed so far.
        /// </summary>
        public long Composed
        {
            get { return Interlocked.Read(ref _Composed); }
        }

        /// <summary>
        /// Ticks that skipped composing so far.
        /// </summary>
        public long Skipped
        {
            get { return Interlocked.Read(ref _Skipped); }
        }

        /// <summary>
        /// Milliseconds clock; replaceable for tests. Defaults to a monotonic stopwatch.
        /// </summary>
        public Func<long> Clock { get; set; }

        #endregion

        #region Private-Members

        private readonly Stopwatch _Watch = Stopwatch.StartNew();
        private int _IdleIntervalMs = 250;
        private int _UnfocusedIntervalMs = 1000;
        private int _HotWindowMs = 150;
        private int _ActiveFrameMs = 16;
        private int _IdlePollMs = 25;
        private long _LastChangeMs = Int64.MinValue / 2;
        private int _Dirty = 1;
        private long _LastInputMs = Int64.MinValue / 2;
        private long _LastFrameMs = Int64.MinValue / 2;
        private Size _LastSize = new Size(0, 0);
        private long _Composed = 0;
        private long _Skipped = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (disabled).
        /// </summary>
        public FrameGovernor()
        {
            Clock = () => _Watch.ElapsedMilliseconds;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Something changed: redraw on the next frame.
        /// </summary>
        public void Invalidate()
        {
            Interlocked.Exchange(ref _Dirty, 1);
        }

        /// <summary>
        /// Input was read: redraw now and for the hot window.
        /// </summary>
        public void NoteInput()
        {
            Interlocked.Exchange(ref _LastInputMs, Clock());
            Interlocked.Exchange(ref _Dirty, 1);
        }

        /// <summary>
        /// Decide whether this tick composes a frame, and count it. Always true while disabled.
        /// </summary>
        /// <param name="size">Terminal size.</param>
        /// <returns>True to compose; false to skip.</returns>
        public bool ShouldRender(Size size)
        {
            long now = Clock();
            bool dirty = Interlocked.Exchange(ref _Dirty, 0) == 1;
            if (dirty || size != _LastSize) _LastChangeMs = now;
            bool render = !Enabled
                || dirty
                || size != _LastSize
                || IsHot(now)
                || now - _LastFrameMs >= (TerminalFocused ? _IdleIntervalMs : _UnfocusedIntervalMs);
            if (render)
            {
                _LastFrameMs = now;
                _LastSize = size;
                Interlocked.Increment(ref _Composed);
            }
            else
            {
                Interlocked.Increment(ref _Skipped);
            }

            return render;
        }

        /// <summary>
        /// Milliseconds the loop sleeps before its next tick: the active frame interval while disabled or hot, the idle
        /// polling interval otherwise.
        /// </summary>
        /// <returns>Milliseconds.</returns>
        public int NextDelayMs()
        {
            if (!Enabled || Volatile.Read(ref _Dirty) == 1 || IsHot(Clock())) return _ActiveFrameMs;
            return _IdlePollMs;
        }

        #endregion

        #region Private-Methods

        private bool IsHot(long now)
        {
            return now - Interlocked.Read(ref _LastInputMs) <= _HotWindowMs || now - _LastChangeMs <= _HotWindowMs;
        }

        #endregion
    }
}
