namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Show a mission's attempt history: recovery counters, escalation state and every attempt.
    /// </summary>
    [Description("Show a mission's attempts, repairs and escalations")]
    public class MissionAttemptsCommand : BaseCommand<MissionAttemptsSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionAttemptsSettings settings, CancellationToken cancellationToken)
        {
            MissionAttemptHistory? history = await GetAsync<MissionAttemptHistory>("/api/v1/missions/" + Uri.EscapeDataString(settings.MissionId) + "/attempts").ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(history);
                return 0;
            }

            if (history == null)
            {
                AnsiConsole.MarkupLine("[red]Mission not found.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(history.MissionId)}[/] {Markup.Escape(history.Status.ToString())}, persona {Markup.Escape(history.Persona ?? "-")}, mode {Markup.Escape(history.Mode.ToString())}, tier {Markup.Escape(history.Tier?.ToString() ?? "-")}");
            AnsiConsole.MarkupLine($"Escalations {history.EscalationCount}" + (String.IsNullOrEmpty(history.EscalatedFromModel) ? "" : " (from " + Markup.Escape(history.EscalatedFromModel!) + ")")
                + $", redispatches {history.RedispatchAttempts}, output repairs {history.OutputRepairAttempts}, no-op redispatches {history.NoOpRedispatchAttempts}, stall recoveries {history.StallRecoveryAttempts}");
            if (history.Attempts.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No attempts recorded yet.[/]");
                return 0;
            }

            Table table = TableRenderer.CreateTable("Attempts", null);
            table.AddColumn("#");
            table.AddColumn("Captain");
            table.AddColumn("Model");
            table.AddColumn("Disposition");
            table.AddColumn("Failure");
            table.AddColumn("Runtime exit");
            table.AddColumn("Repair");
            table.AddColumn("Duration");
            foreach (MissionAttempt attempt in history.Attempts)
            {
                string repair = attempt.ContractRepairKind?.ToString() ?? (attempt.IsRepair ? "OutputRepair" : "");
                string duration = attempt.DurationMs.HasValue ? TimeSpan.FromMilliseconds(attempt.DurationMs.Value).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "";
                table.AddRow(
                    attempt.Attempt.ToString(CultureInfo.InvariantCulture) + (attempt.ReworkRound > 0 ? " r" + attempt.ReworkRound.ToString(CultureInfo.InvariantCulture) : ""),
                    Markup.Escape(attempt.CaptainName ?? attempt.CaptainId ?? ""),
                    Markup.Escape(attempt.Model ?? ""),
                    Markup.Escape(attempt.Disposition.ToString()) + (attempt.Escalated && !String.IsNullOrEmpty(attempt.NextCaptainId) ? " to " + Markup.Escape(attempt.NextCaptainId!) : ""),
                    Markup.Escape(attempt.FailureKind?.ToString() ?? ""),
                    Markup.Escape(attempt.RuntimeFailureKind?.ToString() ?? ""),
                    Markup.Escape(repair),
                    duration);
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
