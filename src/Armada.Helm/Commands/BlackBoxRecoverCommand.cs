namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox recover &lt;entryId&gt; &lt;pathId&gt; [--execute] [--yes] [--acknowledge-data-loss]</c>: a dry
    /// run by default (prints the plan; nothing runs). With <c>--execute</c> it dry runs, asks to confirm (or takes
    /// <c>--yes</c>; refused when not interactive without it), refuses a path marked dataLossRisk without
    /// <c>--acknowledge-data-loss</c>, then runs the path with the dry run's confirm token. Notes are written as Operator.
    /// </summary>
    [Description("Run a black box recovery path (dry run unless --execute)")]
    public class BlackBoxRecoverCommand : BlackBoxCommandBase<BlackBoxRecoverSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxRecoverSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxRecoveryRunRequest dry = new BlackBoxRecoveryRunRequest { DryRun = true, AuthorKind = BlackBoxNoteAuthorKindEnum.Operator };
                BlackBoxRecoveryRunResult? plan = await client.RunBlackBoxRecoveryAsync(settings.Id, settings.PathId, dry, cancellationToken).ConfigureAwait(false);
                if (plan == null)
                {
                    Status("Error: the server returned no plan.");
                    return 1;
                }

                bool risk = plan.Plan.Any(s => s.DataLossRisk);
                if (!settings.Execute)
                {
                    if (settings.Json) WritePureJson(plan);
                    else
                    {
                        RenderPlan(plan, risk);
                        AnsiConsole.MarkupLine("[dim]Dry run: nothing ran. Run it with:[/] armada blackbox recover " + Markup.Escape(settings.Id) + " " + Markup.Escape(settings.PathId) + " --execute" + (risk ? " --acknowledge-data-loss" : ""));
                    }

                    return 0;
                }

                if (!settings.Json) RenderPlan(plan, risk);
                if (String.IsNullOrEmpty(plan.ConfirmToken))
                {
                    Status("Refused: the dry run issued no confirm token, so this path cannot run. " + (plan.Message ?? ""));
                    return BlackBoxCli.RefusedExitCode;
                }

                if (risk && !settings.AcknowledgeDataLoss)
                {
                    Status("Refused: this recovery path can lose work (dataLossRisk). Re-run with --acknowledge-data-loss to run it.");
                    return BlackBoxCli.RefusedExitCode;
                }

                if (!Confirmed("Run recovery path " + settings.PathId + " now?", settings.Yes))
                {
                    Status("Refused: not confirmed. Pass --yes to run without the question (required when not interactive).");
                    return BlackBoxCli.RefusedExitCode;
                }

                BlackBoxRecoveryRunRequest run = new BlackBoxRecoveryRunRequest
                {
                    DryRun = false,
                    ConfirmToken = plan.ConfirmToken,
                    AcknowledgeDataLoss = settings.AcknowledgeDataLoss,
                    AuthorKind = BlackBoxNoteAuthorKindEnum.Operator
                };
                BlackBoxRecoveryRunResult? result = await client.RunBlackBoxRecoveryAsync(settings.Id, settings.PathId, run, cancellationToken).ConfigureAwait(false);
                if (result == null)
                {
                    Status("Error: the server returned no result.");
                    return 1;
                }

                if (settings.Json) WritePureJson(result);
                else
                {
                    foreach (BlackBoxRecoveryStepResult s in result.StepResults.OrderBy(s => s.Order))
                        AnsiConsole.MarkupLine("  " + s.Order + ". " + (s.Succeeded ? "[green]ok[/]" : "[red]failed[/]") + (String.IsNullOrEmpty(s.Tool) ? "" : " [dim]" + Markup.Escape(s.Tool!) + "[/]") + (String.IsNullOrEmpty(s.Message) ? "" : "  " + Markup.Escape(s.Message!)));
                    AnsiConsole.MarkupLine(result.Succeeded ? "[green]Recovery path ran.[/]" : "[red]Recovery path failed.[/] " + Markup.Escape(result.Message ?? ""));
                }

                return result.Succeeded ? 0 : 1;
            }).ConfigureAwait(false);
        }

        private static void RenderPlan(BlackBoxRecoveryRunResult plan, bool risk)
        {
            AnsiConsole.MarkupLine("[bold]Plan for " + Markup.Escape(plan.PathId) + "[/]" + (risk ? " [red](data loss risk)[/]" : ""));
            List<BlackBoxRecoveryStep> steps = plan.Plan.OrderBy(s => s.Order).ToList();
            if (steps.Count == 0) AnsiConsole.MarkupLine("  [dim](no steps)[/]");
            foreach (BlackBoxRecoveryStep s in steps)
            {
                string tool = s.Action != null && !String.IsNullOrEmpty(s.Action.Tool) ? " [dim][[" + Markup.Escape(s.Action.Tool) + "]][/]" : "";
                AnsiConsole.MarkupLine("  " + s.Order + ". " + Markup.Escape(s.Description) + tool + (s.DataLossRisk ? " [red](data loss)[/]" : ""));
            }

            if (!String.IsNullOrEmpty(plan.Message)) AnsiConsole.MarkupLine("[dim]" + Markup.Escape(plan.Message!) + "[/]");
        }
    }
}
