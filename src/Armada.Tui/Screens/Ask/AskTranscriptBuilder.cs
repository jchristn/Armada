namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Approvals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Lays out the Ask transcript as blocks of styled lines at a width, rendering each message by kind exactly as the
    /// dashboard's <c>AskMessageList</c> and <c>AskMessageView</c> do: user text, captain replies (tool chips, thinking,
    /// duration, metrics, confirm card, Markdown), ActionProposal confirm cards, ActionResult with the compact card,
    /// WorkUpdate milestones (with "show live card" when the card lives elsewhere), summaries, errors, and system notes;
    /// each tracked item's live work card on its host message; plus "Load earlier messages", the streaming reply
    /// (Markdown re-rendered on every chunk so lists, headings, and code read correctly while streaming), the rotating
    /// waiting phrase, the turn failure, and the empty-state greeting. Markdown renders are cached per text and width,
    /// and message blocks are cached per message with a signature of everything they are built from (text, kind, tool
    /// calls, relative time, expanded sections, captain name, metrics, width, theme, locale), so a rebuild after a
    /// streaming chunk or a clock tick only lays out what changed (W8.5: 5,000-message transcripts). Messages with a
    /// confirm card or a live work card are always rebuilt (they show live state). Not thread-safe.
    /// </summary>
    public class AskTranscriptBuilder
    {
        #region Public-Members

        /// <summary>
        /// Message blocks reused from the cache by the last <see cref="Build"/>.
        /// </summary>
        public int LastReused { get; private set; } = 0;

        /// <summary>
        /// Message blocks laid out by the last <see cref="Build"/>.
        /// </summary>
        public int LastBuilt { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly Dictionary<string, List<StyledText>> _Markdown = new Dictionary<string, List<StyledText>>(StringComparer.Ordinal);
        private Dictionary<string, AskBlockCacheEntry> _Blocks = new Dictionary<string, AskBlockCacheEntry>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTranscriptBuilder()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the blocks.
        /// </summary>
        /// <param name="ask">Ask session.</param>
        /// <param name="view">View state.</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="width">Content width (without the gutter).</param>
        /// <returns>Blocks.</returns>
        public List<AskBlock> Build(AskController ask, AskViewState view, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int width)
        {
            int w = Math.Max(20, width);
            AskConversation conv = ask.Conversation;
            List<AskBlock> blocks = new List<AskBlock>();
            if (conv.HasMore)
            {
                AskBlock older = new AskBlock();
                older.Key = "older";
                older.Kind = AskBlockKindEnum.Older;
                older.Lines.Add(StyledText.From(ask.LoadingOlder ? loc.T("Loading earlier messages...") : "[" + loc.T("Load earlier messages") + "]  Enter", ask.LoadingOlder ? theme.Muted : theme.Link));
                blocks.Add(older);
            }

            AskStreamingTurn? stream = conv.Streaming;
            bool streamVisible = stream != null && (stream.TextLength > 0 || stream.Tools.Count > 0 || stream.ThinkingLength > 0);
            if (conv.Messages.Count == 0 && !streamVisible && !conv.TurnActive)
            {
                blocks.Add(EmptyState(ask, theme, loc, w));
            }
            else
            {
                Dictionary<string, string> hosts = conv.WorkCardHosts();
                HashSet<string> proposalHosts = new HashSet<string>(conv.Messages
                    .Where(m => m.Kind == AskMessageKindEnum.ActionProposal)
                    .Select(m => m.ProposalId ?? m.Proposal?.Id)
                    .Where(id => id != null)
                    .Select(id => id!), StringComparer.Ordinal);
                string? highlight = view.HighlightAt(nowUtc);
                string frame = w + "|" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(theme) + "|" + loc.Locale + "|" + (ask.ActiveCaptain?.Name ?? "") + "|" + PermissionKey(conv.Thread?.CliPermission);
                Dictionary<string, AskBlockCacheEntry> next = new Dictionary<string, AskBlockCacheEntry>(StringComparer.Ordinal);
                int reused = 0;
                int built = 0;
                foreach (AskMessage message in conv.Messages)
                {
                    string? signature = Signature(ask, view, message, hosts, frame, loc, nowUtc);
                    AskBlock? block;
                    if (signature != null && _Blocks.TryGetValue(message.Id, out AskBlockCacheEntry? hit) && hit.Signature == signature)
                    {
                        block = hit.Block;
                        reused++;
                    }
                    else
                    {
                        block = MessageBlock(ask, view, message, hosts, proposalHosts, highlight, theme, loc, nowUtc, w);
                        built++;
                    }

                    if (signature != null && !String.IsNullOrEmpty(message.Id)) next[message.Id] = new AskBlockCacheEntry(signature, block);
                    if (block != null) blocks.Add(block);
                }

                _Blocks = next;
                LastReused = reused;
                LastBuilt = built;
            }

            if (streamVisible) blocks.Add(StreamBlock(ask, view, stream!, theme, loc, nowUtc, w));
            bool waiting = conv.TurnActive && (stream == null || (stream.TextLength == 0 && stream.Tools.Count == 0));
            if (waiting)
            {
                AskBlock wait = new AskBlock();
                wait.Key = "waiting";
                wait.Kind = AskBlockKindEnum.Waiting;
                wait.Focusable = false;
                string phrase = ask.WaitingText;
                wait.Lines.Add(StyledText.From((ask.Stopping ? loc.T("Stopping...") : (phrase.Length > 0 ? loc.T(phrase) : loc.T("Thinking..."))) + "   (Ctrl+C " + loc.T("Stop") + ")", theme.Muted.WithAttribute(CellAttributes.Italic, true)));
                blocks.Add(wait);
            }

            if (conv.TurnError != null && !conv.TurnActive)
            {
                AskBlock err = new AskBlock();
                err.Key = "error";
                err.Kind = AskBlockKindEnum.TurnError;
                err.Lines.AddRange(AskCardRenderer.Para(loc.T("The captain turn failed: {{reason}}", LocalizationArgs.Of("reason", conv.TurnError)), theme.Error, w));
                err.CopyText = conv.TurnError;
                blocks.Add(err);
            }

            if (_Markdown.Count > 2000) _Markdown.Clear();
            return blocks;
        }

        /// <summary>
        /// Render Markdown wrapped to a width (cached).
        /// </summary>
        /// <param name="markdown">Markdown.</param>
        /// <param name="width">Width.</param>
        /// <returns>Lines.</returns>
        public List<StyledText> Markdown(string markdown, int width)
        {
            string text = markdown ?? "";
            string key = width + "|" + text.Length + "|" + text.GetHashCode();
            if (_Markdown.TryGetValue(key, out List<StyledText>? cached)) return cached;
            List<StyledText> lines = new List<StyledText>();
            if (text.Trim().Length > 0)
            {
                foreach (StyledText line in MarkdownRenderer.Render(text))
                {
                    if (line.Width <= width) lines.Add(line);
                    else lines.AddRange(TextWrapper.Wrap(line, Math.Max(1, width)));
                }
            }

            _Markdown[key] = lines;
            return lines;
        }

        /// <summary>
        /// Tool chip lines (the dashboard's <c>ChatToolChips</c>): status glyph, name, result preview, and time; expanded
        /// chips also show the arguments and the result. A chip the CLI refused for lack of permission gets a line
        /// explaining why from the thread's CLI permission resolution.
        /// </summary>
        /// <param name="tools">Chips.</param>
        /// <param name="expanded">Show details.</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="width">Width.</param>
        /// <param name="permission">The thread's CLI permission resolution, or null.</param>
        /// <returns>Lines.</returns>
        public static List<StyledText> ToolChips(IReadOnlyList<AskToolChip> tools, bool expanded, ArmadaTheme theme, LocalizationService loc, int width, CliPermissionResolution? permission = null)
        {
            List<StyledText> lines = new List<StyledText>();
            foreach (AskToolChip tool in tools)
            {
                string glyph = tool.Status == AskToolChipStatusEnum.Running ? "[..]" : tool.Status == AskToolChipStatusEnum.Success ? "[ok]" : "[x!]";
                CellStyle style = tool.Status == AskToolChipStatusEnum.Running ? theme.Info : tool.Status == AskToolChipStatusEnum.Success ? theme.Success : theme.Error;
                StyledText line = StyledText.From(glyph + " ", style).Append(StyledText.From(tool.Name, theme.Code));
                if (tool.Status != AskToolChipStatusEnum.Running && !String.IsNullOrEmpty(tool.Result)) line = line.Append(StyledText.From("  " + Preview(tool.Result!), theme.Muted));
                line = line.Append(StyledText.From("  " + (tool.Status == AskToolChipStatusEnum.Running ? loc.T("running...") : ToolMs(tool.ElapsedMs)), theme.Muted));
                lines.Add(Clip(line, width));
                if (tool.PermissionDenied && tool.Status != AskToolChipStatusEnum.Running) lines.AddRange(AskCliPermissionCard.DeniedLines(permission, theme, loc, width));
                if (!expanded) continue;
                if (!String.IsNullOrEmpty(tool.Arguments))
                {
                    lines.Add(StyledText.From("    " + loc.T("Arguments"), theme.Muted));
                    lines.AddRange(AskCardRenderer.Para(ApprovalActions.Pretty(tool.Arguments), theme.Code, width, "      "));
                }

                if (!String.IsNullOrEmpty(tool.Result))
                {
                    lines.Add(StyledText.From("    " + loc.T("Result"), theme.Muted));
                    lines.AddRange(AskCardRenderer.Para(ApprovalActions.Pretty(tool.Result), theme.Code, width, "      "));
                }

                if (String.IsNullOrEmpty(tool.Arguments) && String.IsNullOrEmpty(tool.Result)) lines.Add(StyledText.From("    " + loc.T("No details available."), theme.Muted));
            }

            return lines;
        }

        /// <summary>
        /// Persisted tool calls as chips (the dashboard's <c>toolCallsToEvents</c>).
        /// </summary>
        /// <param name="calls">Calls.</param>
        /// <returns>Chips.</returns>
        public static List<AskToolChip> ChipsFor(IEnumerable<AskMessageToolCall>? calls)
        {
            List<AskToolChip> chips = new List<AskToolChip>();
            int i = 0;
            foreach (AskMessageToolCall call in calls ?? Enumerable.Empty<AskMessageToolCall>())
            {
                AskToolChip chip = new AskToolChip();
                chip.Id = !String.IsNullOrEmpty(call.CallId) ? call.CallId! : !String.IsNullOrEmpty(call.Id) ? call.Id : "call-" + i;
                chip.Name = String.IsNullOrEmpty(call.ToolName) ? "tool" : call.ToolName;
                // A call the CLI refused for lack of permission is a failure whatever its ok flag says (the dashboard's
                // toolCallsToEvents), so it shows [x!] with the explanation under it.
                chip.Status = call.PermissionDenied == true || call.Ok == false ? AskToolChipStatusEnum.Failed : (call.Ok == null && String.IsNullOrEmpty(call.ResultText) ? AskToolChipStatusEnum.Running : AskToolChipStatusEnum.Success);
                chip.Arguments = call.ArgumentsText;
                chip.Result = call.ResultText;
                chip.ElapsedMs = call.ElapsedMs;
                chip.PermissionDenied = call.PermissionDenied == true;
                chips.Add(chip);
                i++;
            }

            return chips;
        }

        #endregion

        #region Private-Methods

        private static string? Signature(AskController ask, AskViewState view, AskMessage message, Dictionary<string, string> hosts, string frame, LocalizationService loc, DateTime nowUtc)
        {
            AskConversation conv = ask.Conversation;
            string? workId = message.TrackedWorkId ?? message.TrackedWork?.Id;
            if (workId != null && hosts.TryGetValue(workId, out string? host) && host == message.Id) return null;
            if (conv.ProposalFor(message) != null) return null;
            CliPermissionRequest? cliRequest = conv.CliPermissionFor(message);
            if (message.Kind == AskMessageKindEnum.CliPermission && (cliRequest == null || cliRequest.Status == CliPermissionRequestStatusEnum.Pending)) return null;
            string text = message.ContentText ?? "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
            sb.Append(frame).Append('|').Append((int)message.Kind).Append('|').Append((int)message.Role)
                .Append('|').Append(text.Length).Append(':').Append(text.GetHashCode())
                .Append('|').Append(loc.FormatRelative(message.CreatedUtc, nowUtc))
                .Append('|').Append(AskConversation.IsLocal(message) ? 'L' : 'P')
                .Append('|').Append(workId ?? "")
                .Append('|').Append(message.CaptainId ?? "").Append('=').Append(ask.CaptainName(message.CaptainId) ?? "")
                .Append('|').Append(message.DurationMs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")
                .Append('|').Append(message.ThinkingText?.Length ?? -1).Append(':').Append(message.ThinkingText?.GetHashCode() ?? 0)
                .Append('|').Append(view.ExpandedTools.Contains(message.Id) ? 'T' : 't')
                .Append(view.ExpandedThinking.Contains(message.Id) ? 'K' : 'k');
            if (cliRequest != null)
            {
                CliPermissionRequest request = cliRequest;
                sb.Append("|p").Append(request.Status).Append(':').Append(request.DecisionSource?.ToString() ?? "").Append(':').Append(request.DecisionMessage ?? "");
            }

            if (message.ToolCalls != null)
            {
                foreach (AskMessageToolCall call in message.ToolCalls)
                {
                    sb.Append('|').Append(call.ToolName).Append(':').Append(call.Ok?.ToString() ?? "?")
                        .Append(':').Append(call.ArgumentsText?.Length ?? -1).Append(':').Append(call.ArgumentsText?.GetHashCode() ?? 0)
                        .Append(':').Append(call.ResultText?.Length ?? -1).Append(':').Append(call.ResultText?.GetHashCode() ?? 0)
                        .Append(':').Append(call.ElapsedMs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")
                        .Append(':').Append(call.PermissionDenied == true ? 'D' : 'd');
                }
            }

            if (conv.Metrics.TryGetValue(message.Id, out AskTurnMetrics? metrics))
            {
                sb.Append("|m").Append(metrics.Describe("a", "b", "c", "d"));
            }

            return sb.ToString();
        }

        private static string PermissionKey(CliPermissionResolution? resolution)
        {
            if (resolution == null) return "";
            return resolution.Effective + ":" + resolution.Source + ":" + (resolution.FallbackReason?.ToString() ?? "");
        }

        private AskBlock EmptyState(AskController ask, ArmadaTheme theme, LocalizationService loc, int w)
        {
            AskBlock block = new AskBlock();
            block.Key = "empty";
            block.Kind = AskBlockKindEnum.Empty;
            block.Focusable = false;
            if (ask.Conversation.Thread != null || ask.Conversation.ThreadId != null)
            {
                block.Lines.Add(StyledText.From(loc.T("Send the first message to begin."), theme.Muted));
                return block;
            }

            block.Lines.Add(StyledText.Empty);
            block.Lines.AddRange(AskCardRenderer.Para(loc.T(ask.Greeting), theme.Accent.WithAttribute(CellAttributes.Bold, true), w));
            block.Lines.Add(StyledText.Empty);
            Captain? captain = ask.ActiveCaptain;
            string sub = captain != null
                ? loc.T("Ask {{name}} anything about your fleet, or start work with a quick action.", LocalizationArgs.Of("name", captain.Name))
                : loc.T("Choose a captain to chat, or start work with a quick action.");
            block.Lines.AddRange(AskCardRenderer.Para(sub, theme.Muted, w));
            block.Lines.Add(StyledText.Empty);
            foreach (AskQuickAction action in ask.QuickActions)
            {
                StyledText line = StyledText.From("  " + TextCells.PadRight(AskQuickActions.CommandOf(action), 16), theme.Code);
                if (!String.IsNullOrEmpty(action.Description)) line = line.Append(StyledText.From(loc.T(action.Description), theme.Muted));
                block.Lines.Add(Clip(line, w));
            }

            return block;
        }

        private AskBlock? MessageBlock(AskController ask, AskViewState view, AskMessage message, Dictionary<string, string> hosts, HashSet<string> proposalHosts, string? highlight, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int w)
        {
            AskConversation conv = ask.Conversation;
            string? workId = message.TrackedWorkId ?? message.TrackedWork?.Id;
            bool hostsCard = workId != null && hosts.TryGetValue(workId, out string? host) && host == message.Id;
            AskActionProposal? resolved = conv.ProposalFor(message);
            AskActionProposal? proposal = resolved != null && message.Kind == AskMessageKindEnum.ActionResult && proposalHosts.Contains(resolved.Id) ? null : resolved;
            string text = message.ContentText ?? "";
            string when = loc.FormatRelative(message.CreatedUtc, nowUtc);
            bool local = AskConversation.IsLocal(message);
            AskBlock block = new AskBlock();
            block.Key = message.Id;
            block.Message = message;
            block.Proposal = proposal;
            block.CopyText = text;
            List<StyledText> lines = block.Lines;

            if (message.Kind == AskMessageKindEnum.Text && message.Role == AskMessageRoleEnum.Assistant && text.Trim().Length == 0 && (message.ToolCalls == null || message.ToolCalls.Count == 0))
                return null;

            if (message.Kind == AskMessageKindEnum.ActionProposal)
            {
                if (text.Length > 0 && (proposal == null || String.IsNullOrEmpty(proposal.SummaryText))) lines.AddRange(AskCardRenderer.Para(text, theme.Muted, w));
                if (proposal != null) AddConfirmCard(block, lines, proposal, false, view, ask, theme, loc, nowUtc, w);
                else lines.AddRange(Markdown(text.Length > 0 ? text : loc.T("A proposed action is loading..."), w));
            }
            else if (message.Kind == AskMessageKindEnum.ActionResult)
            {
                lines.Add(Header(StyledText.From("* " + loc.T("Action result"), theme.Accent.WithAttribute(CellAttributes.Bold, true)), when, theme, w));
                lines.AddRange(Markdown(text, w));
                if (proposal != null) AddConfirmCard(block, lines, proposal, true, view, ask, theme, loc, nowUtc, w);
            }
            else if (message.Kind == AskMessageKindEnum.WorkUpdate)
            {
                StyledText head = StyledText.From("- " + loc.T("Progress update"), theme.Info.WithAttribute(CellAttributes.Bold, true));
                if (workId != null && !hostsCard)
                {
                    head = head.Append(StyledText.From("   [" + loc.T("Show live card") + "] Enter", theme.Link));
                    block.LinkedWorkId = workId;
                }

                lines.Add(Header(head, when, theme, w));
                foreach (StyledText line in Markdown(text, w - 2)) lines.Add(StyledText.From("  ", theme.Muted).Append(line));
            }
            else if (message.Kind == AskMessageKindEnum.Summary)
            {
                lines.Add(Header(StyledText.From("= " + loc.T("Conversation summary"), theme.Accent.WithAttribute(CellAttributes.Bold, true)), when, theme, w));
                lines.AddRange(Markdown(text, w));
            }
            else if (message.Kind == AskMessageKindEnum.CliPermission)
            {
                CliPermissionRequest? request = conv.CliPermissionFor(message);
                block.CliRequest = request;
                List<AskCardButton> buttons = new List<AskCardButton>();
                int offset = lines.Count;
                bool busy = request != null && ask.IsCliPermissionBusy(request.Id);
                lines.AddRange(AskCliPermissionCard.Lines(request, text, busy, view.FocusedKey == message.Id, theme, loc, nowUtc, w, buttons));
                foreach (AskCardButton button in buttons)
                {
                    button.Line += offset;
                    block.Buttons.Add(button);
                }
            }
            else if (message.Kind == AskMessageKindEnum.Error)
            {
                lines.Add(Header(StyledText.From("! " + loc.T("Error"), theme.Error.WithAttribute(CellAttributes.Bold, true)), when, theme, w));
                lines.AddRange(AskCardRenderer.Para(text.Length > 0 ? text : loc.T("Something went wrong."), theme.Error, w));
            }
            else if (message.Role == AskMessageRoleEnum.User)
            {
                lines.Add(Header(StyledText.From(loc.T("You"), theme.Accent.WithAttribute(CellAttributes.Bold, true)), local ? loc.T("Sending...") : when, theme, w));
                lines.AddRange(AskCardRenderer.Para(text, theme.Text, w, "  "));
            }
            else if (message.Role == AskMessageRoleEnum.System)
            {
                lines.AddRange(Markdown(text, w).Select(l => l.Style(theme.Muted)));
            }
            else
            {
                List<AskToolChip> chips = ChipsFor(message.ToolCalls);
                if (chips.Count > 0) lines.AddRange(ToolChips(chips, view.ExpandedTools.Contains(block.Key), theme, loc, w, conv.Thread?.CliPermission));
                string name = ask.CaptainName(message.CaptainId) ?? ask.ActiveCaptain?.Name ?? loc.T("Captain");
                StyledText head = StyledText.From(name, theme.Success.WithAttribute(CellAttributes.Bold, true));
                if (message.DurationMs != null) head = head.Append(StyledText.From("  " + AskTurnMetrics.FormatDuration(message.DurationMs), theme.Muted));
                lines.Add(Header(head, when, theme, w));
                if (conv.Metrics.TryGetValue(message.Id, out AskTurnMetrics? metrics))
                    lines.Add(StyledText.From("  " + metrics.Describe(loc.T("first token"), loc.T("tok/s"), loc.T("tokens"), loc.T("total")), theme.Muted));
                if (!String.IsNullOrWhiteSpace(message.ThinkingText)) lines.AddRange(Thinking(message.ThinkingText!, view.ExpandedThinking.Contains(block.Key), false, theme, loc, w));
                if (proposal != null) AddConfirmCard(block, lines, proposal, false, view, ask, theme, loc, nowUtc, w);
                lines.AddRange(Markdown(text, w));
            }

            if (hostsCard && workId != null)
            {
                block.WorkId = workId;
                AskTrackedWork? work = conv.Work(workId) ?? message.TrackedWork;
                lines.AddRange(AskCardRenderer.WorkCard(block, work, conv.SnapshotFor(workId), highlight == workId, lines.Count, theme, loc, nowUtc, w));
            }

            lines.Add(StyledText.Empty);
            return block;
        }

        private AskBlock StreamBlock(AskController ask, AskViewState view, AskStreamingTurn stream, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int w)
        {
            AskBlock block = new AskBlock();
            block.Key = "stream";
            block.Kind = AskBlockKindEnum.Stream;
            string text = stream.Text;
            block.CopyText = text;
            if (stream.Tools.Count > 0) block.Lines.AddRange(ToolChips(stream.Tools, view.ExpandedTools.Contains("stream"), theme, loc, w, ask.Conversation.Thread?.CliPermission));
            string name = ask.ActiveCaptain?.Name ?? loc.T("Captain");
            StyledText head = StyledText.From(name, theme.Success.WithAttribute(CellAttributes.Bold, true));
            head = head.Append(StyledText.From(stream.Finished ? "" : "  " + loc.T("replying..."), theme.Info));
            block.Lines.Add(head);
            block.Lines.Add(StyledText.From("  " + stream.Metrics(nowUtc).Describe(loc.T("first token"), loc.T("tok/s"), loc.T("tokens"), loc.T("total")), theme.Muted));
            if (stream.ThinkingLength > 0) block.Lines.AddRange(Thinking(stream.Thinking, !stream.Finished || view.ExpandedThinking.Contains("stream"), !stream.Finished, theme, loc, w));
            if (text.Length > 0) block.Lines.AddRange(Markdown(text, w));
            if (!stream.Finished && text.Length > 0) block.Lines.Add(StyledText.From("_", theme.Accent));
            block.Lines.Add(StyledText.Empty);
            return block;
        }

        private static List<StyledText> Thinking(string thinking, bool expanded, bool live, ArmadaTheme theme, LocalizationService loc, int w)
        {
            List<StyledText> lines = new List<StyledText>();
            string label = live ? loc.T("Thinking...") : loc.T("Thinking");
            lines.Add(StyledText.From((expanded ? "v " : "> ") + label + (expanded ? "" : "  (t " + loc.T("show") + ")"), theme.Muted.WithAttribute(CellAttributes.Italic, true)));
            if (expanded) lines.AddRange(AskCardRenderer.Para(thinking.Trim(), theme.Muted, w, "  "));
            return lines;
        }

        private static void AddConfirmCard(AskBlock block, List<StyledText> lines, AskActionProposal proposal, bool compact, AskViewState view, AskController ask, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int w)
        {
            List<AskCardButton> buttons = new List<AskCardButton>();
            int offset = lines.Count;
            lines.AddRange(AskCardRenderer.ConfirmCard(proposal, compact, view.ExpandedArguments.Contains(proposal.Id), ask.IsProposalBusy(proposal.Id), view.FocusedKey == block.Key, theme, loc, nowUtc, w, buttons));
            foreach (AskCardButton button in buttons)
            {
                button.Line += offset;
                block.Buttons.Add(button);
            }
        }

        private static StyledText Header(StyledText left, string right, ArmadaTheme theme, int w)
        {
            int gap = Math.Max(2, w - left.Width - TextCells.Width(right));
            if (left.Width + 2 + TextCells.Width(right) > w) return Clip(left, w);
            return left.Append(StyledText.From(new string(' ', gap) + right, theme.Muted));
        }

        private static string Preview(string raw)
        {
            string text = raw;
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(raw))
                {
                    text = System.Text.Json.JsonSerializer.Serialize(doc.RootElement);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                text = raw;
            }

            text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
            return text.Length > 60 ? text.Substring(0, 60) + "..." : text;
        }

        private static string ToolMs(double? ms)
        {
            if (ms == null) return "";
            double v = ms.Value;
            if (v < 1000) return Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms";
            return (v / 1000.0).ToString(v < 10000 ? "0.00" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }

        private static StyledText Clip(StyledText text, int width)
        {
            if (text.Width <= width) return text;
            List<StyledSpan> spans = new List<StyledSpan>();
            int used = 0;
            foreach (StyledSpan span in text.Spans)
            {
                int sw = TextCells.Width(span.Text);
                if (used + sw <= width - 3)
                {
                    spans.Add(span);
                    used += sw;
                    continue;
                }

                string clipped = TextCells.Clip(span.Text, Math.Max(0, width - 3 - used));
                spans.Add(new StyledSpan(clipped + "...", span.Style));
                break;
            }

            return new StyledText(spans);
        }

        #endregion
    }
}
