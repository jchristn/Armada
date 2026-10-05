namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using Armada.Client;
    using Armada.Tui;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Terminal;

    /// <summary>
    /// Drives the full Armada TUI headlessly: a <see cref="HeadlessBackend"/>, a TUIKit application (never started, so
    /// several hosts can run sequentially), the real composition root with a stub or real client, temporary preference
    /// and credential files, keystrokes fed as terminal bytes through TUIKit's parser and routing, and text snapshots of
    /// the composed frame including modals.
    /// </summary>
    public sealed class TuiTestHost : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Backend.
        /// </summary>
        public HeadlessBackend Backend { get; }

        /// <summary>
        /// The adapter the TUI writes through (wraps <see cref="Backend"/>).
        /// </summary>
        public TerminalBackendAdapter Adapter { get; }

        /// <summary>
        /// Application.
        /// </summary>
        public TuiApplication App { get; }

        /// <summary>
        /// TUI.
        /// </summary>
        public ArmadaTuiApp Tui { get; }

        /// <summary>
        /// Temporary directory (preferences, credentials).
        /// </summary>
        public string TempDir { get; }

        /// <summary>
        /// Columns.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Rows.
        /// </summary>
        public int Height { get; private set; }

        #endregion

        #region Private-Members

        private bool _Started = false;

        private static readonly Dictionary<string, string> _Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = "\r", ["tab"] = "\t", ["shift+tab"] = "\u001b[Z", ["esc"] = "\u001b", ["up"] = "\u001b[A",
            ["down"] = "\u001b[B", ["right"] = "\u001b[C", ["left"] = "\u001b[D", ["home"] = "\u001b[H", ["end"] = "\u001b[F",
            ["pgup"] = "\u001b[5~", ["pgdn"] = "\u001b[6~", ["del"] = "\u001b[3~", ["backspace"] = "\u007f", ["f1"] = "\u001bOP",
            ["f2"] = "\u001bOQ", ["f5"] = "\u001b[15~", ["f6"] = "\u001b[17~", ["f10"] = "\u001b[21~", ["f12"] = "\u001b[24~",
            ["alt+left"] = "\u001b[1;3D", ["alt+right"] = "\u001b[1;3C", ["shift+up"] = "\u001b[1;2A", ["shift+down"] = "\u001b[1;2B",
            ["alt+up"] = "\u001b[1;3A", ["alt+down"] = "\u001b[1;3B",
            ["space"] = " "
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a host.
        /// </summary>
        /// <param name="width">Columns.</param>
        /// <param name="height">Rows.</param>
        /// <param name="handler">Stub HTTP handler, or null to use real HTTP (end-to-end).</param>
        /// <param name="serverUrl">Server URL.</param>
        /// <param name="configure">Adjust start options, or null.</param>
        public TuiTestHost(int width, int height, StubHttpHandler? handler, string serverUrl = "http://127.0.0.1:9", Action<TuiStartOptions>? configure = null)
        {
            Width = width;
            Height = height;
            TempDir = Path.Combine(Path.GetTempPath(), "armada-tui-test-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(TempDir);
            Backend = new HeadlessBackend(width, height);
            Adapter = new TerminalBackendAdapter(Backend);
            App = new TuiApplication(Adapter);
            TuiStartOptions options = new TuiStartOptions();
            options.ServerUrl = serverUrl;
            options.PreferencesPath = Path.Combine(TempDir, "tui.json");
            options.Live = false;
            options.Utf8Probe = () => true;
            configure?.Invoke(options);
            Func<string, ArmadaClient>? factory = handler != null
                ? (Func<string, ArmadaClient>)(url => new ArmadaClient(new ArmadaClientOptions(url), handler))
                : null;
            Tui = new ArmadaTuiApp(App, Adapter, options, factory, new FileCredentialStore(Path.Combine(TempDir, "creds.json")));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run startup and wait for it.
        /// </summary>
        /// <returns>This host.</returns>
        public TuiTestHost Start()
        {
            Tui.Start();
            PumpUntil(() => Tui.StartupTask.IsCompleted, 10000);
            Pump();
            return this;
        }

        /// <summary>
        /// Process posted actions and pending input a few times.
        /// </summary>
        public void Pump()
        {
            for (int i = 0; i < 4; i++)
            {
                App.PumpInputOnce();
            }
        }

        /// <summary>
        /// Pump until a condition holds.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>True when it held before the timeout.</returns>
        public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                App.PumpInputOnce();
                if (condition()) return true;
                Thread.Sleep(5);
            }

            App.PumpInputOnce();
            return condition();
        }

        /// <summary>
        /// Press a named key (enter, tab, esc, f5, alt+left, ctrl+k, ...) or a single character.
        /// </summary>
        /// <param name="name">Key name.</param>
        /// <returns>This host.</returns>
        public TuiTestHost Press(string name)
        {
            Backend.FeedInput(Bytes(name));
            Pump();
            return this;
        }

        /// <summary>
        /// Type text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>This host.</returns>
        public TuiTestHost Type(string text)
        {
            Backend.FeedInput(text);
            Pump();
            return this;
        }

        /// <summary>
        /// Start the TUIKit application on the headless backend (once), so it composes real frames through
        /// <see cref="TuiApplication.RenderOnce"/>, keeps a hit map, and can be audited (<c>FocusAudit</c>). Only one
        /// started application may exist at a time; <see cref="Dispose"/> stops it.
        /// </summary>
        public void StartApp()
        {
            if (_Started) return;
            App.Start();
            _Started = true;
        }

        /// <summary>
        /// Click the left mouse button at a cell (zero-based) through TUIKit's real input path: the headless backend
        /// receives the SGR press and release (<see cref="HeadlessBackend.FeedClick"/>), and the application parses
        /// them, hit-tests its last frame, focuses the region, synthesizes the click, and routes it to the widget
        /// under the pointer (the shell, bound to the full-screen region), the way a terminal's click arrives. The
        /// first click starts the application (TUIKit composes and keeps a hit map only once started) and every click
        /// renders a frame first, so the hit map matches the screen. Only one started application may exist at a
        /// time; <see cref="Dispose"/> stops it.
        /// </summary>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <returns>This host.</returns>
        public TuiTestHost Click(int x, int y)
        {
            Pump();
            StartApp();
            App.RenderOnce();
            Backend.FeedClick(x, y);
            Pump();
            return this;
        }

        /// <summary>
        /// Paste text (bracketed paste).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>This host.</returns>
        public TuiTestHost Paste(string text)
        {
            Backend.FeedInput("\u001b[200~" + text + "\u001b[201~");
            Pump();
            return this;
        }

        /// <summary>
        /// Resize the virtual terminal.
        /// </summary>
        /// <param name="width">Columns.</param>
        /// <param name="height">Rows.</param>
        public void Resize(int width, int height)
        {
            Width = width;
            Height = height;
            Backend.Resize(width, height);
        }

        /// <summary>
        /// The composed frame as text.
        /// </summary>
        /// <returns>Text.</returns>
        public string Screen()
        {
            Pump();
            return TuiSnapshot.Render(Tui.Shell, App, Width, Height);
        }

        /// <summary>
        /// Wait until the frame contains text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>True when found.</returns>
        public bool WaitForText(string text, int timeoutMs = 5000)
        {
            return PumpUntil(() => TuiSnapshot.Render(Tui.Shell, App, Width, Height).Contains(text, StringComparison.Ordinal), timeoutMs);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            try { Tui.Dispose(); } catch (Exception) { }
            try { App.Dispose(); } catch (Exception) { }
            try { Backend.Dispose(); } catch (Exception) { }
            try { Directory.Delete(TempDir, true); } catch (Exception) { }
        }

        #endregion

        #region Private-Methods

        private static string Bytes(string name)
        {
            if (_Keys.TryGetValue(name, out string? bytes)) return bytes;
            if (name.StartsWith("ctrl+", StringComparison.OrdinalIgnoreCase) && name.Length == 6)
            {
                char c = Char.ToLowerInvariant(name[5]);
                return ((char)(c - 'a' + 1)).ToString();
            }

            if (name.StartsWith("alt+", StringComparison.OrdinalIgnoreCase) && name.Length == 5) return "\u001b" + name[4];
            return name;
        }

        #endregion
    }
}
