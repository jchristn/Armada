namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Workspace terminal (the dashboard's WorkspaceTerminal): runs one non-interactive command at a time in the
    /// vessel's working tree on the Admiral host, bounded by the server's timeout, and shows the command, stdout,
    /// stderr, and the exit code (or "timed out") with the duration. <c>Enter</c> runs, <c>Up</c>/<c>Down</c> recall
    /// earlier commands, <c>PgUp</c>/<c>PgDn</c> scroll, <c>Ctrl+L</c> clears, <c>Ctrl+Y</c> copies the output, and
    /// <c>Esc</c> closes; the output stays with the Workspace for the next time it opens. Not thread-safe.
    /// </summary>
    public class WorkspaceTerminalDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Command input.
        /// </summary>
        public TextInput Command { get; } = new TextInput();

        /// <summary>
        /// Output lines (shared with the owning screen).
        /// </summary>
        public List<WorkspaceTerminalLine> Lines { get; }

        /// <summary>
        /// Command history, newest first (shared with the owning screen).
        /// </summary>
        public List<string> History { get; }

        /// <summary>
        /// True while a command runs.
        /// </summary>
        public bool Busy { get; private set; } = false;

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;
        private readonly string _VesselId;
        private int _HistoryIndex = -1;
        private int _ScrollFromBottom = 0;
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="lines">Output lines to show and append to.</param>
        /// <param name="history">Command history to use and extend.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public WorkspaceTerminalDialog(OpsScreen screen, string vesselId, List<WorkspaceTerminalLine> lines, List<string> history)
            : base(screen?.Tr("Terminal") ?? "Terminal", screen?.Context.Loc, screen?.Context.Theme.Current)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _VesselId = vesselId ?? throw new ArgumentNullException(nameof(vesselId));
            Lines = lines ?? new List<WorkspaceTerminalLine>();
            History = history ?? new List<string>();
            Command.Placeholder = "Enter a command...";
            Command.Localizer = Localizer;
            Command.ApplyTheme(Theme);
            Command.OnFocusChanged(true);
            FooterHint = " Enter " + T("Run") + "  Up/Down " + T("History") + "  Ctrl+L " + T("Clear") + "  Ctrl+Y " + T("Copy") + "  Esc " + T("Close") + " ";
            MinContentWidth = 50;
            MaxContentWidth = 200;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the typed command.
        /// </summary>
        public void RunCommand()
        {
            string cmd = Command.Value.Trim();
            if (cmd.Length == 0 || Busy) return;
            Command.Value = "";
            History.Insert(0, cmd);
            while (History.Count > 50) History.RemoveAt(History.Count - 1);
            _HistoryIndex = -1;
            Lines.Add(new WorkspaceTerminalLine("command", "$ " + cmd));
            _ScrollFromBottom = 0;
            Busy = true;
            _Screen.Call((c, t) => c.ExecWorkspaceCommandAsync(_VesselId, cmd, null, t), result =>
            {
                Busy = false;
                if (result == null) return;
                if (!String.IsNullOrEmpty(result.Stdout)) Lines.Add(new WorkspaceTerminalLine("stdout", result.Stdout.TrimEnd('\n', '\r')));
                if (!String.IsNullOrEmpty(result.Stderr)) Lines.Add(new WorkspaceTerminalLine("stderr", result.Stderr.TrimEnd('\n', '\r')));
                string status = result.TimedOut ? T("timed out") : Localizer.T("exit {{code}}", LocalizationArgs.Of("code", result.ExitCode));
                Lines.Add(new WorkspaceTerminalLine("meta", status + " - " + Math.Round(result.DurationMs) + "ms"));
                _ScrollFromBottom = 0;
            }, null, ex =>
            {
                Busy = false;
                Lines.Add(new WorkspaceTerminalLine("stderr", ex.Message));
            });
        }

        /// <summary>
        /// Plain text of the output.
        /// </summary>
        /// <returns>Text.</returns>
        public string PlainText()
        {
            return String.Join("\n", Lines.Select(l => l.Text));
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                RequestClose(null);
                return true;
            }

            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character)
            {
                char c = Char.ToLowerInvariant((char)key.Rune);
                if (c == 'l')
                {
                    Lines.Clear();
                    _ScrollFromBottom = 0;
                    return true;
                }

                if (c == 'y')
                {
                    _Screen.Copy(PlainText(), "Output");
                    return true;
                }
            }

            switch (key.Code)
            {
                case KeyCode.Enter:
                    RunCommand();
                    return true;
                case KeyCode.Up:
                    if (History.Count == 0) return true;
                    _HistoryIndex = Math.Min(_HistoryIndex + 1, History.Count - 1);
                    Command.Value = History[_HistoryIndex];
                    return true;
                case KeyCode.Down:
                    _HistoryIndex = Math.Max(_HistoryIndex - 1, -1);
                    Command.Value = _HistoryIndex == -1 ? "" : History[_HistoryIndex];
                    return true;
                case KeyCode.PageUp:
                    _ScrollFromBottom += 10;
                    return true;
                case KeyCode.PageDown:
                    _ScrollFromBottom = Math.Max(0, _ScrollFromBottom - 10);
                    return true;
            }

            if (Busy) return true;
            Command.HandleKey(key);
            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (!Busy) Command.Insert((text ?? "").Replace("\r", " ").Replace("\n", " "));
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _ScreenWidth = surface.Size.Width;
            _ScreenHeight = surface.Size.Height;
            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(50, (int)(_ScreenWidth * 0.9)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(8, (int)(_ScreenHeight * 0.8) - 4);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            int outputHeight = Math.Max(1, height - 2);
            List<KeyValuePair<string, CellStyle>> wrapped = new List<KeyValuePair<string, CellStyle>>();
            foreach (WorkspaceTerminalLine line in Lines)
            {
                CellStyle style = line.Kind == "command" ? On(Theme.Info) : line.Kind == "stderr" ? On(Theme.Error) : line.Kind == "meta" ? Dim() : Body();
                foreach (string raw in line.Text.Replace("\r\n", "\n").Split('\n'))
                {
                    foreach (string part in TextCells.Wrap(raw.Length == 0 ? " " : raw, Math.Max(10, width)))
                        wrapped.Add(new KeyValuePair<string, CellStyle>(part, style));
                }
            }

            if (wrapped.Count == 0)
            {
                SurfaceText.Draw(content, 0, 0, T("Run a command in the vessel working tree (e.g. git status, ls, npm test)."), Dim(), width);
            }
            else
            {
                _ScrollFromBottom = Math.Clamp(_ScrollFromBottom, 0, Math.Max(0, wrapped.Count - outputHeight));
                int start = Math.Max(0, wrapped.Count - outputHeight - _ScrollFromBottom);
                for (int y = 0; y < outputHeight && start + y < wrapped.Count; y++)
                    SurfaceText.Draw(content, 0, y, wrapped[start + y].Key, wrapped[start + y].Value, width);
            }

            int inputY = height - 1;
            string prompt = Busy ? T("Running...") + " " : "$ ";
            int px = SurfaceText.Draw(content, 0, inputY, prompt, Dim(), width);
            Command.Render(new SurfaceView(content, new Rect(px, inputY, Math.Max(1, width - px), 1)));
        }

        #endregion
    }
}
