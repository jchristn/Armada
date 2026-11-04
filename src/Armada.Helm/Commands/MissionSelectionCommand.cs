namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>Inspect scoped selection evidence through the same API used by web and native clients.</summary>
    [Description("Inspect why a captain was selected or preview a Pending mission")]
    public sealed class MissionSelectionCommand : BaseCommand<MissionSelectionSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionSelectionSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureServerAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            int page = settings.Page ?? 1, size = settings.PageSize ?? 25;
            if (settings.History)
            {
                EnumerationResult<CaptainSelectionDecisionView>? history = await GetApiClient().GetCaptainSelectionHistoryAsync(settings.MissionId,
                    new CaptainSelectionHistoryQuery { Outcome = settings.Outcome, PageNumber = page, PageSize = size }, cancellationToken).ConfigureAwait(false);
                if (IsJsonMode(settings)) { WriteJson(history); return history == null ? 1 : 0; }
                if (history == null) return 1;
                TableRenderer.RenderPaginationHeader(history.PageNumber, history.TotalPages, history.TotalRecords, history.TotalMs);
                Table table = TableRenderer.CreateTable("Selection history");
                table.AddColumn("Decision"); table.AddColumn("Time (UTC)"); table.AddColumn("Outcome"); table.AddColumn("Strategy"); table.AddColumn("Details");
                foreach (CaptainSelectionDecisionView row in history.Objects)
                    table.AddRow(Markup.Escape(row.Id), row.EvaluatedUtc.ToString("u", CultureInfo.InvariantCulture), Markup.Escape(row.Outcome),
                        Markup.Escape(UsageSelectionText.ForStrategy(row.Strategy)), Markup.Escape(UsageSelectionText.ForReason(row.ReasonCode)));
                AnsiConsole.Write(table);
                return 0;
            }
            if (settings.Preview)
            {
                CaptainSelectionPreview? preview = await GetApiClient().PreviewCaptainSelectionAsync(settings.MissionId, page, size, cancellationToken).ConfigureAwait(false);
                if (IsJsonMode(settings)) { WriteJson(preview); return preview == null ? 1 : 0; }
                if (preview == null) return 1;
                AnsiConsole.MarkupLine(Markup.Escape(UsageSelectionText.ForReason(preview.ReasonCode)));
                // A mission that cannot be previewed (for example not Pending) has no comparison; skip the empty table.
                if (preview.Candidates.TotalRecords > 0) RenderCandidates(preview.Candidates);
                return 0;
            }
            CaptainSelectionDecisionDetail? detail;
            if (settings.DecisionId != null)
            {
                detail = await GetApiClient().GetCaptainSelectionDecisionAsync(settings.DecisionId, settings.TenantId, page, size, cancellationToken).ConfigureAwait(false);
                if (detail != null && detail.Decision.MissionId != settings.MissionId) throw new InvalidOperationException("The selected decision does not belong to this mission.");
                if (IsJsonMode(settings)) { WriteJson(detail); return detail == null ? 1 : 0; }
            }
            else
            {
                CaptainSelectionSummary? summary = await GetApiClient().GetCaptainSelectionAsync(settings.MissionId, cancellationToken).ConfigureAwait(false);
                if (IsJsonMode(settings)) { WriteJson(summary); return summary == null ? 1 : 0; }
                if (summary == null) return 1;
                AnsiConsole.MarkupLine(Markup.Escape(summary.CurrentMode + " / " + UsageSelectionText.ForStrategy(summary.CurrentStrategy)));
                if (summary.Recovery != null) AnsiConsole.MarkupLine(Markup.Escape(DescribeRecovery(summary.Recovery)));
                CaptainSelectionDecisionView? current = summary.CurrentAttempt ?? summary.LatestDecision;
                if (current == null) { AnsiConsole.MarkupLine(Markup.Escape(UsageSelectionText.Get("usageSelection.noHistory"))); return 0; }
                if (summary.CurrentAttempt == null) AnsiConsole.MarkupLine("[dim]Latest retained decision; no selection was recorded for the current attempt.[/]");
                detail = await GetApiClient().GetCaptainSelectionDecisionAsync(current.Id, current.TenantId, page, size, cancellationToken).ConfigureAwait(false);
            }
            if (detail == null) return 1;
            CaptainSelectionDecisionView decision = detail.Decision;
            Table facts = TableRenderer.CreateTable("Captain selection");
            facts.AddColumn("Field"); facts.AddColumn("Value");
            facts.AddRow("Decision", Markup.Escape(decision.Id));
            facts.AddRow("Selected captain", Markup.Escape(decision.SelectedCaptainName ?? decision.SelectedCaptainId ?? "Not selected"));
            facts.AddRow("Intended model", Markup.Escape(decision.IntendedModel ?? "Unknown"));
            facts.AddRow("Runtime-reported model", Markup.Escape(decision.ReportedModel ?? "Unknown"));
            facts.AddRow("Mode / strategy", Markup.Escape(decision.Mode + " / " + UsageSelectionText.ForStrategy(decision.Strategy)));
            facts.AddRow("Selected at (UTC)", decision.EvaluatedUtc.ToString("u", CultureInfo.InvariantCulture));
            facts.AddRow("Details", Markup.Escape(decision.Mode == Armada.Core.Enums.UsageSelectionModeEnum.Observe && decision.Outcome == CaptainSelectionOutcomes.Admitted ? UsageSelectionText.Get("usageSelection.observeChoice") : UsageSelectionText.ForReason(decision.ReasonCode)));
            AnsiConsole.Write(facts);
            RenderCandidates(detail.Candidates);
            return 0;
        }

        /// <summary>One-line English summary of a mission's recovery permissions.</summary>
        /// <param name="recovery">Recovery record, or null.</param>
        /// <returns>Summary text.</returns>
        public static string DescribeRecovery(UsageSelectionRecovery? recovery)
        {
            if (recovery == null) return "Recovery permissions: none recorded (revision 0).";
            return "Recovery permissions revision " + recovery.Revision.ToString(CultureInfo.InvariantCulture) + ": "
                + UsageSelectionText.Get(recovery.AllowFallback ? "usageSelection.recovery.fallbackOn" : "usageSelection.recovery.fallbackOff") + ", "
                + UsageSelectionText.Get(recovery.ReserveException ? "usageSelection.recovery.reserveOn" : "usageSelection.recovery.reserveOff")
                + ". Reason: " + recovery.Reason;
        }

        private static void RenderCandidates(EnumerationResult<CaptainSelectionCandidateView> page)
        {
            TableRenderer.RenderPaginationHeader(page.PageNumber, page.TotalPages, page.TotalRecords, page.TotalMs);
            Table table = TableRenderer.CreateTable("Captain comparison");
            table.AddColumn(new TableColumn("Captain / model").Width(26)); table.AddColumn(new TableColumn("State").Width(12)); table.AddColumn("Details");
            foreach (CaptainSelectionCandidateView row in page.Objects)
            {
                string state = row.ActualSelection ? "Selected" : row.HypotheticalSelection ? "Proposed" : row.Band.ToString();
                string available = row.AvailablePercent.HasValue ? row.AvailablePercent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "Unknown";
                table.AddRow(Markup.Escape((row.CaptainName ?? row.CaptainId) + "\n" + (row.Model ?? "Unknown")), Markup.Escape(state),
                    Markup.Escape(UsageSelectionText.ForReason(row.ReasonCode) + "\nEffective headroom: " + available));
            }
            AnsiConsole.Write(table);
        }
    }
}
