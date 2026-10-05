namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Renders a CLI permission card in the Ask transcript (an <see cref="AskMessage"/> of kind
    /// <see cref="AskMessageKindEnum.CliPermission"/>): tool, command or input summary, captain (and vessel and mission
    /// for mission prompts), status, the expiry countdown while pending, and, for a request the user may decide, the
    /// clickable [Allow once] (a), [Allow and remember] (A, admins), and [Deny] (d) buttons (the keys spelled out under
    /// them while the card is selected), or that an admin must decide. Also builds the line under a tool chip the CLI refused for lack of permission. Thread-safe
    /// (stateless).
    /// </summary>
    public static class AskCliPermissionCard
    {
        #region Public-Methods

        /// <summary>
        /// Lines of the card.
        /// </summary>
        /// <param name="request">The linked request, or null when the message carries none (the message text is shown).</param>
        /// <param name="fallbackText">Message text ("Tool: summary").</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="width">Width in cells.</param>
        /// <returns>Lines.</returns>
        public static List<StyledText> Lines(CliPermissionRequest? request, string? fallbackText, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int width)
        {
            return Lines(request, fallbackText, false, false, theme, loc, nowUtc, width, null);
        }

        /// <summary>
        /// Lines of the card, also reporting where its clickable buttons were drawn.
        /// </summary>
        /// <param name="request">The linked request, or null when the message carries none (the message text is shown).</param>
        /// <param name="fallbackText">Message text ("Tool: summary").</param>
        /// <param name="busy">A decision call is in flight.</param>
        /// <param name="focused">The card is selected in the focused transcript (its keys are spelled out).</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="width">Width in cells.</param>
        /// <param name="buttons">Receives the buttons (line within the returned lines, cell range, action), or null.</param>
        /// <returns>Lines.</returns>
        public static List<StyledText> Lines(CliPermissionRequest? request, string? fallbackText, bool busy, bool focused, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int width, List<AskCardButton>? buttons)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            int inner = Math.Max(10, width - 4);
            List<StyledText> body = new List<StyledText>();
            if (request == null)
            {
                body.AddRange(AskCardRenderer.Para(String.IsNullOrWhiteSpace(fallbackText) ? loc.T("A CLI permission request is loading...") : fallbackText!, theme.Code, inner));
                return AskCardRenderer.Box(StyledText.From(loc.T("CLI permission"), theme.Accent), StyledText.Empty, body, theme.Border, width);
            }

            bool pending = request.Status == CliPermissionRequestStatusEnum.Pending;
            bool expired = pending && request.ExpiresUtc <= nowUtc;
            StyledText title = StyledText.From(pending ? loc.T("Permission needed") : loc.T("CLI permission"), pending ? theme.Warning.WithAttribute(CellAttributes.Bold, true) : theme.Accent)
                .Append(StyledText.From("  " + (String.IsNullOrEmpty(request.ToolName) ? "tool" : request.ToolName) + "  ", theme.Code));
            CellStyle statusStyle = StatusStyle(request.Status, theme);
            StyledText right = StyledText.From(Marker(request.Status) + " " + CliPermissionText.Status(loc, request.Status), statusStyle);
            string summary = !String.IsNullOrWhiteSpace(request.SummaryText) ? request.SummaryText : (fallbackText ?? "");
            if (summary.Trim().Length > 0) body.AddRange(AskCardRenderer.Para(summary.Trim(), theme.Code, inner));
            string where = CliPermissionText.Where(loc, request);
            if (where.Length > 0) body.AddRange(AskCardRenderer.Para(where, theme.Muted, inner));
            int decisionIndex = -1;
            List<int> starts = new List<int>();
            List<int> widths = new List<int>();
            List<AskCardActionEnum> actions = new List<AskCardActionEnum>();
            if (pending)
            {
                body.Add(StyledText.From(CliPermissionText.ExpiresIn(loc, request.ExpiresUtc, nowUtc), expired ? theme.Muted : theme.Warning));
                if (!request.CanDecide)
                {
                    body.AddRange(AskCardRenderer.Para(loc.T("Waiting for an admin to decide."), theme.Muted, inner));
                }
                else if (busy)
                {
                    body.Add(StyledText.From(loc.T("Working..."), theme.Info));
                }
                else
                {
                    body.AddRange(AskCardRenderer.Para(loc.T("The captain is waiting. Decide here or in the Approvals center (Ctrl+A)."), theme.Muted, inner));
                    // Buttons work with the mouse whatever has keyboard focus; the key in parentheses works once the card
                    // has focus.
                    StyledText line = StyledText.Empty;
                    int x = 0;
                    Action<string, string, CellStyle, AskCardActionEnum> add = (label, key, style, action) =>
                    {
                        string text = "[" + label + "]";
                        string suffix = " (" + key + ")   ";
                        starts.Add(x);
                        widths.Add(TextCells.Width(text));
                        actions.Add(action);
                        line = line.Append(StyledText.From(text, style)).Append(StyledText.From(suffix, theme.Muted));
                        x += TextCells.Width(text) + TextCells.Width(suffix);
                    };
                    add(loc.T("Allow once"), "a", theme.Success.WithAttribute(CellAttributes.Bold, true), AskCardActionEnum.AllowOnce);
                    if (request.CanRemember) add(loc.T("Allow and remember"), "A", theme.Success, AskCardActionEnum.AllowAndRemember);
                    add(loc.T("Deny"), "d", theme.Error.WithAttribute(CellAttributes.Bold, true), AskCardActionEnum.Deny);
                    decisionIndex = body.Count;
                    body.Add(line);
                    if (focused)
                    {
                        string keys = "> a " + loc.T("Allow once") + (request.CanRemember ? "   A " + loc.T("Allow and remember") : "") + "   d " + loc.T("Deny");
                        body.Add(StyledText.From(keys, theme.Accent.WithAttribute(CellAttributes.Bold, true)));
                    }
                }
            }
            else if (request.Status == CliPermissionRequestStatusEnum.Allowed)
            {
                string allowed = request.DecisionSource == CliPermissionDecisionSourceEnum.AllowRule ? loc.T("Allowed by a saved rule.") : loc.T("Allowed. The captain ran the tool.");
                body.Add(StyledText.From(allowed, theme.Success));
            }
            else if (request.Status == CliPermissionRequestStatusEnum.Denied)
            {
                string denied = request.DecisionSource == CliPermissionDecisionSourceEnum.DenyRule ? loc.T("Denied by a saved rule.") : loc.T("Denied. The tool did not run.");
                body.Add(StyledText.From(denied, theme.Error));
                if (!String.IsNullOrWhiteSpace(request.DecisionMessage)) body.AddRange(AskCardRenderer.Para(request.DecisionMessage!, theme.Muted, inner, "  "));
            }
            else if (request.Status == CliPermissionRequestStatusEnum.Expired)
            {
                body.Add(StyledText.From(loc.T("Expired without a decision. The tool did not run."), theme.Muted));
            }
            else if (request.Status == CliPermissionRequestStatusEnum.Cancelled)
            {
                body.Add(StyledText.From(loc.T("Cancelled. The turn ended before a decision."), theme.Muted));
            }

            if (buttons != null && decisionIndex >= 0 && body[decisionIndex].Width <= inner)
            {
                // The box draws a top edge, then each body line (wrapped when wider than the box), each prefixed "| ".
                int lineNo = 1;
                for (int i = 0; i < decisionIndex; i++) lineNo += body[i].Width <= inner ? 1 : TextWrapper.Wrap(body[i], inner).Count;
                for (int b = 0; b < actions.Count; b++)
                    buttons.Add(new AskCardButton { Line = lineNo, X = 2 + starts[b], Width = widths[b], Action = actions[b], RequestId = request.Id });
            }

            return AskCardRenderer.Box(title, right, body, pending ? theme.Warning : theme.Border, width);
        }

        /// <summary>
        /// The line under a tool chip the CLI refused for lack of permission, explained from the thread's resolution.
        /// </summary>
        /// <param name="resolution">The thread's CLI permission resolution, or null.</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="width">Width.</param>
        /// <returns>Lines (indented under the chip).</returns>
        public static List<StyledText> DeniedLines(CliPermissionResolution? resolution, ArmadaTheme theme, LocalizationService loc, int width)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            return AskCardRenderer.Para(CliPermissionText.DeniedExplanation(loc, resolution), theme.Warning, width, "     ");
        }

        #endregion

        #region Private-Methods

        private static string Marker(CliPermissionRequestStatusEnum status)
        {
            switch (status)
            {
                case CliPermissionRequestStatusEnum.Pending: return "!";
                case CliPermissionRequestStatusEnum.Allowed: return "+";
                case CliPermissionRequestStatusEnum.Denied: return "x";
                default: return "-";
            }
        }

        private static CellStyle StatusStyle(CliPermissionRequestStatusEnum status, ArmadaTheme theme)
        {
            switch (status)
            {
                case CliPermissionRequestStatusEnum.Pending: return theme.Warning;
                case CliPermissionRequestStatusEnum.Allowed: return theme.Success;
                case CliPermissionRequestStatusEnum.Denied: return theme.Error;
                default: return theme.Muted;
            }
        }

        #endregion
    }
}
