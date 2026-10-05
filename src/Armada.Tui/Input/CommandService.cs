namespace Armada.Tui.Input
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using TUIKit.Input;
    using Armada.Tui.Services;

    /// <summary>
    /// The single registry of commands. The menu bar, the command palette, and the help overlay are built from it, and
    /// it dispatches key bindings (including two-stroke go-to sequences such as <c>g m</c>) after the focused widget
    /// declines a key. Global commands live for the session; screen commands are replaced when the screen changes.
    /// User rebindings from preferences override defaults. Call on the UI loop thread.
    /// </summary>
    public class CommandService
    {
        #region Public-Members

        /// <summary>
        /// Milliseconds a pending sequence prefix waits for its second stroke. Default 1200; clamped to 100..10000.
        /// </summary>
        public int SequenceTimeoutMs
        {
            get { return _SequenceTimeoutMs; }
            set { _SequenceTimeoutMs = Math.Clamp(value, 100, 10000); }
        }

        /// <summary>
        /// Pending sequence prefix label (for the status bar, for example <c>g</c>), or null.
        /// </summary>
        public string? PendingPrefix
        {
            get { return _Pending?.ToLabel(); }
        }

        /// <summary>
        /// Current screen id whose scoped commands are active, or null.
        /// </summary>
        public string? ActiveScope { get; private set; } = null;

        /// <summary>
        /// Raised after commands are added or removed (menus rebuild).
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Raised after a command runs, with its id (telemetry hook).
        /// </summary>
        public event EventHandler<string>? Executed;

        #endregion

        #region Private-Members

        private readonly List<ArmadaCommand> _Global = new List<ArmadaCommand>();
        private readonly List<ArmadaCommand> _Screen = new List<ArmadaCommand>();
        private readonly Dictionary<string, string> _Overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        private KeyStroke? _Pending = null;
        private DateTime _PendingAt = DateTime.MinValue;
        private int _SequenceTimeoutMs = 1200;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register (or replace by id) a global command.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <returns>The command.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
        public ArmadaCommand Register(ArmadaCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            _Global.RemoveAll(c => c.Id == command.Id);
            ApplyOverride(command);
            _Global.Add(command);
            RaiseChanged();
            return command;
        }

        /// <summary>
        /// Replace the screen-scoped commands (called when the active screen changes).
        /// </summary>
        /// <param name="scope">Screen id, or null when no screen.</param>
        /// <param name="commands">Commands; null clears.</param>
        public void SetScreenCommands(string? scope, IEnumerable<ArmadaCommand>? commands)
        {
            _Screen.Clear();
            ActiveScope = scope;
            if (commands != null)
            {
                foreach (ArmadaCommand c in commands)
                {
                    c.Scope = scope;
                    if (c.Menu == CommandMenuEnum.None) c.Menu = CommandMenuEnum.Actions;
                    ApplyOverride(c);
                    _Screen.Add(c);
                }
            }

            RaiseChanged();
        }

        /// <summary>
        /// Apply user rebindings (command id to gesture text). Invalid gestures are ignored.
        /// </summary>
        /// <param name="overrides">Rebindings; null clears.</param>
        public void SetOverrides(IDictionary<string, string>? overrides)
        {
            _Overrides.Clear();
            if (overrides != null)
            {
                foreach (KeyValuePair<string, string> kvp in overrides) _Overrides[kvp.Key] = kvp.Value;
            }

            foreach (ArmadaCommand c in _Global.Concat(_Screen)) ApplyOverride(c);
            RaiseChanged();
        }

        /// <summary>
        /// Every registered command (screen commands first so they shadow global bindings).
        /// </summary>
        /// <returns>Commands.</returns>
        public IReadOnlyList<ArmadaCommand> All()
        {
            return _Screen.Concat(_Global).ToList();
        }

        /// <summary>
        /// Visible commands for a menu, in registration order.
        /// </summary>
        /// <param name="menu">Menu.</param>
        /// <returns>Commands.</returns>
        public IReadOnlyList<ArmadaCommand> ForMenu(CommandMenuEnum menu)
        {
            IEnumerable<ArmadaCommand> source = menu == CommandMenuEnum.Actions ? _Screen.Concat(_Global) : _Global.Concat(_Screen);
            return source.Where(c => c.Menu == menu && c.Visible).ToList();
        }

        /// <summary>
        /// Find a command by id.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <returns>The command, or null.</returns>
        public ArmadaCommand? Find(string id)
        {
            return _Screen.FirstOrDefault(c => c.Id == id) ?? _Global.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Run a command by id when it is visible and enabled.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="source">Where the command came from (a <c>TuiTelemetry.Source*</c> value), for telemetry.</param>
        /// <returns>True when it ran.</returns>
        public bool Execute(string id, string source = TuiTelemetry.SourceDirect)
        {
            ArmadaCommand? command = Find(id);
            if (command == null) return false;
            return Run(command, source);
        }

        /// <summary>
        /// Run a command when visible and enabled.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <param name="source">Where the command came from (a <c>TuiTelemetry.Source*</c> value), for telemetry.</param>
        /// <returns>True when it ran.</returns>
        public bool Run(ArmadaCommand command, string source = TuiTelemetry.SourceDirect)
        {
            if (command == null || !command.Visible || !command.Enabled) return false;
            Activity? activity = TuiTelemetry.StartCommand(command.Id, source);
            bool ok = false;
            try
            {
                command.Handler();
                ok = true;
            }
            finally
            {
                TuiTelemetry.RecordCommand(activity, command.Id, source, ok);
            }

            EventHandler<string>? handler = Executed;
            if (handler != null) handler(this, command.Id);
            return true;
        }

        /// <summary>
        /// Complete a pending sequence (for example the <c>s</c> of <c>g s</c>) before the screen sees the key, so a
        /// screen that binds the second letter itself (a grid's <c>s</c> sort) cannot swallow it. Returns false and
        /// leaves the key to the screen when nothing is pending or the key completes no sequence (the prefix is
        /// dropped); <c>Esc</c> cancels a pending prefix and is consumed.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="nowUtc">Current time (for the sequence timeout).</param>
        /// <returns>True when consumed.</returns>
        public bool TryCompletePending(KeyEvent key, DateTime nowUtc)
        {
            if (_Pending == null) return false;
            if ((nowUtc - _PendingAt).TotalMilliseconds > _SequenceTimeoutMs)
            {
                _Pending = null;
                return false;
            }

            KeyStroke prefix = _Pending;
            _Pending = null;
            foreach (ArmadaCommand c in All().Where(c => c.Dispatch && c.Visible))
            {
                foreach (KeyGesture g in c.Gestures)
                {
                    if (g.IsSequence && SameStroke(g.Strokes[0], prefix) && g.Strokes[1].Matches(key))
                    {
                        Run(c);
                        return true;
                    }
                }
            }

            return key.Code == KeyCode.Escape;
        }

        /// <summary>
        /// Dispatch a key to bindings. Completes a pending sequence, begins one when the key is a sequence prefix, or
        /// runs a single-stroke binding. Screen bindings win over global ones.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="nowUtc">Current time (for the sequence timeout).</param>
        /// <returns>True when consumed.</returns>
        public bool TryHandleKey(KeyEvent key, DateTime nowUtc)
        {
            List<ArmadaCommand> candidates = All().Where(c => c.Dispatch && c.Visible).ToList();
            if (_Pending != null && (nowUtc - _PendingAt).TotalMilliseconds > _SequenceTimeoutMs) _Pending = null;

            if (_Pending != null)
            {
                KeyStroke prefix = _Pending;
                _Pending = null;
                foreach (ArmadaCommand c in candidates)
                {
                    foreach (KeyGesture g in c.Gestures)
                    {
                        if (g.IsSequence && SameStroke(g.Strokes[0], prefix) && g.Strokes[1].Matches(key))
                        {
                            Run(c, TuiTelemetry.SourceKey);
                            return true;
                        }
                    }
                }

                if (key.Code == KeyCode.Escape) return true;
            }

            foreach (ArmadaCommand c in candidates)
            {
                foreach (KeyGesture g in c.Gestures)
                {
                    if (!g.IsSequence && g.Strokes[0].Matches(key))
                    {
                        if (!c.Enabled) return true;
                        Run(c, TuiTelemetry.SourceKey);
                        return true;
                    }
                }
            }

            foreach (ArmadaCommand c in candidates)
            {
                foreach (KeyGesture g in c.Gestures)
                {
                    if (g.IsSequence && g.Strokes[0].Matches(key))
                    {
                        _Pending = g.Strokes[0];
                        _PendingAt = nowUtc;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Abandon a pending sequence prefix.
        /// </summary>
        public void CancelPending()
        {
            _Pending = null;
        }

        #endregion

        #region Private-Methods

        private void ApplyOverride(ArmadaCommand command)
        {
            if (!_Overrides.TryGetValue(command.Id, out string? gesture) || String.IsNullOrWhiteSpace(gesture)) return;
            try
            {
                command.Gestures = new List<KeyGesture> { new KeyGesture(gesture) };
            }
            catch (FormatException)
            {
                // Keep the default binding when the override is invalid.
            }
        }

        private static bool SameStroke(KeyStroke a, KeyStroke b)
        {
            return a.Code == b.Code && a.Rune == b.Rune && a.Modifiers == b.Modifiers;
        }

        private void RaiseChanged()
        {
            EventHandler? handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        #endregion
    }
}
