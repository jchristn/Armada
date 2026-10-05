namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;

    /// <summary>
    /// The planning transcript (the dashboard's CaptainChatPanel as used by Planning): messages by role with the
    /// captain's name, Markdown replies, tool chips (<c>[ok]</c>, <c>[..]</c>, <c>[x!]</c> with runtime), collapsible
    /// thinking, the per-turn metrics line, the rotating thinking phrase while the captain responds, and the selected
    /// dispatch reply marker. <c>Up</c>/<c>Down</c> move between messages, <c>PgUp</c>/<c>PgDn</c> scroll, <c>End</c>
    /// follows the tail, <c>Enter</c> uses an assistant reply for dispatch, <c>o</c> opens it in Dispatch, <c>t</c>
    /// expands thinking, <c>x</c> shows tool calls, <c>y</c>/<c>Y</c> copy. Not thread-safe.
    /// </summary>
    public class PlanningTranscriptView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Index of the message under the cursor (-1 when empty).
        /// </summary>
        public int Cursor { get; private set; } = -1;

        /// <summary>
        /// True while the view follows the newest content.
        /// </summary>
        public bool FollowTail { get; private set; } = true;

        /// <summary>
        /// Message ids whose thinking is expanded.
        /// </summary>
        public HashSet<string> ExpandedThinking { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Plain text of the transcript (for copy).
        /// </summary>
        public string PlainText
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                foreach (PlanningSessionMessage m in _Owner.Messages)
                {
                    sb.Append(RoleLabel(m)).Append(":\n").Append(m.Content).Append("\n\n");
                }

                return sb.ToString().TrimEnd();
            }
        }

        #endregion

        #region Private-Members

        private readonly PlanningScreen _Owner;
        private int _Scroll = 0;
        private int _Height = 1;
        private List<int> _Starts = new List<int>();
        private int _Total = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="owner">Planning screen.</param>
        public PlanningTranscriptView(PlanningScreen owner)
        {
            _Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The message under the cursor, or null.
        /// </summary>
        /// <returns>Message or null.</returns>
        public PlanningSessionMessage? Current()
        {
            List<PlanningSessionMessage> messages = _Owner.Messages;
            return Cursor >= 0 && Cursor < messages.Count ? messages[Cursor] : null;
        }

        /// <summary>
        /// Move the cursor to the newest message and follow the tail.
        /// </summary>
        public void ToEnd()
        {
            Cursor = _Owner.Messages.Count - 1;
            FollowTail = true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            List<PlanningSessionMessage> messages = _Owner.Messages;
            bool plain = (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (messages.Count == 0) return true;
                    Cursor = Math.Max(0, (Cursor < 0 ? messages.Count : Cursor) - 1);
                    FollowTail = false;
                    return true;
                case KeyCode.Down:
                    if (messages.Count == 0) return true;
                    Cursor = Math.Min(messages.Count - 1, Cursor + 1);
                    FollowTail = Cursor == messages.Count - 1;
                    return true;
                case KeyCode.PageUp:
                    _Scroll = Math.Max(0, _Scroll - Math.Max(1, _Height - 1));
                    FollowTail = false;
                    return true;
                case KeyCode.PageDown:
                    _Scroll += Math.Max(1, _Height - 1);
                    return true;
                case KeyCode.Home:
                    Cursor = messages.Count > 0 ? 0 : -1;
                    _Scroll = 0;
                    FollowTail = false;
                    return true;
                case KeyCode.End:
                    ToEnd();
                    return true;
                case KeyCode.Enter:
                    PlanningSessionMessage? m = Current();
                    if (m != null && PlanningLogic.IsAssistant(m)) _Owner.SelectForDispatch(m.Id);
                    return true;
                case KeyCode.Character:
                    if (!plain) return false;
                    PlanningSessionMessage? cur = Current();
                    switch (key.Rune)
                    {
                        case 'o':
                            if (cur != null && PlanningLogic.IsAssistant(cur)) _Owner.OpenMessageInDispatch(cur.Id);
                            return true;
                        case 't':
                            if (cur != null && !ExpandedThinking.Remove(cur.Id)) ExpandedThinking.Add(cur.Id);
                            return true;
                        case 'x':
                            if (cur != null) _Owner.ShowTools(cur.Id);
                            return true;
                        case 'y':
                            if (cur != null) _Owner.Copy(cur.Content, "Message");
                            return true;
                        case 'Y':
                            _Owner.Copy(PlainText, "Transcript");
                            return true;
                        default:
                            return false;
                    }

                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                if (mouse.Button == MouseButton.WheelUp)
                {
                    _Scroll = Math.Max(0, _Scroll - 3);
                    FollowTail = false;
                }
                else if (mouse.Button == MouseButton.WheelDown)
                {
                    _Scroll += 3;
                }

                return true;
            }

            if (mouse.Kind != MouseEventKind.Press) return false;
            int row = _Scroll + mouse.Y;
            for (int i = _Starts.Count - 1; i >= 0; i--)
            {
                if (row >= _Starts[i])
                {
                    Cursor = i;
                    FollowTail = false;
                    break;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            _Height = Math.Max(1, height);
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 8 || height < 1) return;
            List<PlanningSessionMessage> messages = _Owner.Messages;
            if (Cursor >= messages.Count) Cursor = messages.Count - 1;
            if (FollowTail) Cursor = messages.Count - 1;

            List<StyledText> rows = new List<StyledText>();
            List<int> rowOwner = new List<int>();
            _Starts = new List<int>();
            if (_Owner.CaptainRuntime == AgentRuntimeEnum.Codex) AddWrapped(rows, rowOwner, -1, StyledText.From(T("Codex responses cannot be streamed and will arrive upon completion."), Theme.Info), width);
            if (messages.Count == 0)
            {
                AddWrapped(rows, rowOwner, -1, StyledText.From(T("No transcript yet. Send the first planning message below."), Theme.Muted), width);
            }

            string? lastAssistant = messages.LastOrDefault(PlanningLogic.IsAssistant)?.Id;
            bool busy = _Owner.Busy;
            for (int i = 0; i < messages.Count; i++)
            {
                _Starts.Add(rows.Count);
                foreach (StyledText line in MessageLines(messages[i], busy && messages[i].Id == lastAssistant, i == Cursor)) AddWrapped(rows, rowOwner, i, line, width);
                AddWrapped(rows, rowOwner, i, StyledText.Empty, width);
            }

            bool lastIsUser = messages.Count > 0 && !PlanningLogic.IsAssistant(messages[messages.Count - 1]);
            if (busy && (messages.Count == 0 || lastIsUser))
            {
                AddWrapped(rows, rowOwner, -1, StyledText.From(_Owner.CaptainName + ": " + T(_Owner.ThinkingPhrase), Theme.Info), width);
            }

            _Total = rows.Count;
            int max = Math.Max(0, _Total - _Height);
            if (FollowTail) _Scroll = max;
            else if (Cursor >= 0 && Cursor < _Starts.Count)
            {
                int start = _Starts[Cursor];
                int end = Cursor + 1 < _Starts.Count ? _Starts[Cursor + 1] : _Total;
                if (start < _Scroll) _Scroll = start;
                if (end > _Scroll + _Height && end - start <= _Height) _Scroll = end - _Height;
            }

            _Scroll = Math.Clamp(_Scroll, 0, max);
            for (int r = 0; r < _Height && _Scroll + r < rows.Count; r++)
            {
                int idx = _Scroll + r;
                bool cursorRow = rowOwner[idx] >= 0 && rowOwner[idx] == Cursor;
                CellStyle bg = cursorRow && IsFocused ? Theme.SelectionInactive : Theme.Text;
                if (cursorRow && IsFocused) SurfaceText.FillRow(surface, 0, r, width, bg);
                surface.DrawStyledText(0, r, rows[idx], bg);
                if (cursorRow) surface.DrawText(0, r, ">", Theme.Accent.WithBackground(bg.Background));
            }

            if (!FollowTail && _Scroll < max && height > 1)
            {
                string tag = "[" + T("End") + ": " + T("follow") + "]";
                SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(tag)), height - 1, tag, Theme.Accent, width);
            }
        }

        #endregion

        #region Private-Methods

        private string RoleLabel(PlanningSessionMessage m)
        {
            string role = (m.Role ?? "").ToLowerInvariant();
            if (role == "user") return T("You");
            if (role == "assistant") return _Owner.CaptainName;
            return T("System");
        }

        private static void AddWrapped(List<StyledText> rows, List<int> owners, int owner, StyledText line, int width)
        {
            int w = Math.Max(1, width - 2);
            IReadOnlyList<StyledText> wrapped = line.Width <= w ? new List<StyledText> { line } : TextWrapper.Wrap(line, w);
            foreach (StyledText r in wrapped)
            {
                rows.Add(StyledText.From("  ").Append(r));
                owners.Add(owner);
            }
        }

        private List<StyledText> MessageLines(PlanningSessionMessage m, bool streaming, bool cursor)
        {
            List<StyledText> lines = new List<StyledText>();
            bool assistant = PlanningLogic.IsAssistant(m);
            string role = (m.Role ?? "").ToLowerInvariant();
            CellStyle headStyle = assistant ? Theme.Accent.WithAttribute(CellAttributes.Bold, true) : role == "user" ? Theme.Text.WithAttribute(CellAttributes.Bold, true) : Theme.Muted;
            StyledText head = StyledText.From(RoleLabel(m), headStyle)
                .Append(StyledText.From("  " + _Owner.Context.Loc.FormatRelative(m.CreatedUtc, _Owner.Context.Clock.UtcNow), Theme.Muted));
            if (m.Id == _Owner.SelectedMessageId) head = head.Append(StyledText.From("  [" + T("Dispatch From Session") + "]", Theme.Success));
            if (streaming) head = head.Append(StyledText.From("  " + T("Thinking..."), Theme.Info));
            lines.Add(head);

            if (assistant)
            {
                if (_Owner.Tools.TryGetValue(m.Id, out List<AskToolChip>? tools))
                {
                    foreach (AskToolChip chip in tools)
                    {
                        string mark = chip.Status == AskToolChipStatusEnum.Running ? "[..]" : chip.Status == AskToolChipStatusEnum.Failed ? "[x!]" : "[ok]";
                        CellStyle st = chip.Status == AskToolChipStatusEnum.Running ? Theme.Info : chip.Status == AskToolChipStatusEnum.Failed ? Theme.Error : Theme.Success;
                        string tail = chip.Status == AskToolChipStatusEnum.Running ? "  " + T("running...") : chip.ElapsedMs.HasValue ? "  " + PlanningLogic.Ms(chip.ElapsedMs) : "";
                        lines.Add(StyledText.From(mark + " ", st).Append(StyledText.From(chip.Name + tail + (_Owner.CaptainRuntime == null ? "" : "  (" + _Owner.CaptainRuntime + ")"), Theme.Muted)));
                    }
                }

                if (_Owner.Thinking.TryGetValue(m.Id, out string? thinking) && !String.IsNullOrEmpty(thinking))
                {
                    if (ExpandedThinking.Contains(m.Id))
                    {
                        lines.Add(StyledText.From("[-] " + T("Thinking"), Theme.Muted));
                        foreach (string t in thinking.Replace("\r\n", "\n").Split('\n')) lines.Add(StyledText.From("    " + t, Theme.Muted));
                    }
                    else
                    {
                        lines.Add(StyledText.From("[+] " + T("Thinking") + " (t)", Theme.Muted));
                    }
                }

                if (!String.IsNullOrWhiteSpace(m.Content)) lines.AddRange(MarkdownRenderer.Render(m.Content));
                else if (streaming) lines.Add(StyledText.From(T(_Owner.ThinkingPhrase), Theme.Info));

                if (m.Metrics != null)
                {
                    CaptainChatMetrics x = m.Metrics;
                    string metrics = PlanningLogic.Ms(x.TimeToFirstTokenMs) + " " + T("time to first token") + "   "
                        + PlanningLogic.Ms(x.StreamingMs) + " " + T("streaming") + "   "
                        + (x.TokensPerSecond.HasValue ? x.TokensPerSecond.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-") + " " + T("tokens/sec") + "   "
                        + (x.CompletionTokens.HasValue ? x.CompletionTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-") + " " + T("tokens") + "   "
                        + PlanningLogic.Ms(x.TotalMs) + " " + T("total");
                    lines.Add(StyledText.From(metrics, Theme.Muted));
                }

                if (cursor && !String.IsNullOrWhiteSpace(m.Content))
                {
                    lines.Add(StyledText.From("Enter " + T("Use for dispatch") + "   o " + T("Open in Dispatch") + "   x " + T("Tools") + "   t " + T("Thinking") + "   y " + T("Copy"), Theme.Muted));
                }
            }
            else
            {
                foreach (string t in (m.Content ?? "").Replace("\r\n", "\n").Split('\n')) lines.Add(StyledText.From(t, Theme.Text));
            }

            return lines;
        }

        #endregion
    }
}
