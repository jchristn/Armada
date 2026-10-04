namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Approvals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Renders the Ask transcript's boxed cards as styled lines: the confirm card (the dashboard's
    /// <c>AskConfirmCard</c>: label, tool, source, status, summary, exact arguments, expiry, decision keys, and outcome)
    /// and the live work card (<c>AskWorkCard</c>: type, title, status, progress bar, counts by status, per-mission and
    /// per-target rows, error, updated time). ASCII borders throughout. Thread-safe (stateless).
    /// </summary>
    public static class AskCardRenderer
    {
        #region Public-Methods

        /// <summary>
        /// Lines of a confirm card.
        /// </summary>
        /// <param name="proposal">Proposal.</param>
        /// <param name="compact">Compact (on an ActionResult): "Action" label only.</param>
        /// <param name="expanded">Arguments and result expanded.</param>
        /// <param name="busy">An approve or reject call is in flight.</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="width">Width in cells.</param>
        /// <returns>Lines.</returns>
        public static List<StyledText> ConfirmCard(AskActionProposal proposal, bool compact, bool expanded, bool busy, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int width)
        {
            bool pending = proposal.Status == AskProposalStatusEnum.Pending;
            int inner = Math.Max(10, width - 4);
            CellStyle statusStyle = ProposalStyle(proposal.Status, theme);
            StyledText title = StyledText.From(compact ? loc.T("Action") : pending ? loc.T("Approval needed") : loc.T("Action"), pending ? theme.Warning.WithAttribute(CellAttributes.Bold, true) : theme.Accent)
                .Append(StyledText.From("  " + proposal.ToolName + "  ", theme.Code))
                .Append(StyledText.From(proposal.Source == AskProposalSourceEnum.QuickAction ? loc.T("Quick action") : loc.T("Proposed by the captain"), theme.Muted));
            StyledText right = StyledText.From(ProposalMarker(proposal.Status) + " " + loc.T(proposal.Status.ToString()), statusStyle);
            List<StyledText> body = new List<StyledText>();
            if (!String.IsNullOrWhiteSpace(proposal.SummaryText)) body.AddRange(Para(proposal.SummaryText, theme.Text, inner));
            string args = ApprovalActions.Pretty(proposal.ArgumentsText);
            if (args.Length > 0)
            {
                body.Add(StyledText.From((expanded ? "v " : "> ") + loc.T("Exact arguments") + "  (x " + (expanded ? loc.T("hide") : loc.T("show")) + ", y " + loc.T("copy") + ")", theme.Muted));
                if (expanded) body.AddRange(Para(args, theme.Code, inner, "  "));
            }

            if (pending)
            {
                string note = proposal.ExpiresUtc != null
                    ? loc.T("Nothing runs until you approve. Expires {{time}}.", LocalizationArgs.Of("time", ExpiryText(proposal.ExpiresUtc.Value, loc, nowUtc)))
                    : loc.T("Nothing runs until you approve.");
                body.AddRange(Para(note, theme.Muted, inner));
                if (busy) body.Add(StyledText.From(loc.T("Working..."), theme.Info));
                else body.Add(StyledText.From("[a] " + loc.T("Approve"), theme.Success.WithAttribute(CellAttributes.Bold, true))
                    .Append(StyledText.From("   [r] " + loc.T("Reject"), theme.Error))
                    .Append(StyledText.From("   [x] " + loc.T("Arguments"), theme.Muted)));
            }
            else if (proposal.Status == AskProposalStatusEnum.Approved)
            {
                body.Add(StyledText.From(loc.T("Approved. Running now..."), theme.Info));
            }
            else if (proposal.Status == AskProposalStatusEnum.Rejected)
            {
                body.Add(StyledText.From(loc.T("Rejected. Nothing was run."), theme.Muted));
            }
            else if (proposal.Status == AskProposalStatusEnum.Expired)
            {
                body.Add(StyledText.From(loc.T("Expired without running."), theme.Muted));
            }
            else if (proposal.Status == AskProposalStatusEnum.Executed)
            {
                string ran = proposal.ExecutedUtc != null
                    ? loc.T("Ran {{time}}.", LocalizationArgs.Of("time", loc.FormatRelative(proposal.ExecutedUtc.Value, nowUtc)))
                    : loc.T("Ran successfully.");
                body.Add(StyledText.From(ran, theme.Success));
                string result = ApprovalActions.Pretty(proposal.ResultText);
                if (result.Length > 0)
                {
                    body.Add(StyledText.From((expanded ? "v " : "> ") + loc.T("Result") + "  (x " + (expanded ? loc.T("hide") : loc.T("show")) + ")", theme.Muted));
                    if (expanded) body.AddRange(Para(result, theme.Code, inner, "  "));
                }
            }
            else if (proposal.Status == AskProposalStatusEnum.Failed)
            {
                body.AddRange(Para(!String.IsNullOrEmpty(proposal.ErrorText) ? proposal.ErrorText! : loc.T("The action failed."), theme.Error, inner));
            }

            return Box(title, right, body, pending ? theme.Warning : theme.Border, width);
        }

        /// <summary>
        /// Lines of a live work card; fills the block's row bookkeeping (row starts, heights, mission ids, PR URLs,
        /// routes) so the view can select rows.
        /// </summary>
        /// <param name="block">Block receiving row bookkeeping.</param>
        /// <param name="work">Tracked work, or null.</param>
        /// <param name="snapshot">Snapshot, or null.</param>
        /// <param name="highlighted">Emphasize the card.</param>
        /// <param name="lineOffset">Lines that precede the card inside the block.</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="width">Width.</param>
        /// <returns>Lines.</returns>
        public static List<StyledText> WorkCard(AskBlock block, AskTrackedWork? work, AskWorkSnapshot? snapshot, bool highlighted, int lineOffset, ArmadaTheme theme, LocalizationService loc, DateTime nowUtc, int width)
        {
            int inner = Math.Max(10, width - 4);
            AskTrackedEntityTypeEnum type = snapshot?.EntityType ?? work?.EntityType ?? AskTrackedEntityTypeEnum.Voyage;
            string entityId = snapshot?.EntityId ?? work?.EntityId ?? "";
            string titleText = !String.IsNullOrEmpty(work?.Title) ? work!.Title : !String.IsNullOrEmpty(snapshot?.Title) ? snapshot!.Title : entityId;
            string? status = snapshot?.Status ?? work?.Status;
            bool active = snapshot != null ? AskWorkLogic.IsActive(snapshot) : AskWorkLogic.IsActive(work);
            StyledText title = StyledText.From(loc.T(AskWorkLogic.EntityLabel(type)) + "  ", theme.Muted)
                .Append(StyledText.From(titleText, theme.Accent.WithAttribute(CellAttributes.Bold, true)));
            StyledText right = String.IsNullOrEmpty(status)
                ? StyledText.From(loc.T("Waiting for status..."), theme.Muted)
                : StyledText.From((active ? "* " : "") + StatusBadge.Label(loc.T(status!)), StatusBadge.Style(status, theme));
            List<StyledText> body = new List<StyledText>();
            AskWorkProgress? progress = AskWorkLogic.Progress(snapshot);
            if (progress != null)
            {
                int barWidth = Math.Min(24, Math.Max(8, inner / 3));
                int done = progress.Total > 0 ? (int)Math.Round((progress.Done - progress.Failed) * (double)barWidth / progress.Total) : 0;
                int failed = progress.Total > 0 ? (int)Math.Round(progress.Failed * (double)barWidth / progress.Total) : 0;
                done = Math.Clamp(done, 0, barWidth);
                failed = Math.Clamp(failed, 0, barWidth - done);
                StyledText bar = StyledText.From("[", theme.Muted)
                    .Append(StyledText.From(new string('=', done), theme.Success))
                    .Append(StyledText.From(new string('x', failed), theme.Error))
                    .Append(StyledText.From(new string('-', barWidth - done - failed), theme.Muted))
                    .Append(StyledText.From("] ", theme.Muted))
                    .Append(StyledText.From(loc.T("{{done}} of {{total}} finished", LocalizationArgs.Of("done", progress.Done, "total", progress.Total)), theme.Text));
                if (progress.Failed > 0) bar = bar.Append(StyledText.From(", " + loc.T("{{count}} failed", LocalizationArgs.Of("count", progress.Failed)), theme.Error));
                body.AddRange(bar.Width <= inner ? new List<StyledText> { bar } : TextWrapper.Wrap(bar, inner));
            }

            List<AskStatusCount> counts = AskWorkLogic.StatusCounts(snapshot);
            if (counts.Count > 1)
            {
                StyledText line = StyledText.Empty;
                foreach (AskStatusCount c in counts) line = line.Append(StyledText.From(loc.T(c.Status) + " ", theme.Muted)).Append(StyledText.From(c.Count.ToString(CultureInfo.InvariantCulture) + "   ", theme.Text));
                body.AddRange(TextWrapper.Wrap(line, inner));
            }

            List<AskWorkMissionSnapshot> missions = snapshot?.Missions ?? new List<AskWorkMissionSnapshot>();
            List<AskWorkTargetSnapshot> targets = snapshot?.Targets ?? new List<AskWorkTargetSnapshot>();
            int headerLines = 1;
            if (missions.Count > 0)
            {
                foreach (AskWorkMissionSnapshot m in missions)
                {
                    List<StyledText> rows = MissionRow(m, theme, loc, inner);
                    block.RowLines.Add(lineOffset + headerLines + body.Count);
                    block.RowHeights.Add(rows.Count);
                    block.RowMissionIds.Add(m.Id);
                    block.RowPrUrls.Add(String.IsNullOrEmpty(m.PrUrl) ? null : m.PrUrl);
                    block.RowRoutes.Add("/missions/" + Uri.EscapeDataString(m.Id));
                    body.AddRange(rows);
                }
            }
            else if (targets.Count > 0)
            {
                foreach (AskWorkTargetSnapshot t in targets)
                {
                    List<StyledText> rows = TargetRow(t, theme, loc, inner);
                    block.RowLines.Add(lineOffset + headerLines + body.Count);
                    block.RowHeights.Add(rows.Count);
                    block.RowMissionIds.Add(String.IsNullOrEmpty(t.MissionId) ? null : t.MissionId);
                    block.RowPrUrls.Add(null);
                    block.RowRoutes.Add(!String.IsNullOrEmpty(t.MissionId) ? "/missions/" + Uri.EscapeDataString(t.MissionId!)
                        : !String.IsNullOrEmpty(t.VoyageId) ? "/voyages/" + Uri.EscapeDataString(t.VoyageId!)
                        : !String.IsNullOrEmpty(t.VesselId) ? "/vessels/" + Uri.EscapeDataString(t.VesselId) : null);
                    body.AddRange(rows);
                }
            }

            if (!String.IsNullOrEmpty(snapshot?.ErrorText)) body.AddRange(Para(snapshot!.ErrorText!, theme.Error, inner));
            if (snapshot == null) body.Add(StyledText.From(loc.T("Loading live status..."), theme.Muted));
            DateTime? updated = snapshot?.CapturedUtc ?? work?.LastChangeUtc;
            StyledText footer = StyledText.Empty;
            if (updated != null) footer = footer.Append(StyledText.From(loc.T("Updated {{time}}", LocalizationArgs.Of("time", loc.FormatRelative(updated.Value, nowUtc))) + "   ", theme.Muted));
            footer = footer.Append(StyledText.From("Enter " + loc.T("Open details"), theme.Muted));
            if (missions.Count > 0) footer = footer.Append(StyledText.From("  o " + loc.T("PR") + "  l " + loc.T("Log") + "  d " + loc.T("Diff"), theme.Muted));
            body.Add(footer);
            CellStyle border = highlighted ? theme.Accent.WithAttribute(CellAttributes.Bold, true) : active ? theme.Info : theme.Border;
            return Box(title, right, body, border, width);
        }

        /// <summary>
        /// Draw a titled ASCII box around body lines.
        /// </summary>
        /// <param name="title">Title (left of the top edge).</param>
        /// <param name="right">Label at the right of the top edge.</param>
        /// <param name="body">Body lines (already wrapped to width - 4).</param>
        /// <param name="border">Border style.</param>
        /// <param name="width">Width.</param>
        /// <returns>Lines.</returns>
        public static List<StyledText> Box(StyledText title, StyledText right, List<StyledText> body, CellStyle border, int width)
        {
            int w = Math.Max(14, width);
            int inner = w - 4;
            List<StyledText> lines = new List<StyledText>();
            int rightWidth = right.Width;
            int titleBudget = Math.Max(1, w - 6 - rightWidth - 2);
            StyledText fittedTitle = Fit(title, titleBudget);
            int dashes = Math.Max(1, w - 4 - fittedTitle.Width - rightWidth - 3);
            StyledText top = StyledText.From("+- ", border).Append(fittedTitle).Append(StyledText.From(" " + new string('-', dashes) + " ", border));
            if (top.Width + rightWidth + 2 <= w) top = top.Append(right);
            top = top.Append(StyledText.From(new string('-', Math.Max(0, w - top.Width - 1)) + "+", border));
            lines.Add(top);
            foreach (StyledText raw in body)
            {
                foreach (StyledText row in raw.Width <= inner ? new List<StyledText> { raw } : TextWrapper.Wrap(raw, inner))
                {
                    int pad = Math.Max(0, inner - row.Width);
                    lines.Add(StyledText.From("| ", border).Append(row).Append(StyledText.From(new string(' ', pad) + " |", border)));
                }
            }

            lines.Add(StyledText.From("+" + new string('-', w - 2) + "+", border));
            return lines;
        }

        /// <summary>
        /// Wrap plain text (newlines kept) into styled lines.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="style">Style.</param>
        /// <param name="width">Width.</param>
        /// <param name="indent">Prefix for every line.</param>
        /// <returns>Lines.</returns>
        public static List<StyledText> Para(string text, CellStyle style, int width, string indent = "")
        {
            List<StyledText> lines = new List<StyledText>();
            foreach (string source in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = source.TrimStart(' ');
                string lead = indent + new string(' ', Math.Min(source.Length - trimmed.Length, Math.Max(0, width / 2)));
                int w = Math.Max(1, width - TextCells.Width(lead));
                foreach (string line in TextCells.Wrap(trimmed, w)) lines.Add(StyledText.From(lead + line, style));
            }

            return lines;
        }

        /// <summary>
        /// Expiry time text: local time today, or the local date and time.
        /// </summary>
        /// <param name="expiresUtc">Expiry.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Text.</returns>
        public static string ExpiryText(DateTime expiresUtc, LocalizationService loc, DateTime nowUtc)
        {
            DateTime local = DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc).ToLocalTime();
            DateTime today = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).ToLocalTime().Date;
            string clock = local.ToString("t", loc.Culture);
            TimeSpan left = expiresUtc - nowUtc;
            string prefix = left.TotalSeconds > 0 && left.TotalMinutes < 120 ? loc.T("in {{minutes}} min", LocalizationArgs.Of("minutes", Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)))) + ", " : "";
            return prefix + (local.Date == today ? clock : local.ToString("g", loc.Culture));
        }

        #endregion

        #region Private-Methods

        private static CellStyle ProposalStyle(AskProposalStatusEnum status, ArmadaTheme theme)
        {
            switch (status)
            {
                case AskProposalStatusEnum.Executed: return theme.Success;
                case AskProposalStatusEnum.Failed: return theme.Error;
                case AskProposalStatusEnum.Approved: return theme.Info;
                case AskProposalStatusEnum.Pending: return theme.Warning;
                default: return theme.Muted;
            }
        }

        private static string ProposalMarker(AskProposalStatusEnum status)
        {
            switch (status)
            {
                case AskProposalStatusEnum.Executed: return "+";
                case AskProposalStatusEnum.Failed: return "x";
                case AskProposalStatusEnum.Approved: return "~";
                case AskProposalStatusEnum.Pending: return "!";
                default: return "-";
            }
        }

        private static List<StyledText> MissionRow(AskWorkMissionSnapshot m, ArmadaTheme theme, LocalizationService loc, int width)
        {
            List<StyledText> rows = new List<StyledText>();
            bool failed = AskWorkLogic.IsFailedChild(m.Status);
            string status = TextCells.PadRight(StatusBadge.Label(loc.T(m.Status)), 16);
            StyledText first = StyledText.From(status + " ", StatusBadge.Style(m.Status, theme))
                .Append(StyledText.From(String.IsNullOrEmpty(m.Title) ? m.Id : m.Title, failed ? theme.Error : theme.Text));
            if (!String.IsNullOrEmpty(m.CaptainName) || !String.IsNullOrEmpty(m.CaptainId))
                first = first.Append(StyledText.From("   " + (m.CaptainName ?? m.CaptainId), theme.Muted));
            rows.Add(Fit(first, width));
            List<string> facts = new List<string>();
            string? stage = !String.IsNullOrEmpty(m.PipelineStage) ? m.PipelineStage : m.Persona;
            if (!String.IsNullOrEmpty(stage)) facts.Add(loc.T("Stage") + ": " + stage);
            if (!String.IsNullOrEmpty(m.CheckRunStatus)) facts.Add(loc.T("Checks") + ": " + loc.T(m.CheckRunStatus!));
            string? merge = m.MergeQueueStatus ?? m.MergeStatus;
            if (!String.IsNullOrEmpty(merge)) facts.Add(loc.T("Merge") + ": " + loc.T(merge!));
            if (!String.IsNullOrEmpty(m.LandingOutcome)) facts.Add(loc.T("Landing") + ": " + loc.T(m.LandingOutcome!));
            if (!String.IsNullOrEmpty(m.BranchName)) facts.Add(loc.T("Branch") + ": " + m.BranchName);
            if (!String.IsNullOrEmpty(m.PrUrl)) facts.Add(loc.T("PR") + ": " + m.PrUrl);
            if (facts.Count > 0) rows.AddRange(Para(String.Join("   ", facts), theme.Muted, width, "    "));
            if (!String.IsNullOrEmpty(m.FailureReason)) rows.AddRange(Para(m.FailureReason!, theme.Error, width, "    "));
            return rows;
        }

        private static List<StyledText> TargetRow(AskWorkTargetSnapshot t, ArmadaTheme theme, LocalizationService loc, int width)
        {
            List<StyledText> rows = new List<StyledText>();
            bool failed = AskWorkLogic.IsFailedChild(t.Status);
            string status = TextCells.PadRight(StatusBadge.Label(loc.T(t.Status)), 16);
            rows.Add(Fit(StyledText.From(status + " ", StatusBadge.Style(t.Status, theme))
                .Append(StyledText.From(!String.IsNullOrEmpty(t.VesselName) ? t.VesselName : !String.IsNullOrEmpty(t.VesselId) ? t.VesselId : t.Id, failed ? theme.Error : theme.Text)), width));
            List<string> facts = new List<string>();
            if (!String.IsNullOrEmpty(t.VoyageId)) facts.Add(loc.T("Voyage") + ": " + t.VoyageId);
            if (!String.IsNullOrEmpty(t.MissionId)) facts.Add(loc.T("Mission") + ": " + t.MissionId);
            if (facts.Count > 0) rows.AddRange(Para(String.Join("   ", facts), theme.Muted, width, "    "));
            if (!String.IsNullOrEmpty(t.Reason)) rows.AddRange(Para(t.Reason!, theme.Error, width, "    "));
            return rows;
        }

        private static StyledText Fit(StyledText text, int width)
        {
            if (text.Width <= width) return text;
            List<StyledSpan> spans = new List<StyledSpan>();
            int used = 0;
            int budget = Math.Max(1, width - 3);
            foreach (StyledSpan span in text.Spans)
            {
                int w = TextCells.Width(span.Text);
                if (used + w <= budget)
                {
                    spans.Add(span);
                    used += w;
                    continue;
                }

                string clipped = TextCells.Clip(span.Text, budget - used);
                if (clipped.Length > 0) spans.Add(new StyledSpan(clipped, span.Style));
                spans.Add(new StyledSpan("...", span.Style));
                break;
            }

            return new StyledText(spans);
        }

        #endregion
    }
}
