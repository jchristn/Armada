namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The dashboard's mission log viewer: line count 100/200/500/1000 (<c>+</c>/<c>-</c>), Follow (<c>f</c>) refreshes
    /// every second until the mission reaches a terminal status (one final refresh, then stops), Live and Done badges,
    /// total lines, copy (<c>y</c>), search (<c>/</c>), and the readable toggle (<c>r</c>). Not thread-safe; refreshes
    /// are posted to the UI loop.
    /// </summary>
    public class OpsLogModal : ArmadaDialog, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Line counts offered (the dashboard's LINE_COUNT_OPTIONS).
        /// </summary>
        public static readonly int[] LineCounts = new int[] { 100, 200, 500, 1000 };

        /// <summary>
        /// Viewer.
        /// </summary>
        public LogViewer Viewer { get; } = new LogViewer();

        /// <summary>
        /// Lines requested.
        /// </summary>
        public int LineCount { get; private set; } = 200;

        /// <summary>
        /// Total lines reported by the server.
        /// </summary>
        public int TotalLines { get; private set; } = 0;

        /// <summary>
        /// True while following.
        /// </summary>
        public bool Following { get; private set; } = false;

        /// <summary>
        /// Number of fetches made (tests).
        /// </summary>
        public int Fetches { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly Func<int, CancellationToken, Task<LogResult?>> _Fetch;
        private readonly Func<bool> _Completed;
        private readonly IUiDispatcher _Dispatcher;
        private readonly Action<string> _Copy;
        private readonly Action? _OnTick;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private Timer? _Timer = null;
        private bool _WasCompleted;
        private int _Ticks = 0;
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load the first page.
        /// </summary>
        /// <param name="title">Title (translated).</param>
        /// <param name="fetch">Fetches the last N lines (off the UI loop).</param>
        /// <param name="completed">True once the mission reached a terminal status.</param>
        /// <param name="dispatcher">UI dispatcher.</param>
        /// <param name="copy">Copies text.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="onTick">Called every fifth follow refresh (the dashboard reloads the mission), or null.</param>
        public OpsLogModal(string title, Func<int, CancellationToken, Task<LogResult?>> fetch, Func<bool> completed, IUiDispatcher dispatcher, Action<string> copy, ITextLocalizer? localizer, ArmadaTheme? theme, Action? onTick = null)
            : base(title, localizer, theme)
        {
            _Fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            _Completed = completed ?? (() => true);
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _Copy = copy ?? (s => { });
            _OnTick = onTick;
            _WasCompleted = _Completed();
            Viewer.Localizer = Localizer;
            Viewer.ApplyTheme(Theme);
            Viewer.OnFocusChanged(true);
            Viewer.Follow = false;
            Viewer.SetText(T("Loading..."));
            FooterHint = " f " + T("Follow") + "  +/- " + T("lines") + "  / " + T("Search") + "  r " + T("Readable") + "  y " + T("Copy") + "  Esc " + T("Close") + " ";
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fetch the log now.
        /// </summary>
        public void Load()
        {
            int lines = LineCount;
            CancellationToken token = _Cts.Token;
            Fetches++;
            _ = Task.Run(async () =>
            {
                try
                {
                    LogResult? result = await _Fetch(lines, token).ConfigureAwait(false);
                    _Dispatcher.Post(() =>
                    {
                        if (IsClosed) return;
                        TotalLines = result?.TotalLines ?? 0;
                        string text = result?.Log ?? "";
                        Viewer.SetText(text.Length > 0 ? text : T("No log output"));
                        if (Following) Viewer.Follow = true;
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _Dispatcher.Post(() =>
                    {
                        if (IsClosed) return;
                        if (Viewer.LineCount <= 1) Viewer.SetText(Localizer.T("Log unavailable: {{message}}", LocalizationArgs.Of("message", ex.Message)));
                    });
                }
            });
        }

        /// <summary>
        /// Toggle following (no-op once completed).
        /// </summary>
        public void ToggleFollow()
        {
            if (Following)
            {
                StopFollowing();
                return;
            }

            if (_Completed()) return;
            Following = true;
            Viewer.Follow = true;
            Load();
            _Timer?.Dispose();
            _Timer = new Timer(_ => _Dispatcher.Post(Tick), null, 1000, 1000);
        }

        /// <summary>
        /// Change the line count.
        /// </summary>
        /// <param name="lines">Lines.</param>
        public void SetLineCount(int lines)
        {
            LineCount = Math.Clamp(lines, 1, 10000);
            Load();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (!Viewer.Searching && HandleDismiss(key, null))
            {
                Dispose();
                return true;
            }

            if (!Viewer.Searching && key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None)
            {
                switch (key.Rune)
                {
                    case 'q':
                        Dispose();
                        RequestClose(null);
                        return true;
                    case 'y':
                        _Copy(Viewer.PlainText);
                        return true;
                    case 'f':
                        ToggleFollow();
                        return true;
                    case '+':
                    case '=':
                        SetLineCount(Next(1));
                        return true;
                    case '-':
                        SetLineCount(Next(-1));
                        return true;
                }
            }

            if (Viewer.HandleKey(key)) return true;
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _ScreenWidth = surface.Size.Width;
            _ScreenHeight = surface.Size.Height;
            base.Render(surface);
        }

        /// <summary>
        /// Stop the follow timer.
        /// </summary>
        public void Dispose()
        {
            _Timer?.Dispose();
            _Timer = null;
            try { _Cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Max(40, Math.Min(availableWidth, (int)(_ScreenWidth * 0.92)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(6, (int)(_ScreenHeight * 0.88) - 4);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            bool done = _Completed();
            string badge = done ? "[" + T("Done") + "]" : Following ? "[" + T("Live") + "]" : "";
            string head = LineCount + " " + T("lines") + "   " + (done ? T("Completed") : Following ? T("Following") : T("Follow") + " (f)");
            int x = 0;
            if (badge.Length > 0) x += SurfaceText.Draw(content, 0, 0, badge + " ", done ? On(Theme.Success) : On(Theme.Info), width) ;
            SurfaceText.Draw(content, x, 0, head, Dim(), width - x);
            string total = T("Total lines") + ": " + TotalLines;
            SurfaceText.Draw(content, Math.Max(0, width - TextCells.Width(total)), 0, total, Dim(), width);
            Viewer.Render(new SurfaceView(content, new Rect(0, 1, width, Math.Max(1, height - 1))));
        }

        #endregion

        #region Private-Methods

        private int Next(int direction)
        {
            int idx = Array.IndexOf(LineCounts, LineCount);
            if (idx < 0) idx = 1;
            idx = Math.Clamp(idx + direction, 0, LineCounts.Length - 1);
            return LineCounts[idx];
        }

        private void Tick()
        {
            if (IsClosed)
            {
                Dispose();
                return;
            }

            if (!Following) return;
            Load();
            _Ticks++;
            if (_Ticks % 5 == 0) _OnTick?.Invoke();
            bool completed = _Completed();
            if (completed && !_WasCompleted)
            {
                Load();
                StopFollowing();
            }

            _WasCompleted = completed;
        }

        private void StopFollowing()
        {
            Following = false;
            Viewer.Follow = false;
            _Timer?.Dispose();
            _Timer = null;
        }

        #endregion
    }
}
