namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console.Rendering;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox show &lt;missionId|voyageId|entryId&gt;</c>: the black box entry of a mission, a voyage, or an
    /// entry id. <c>--brief</c> prints only the Markdown FIX THIS brief to stdout (<c>| pbcopy</c> works), <c>--json</c>
    /// only the entry with its record; otherwise a summary with the recovery paths. Exits
    /// <see cref="BlackBoxCli.NotFoundExitCode"/> with a message on stderr when there is no entry.
    /// </summary>
    [Description("Show the black box entry of a mission, voyage, or entry id")]
    public class BlackBoxShowCommand : BlackBoxCommandBase<BlackBoxShowSettings>
    {
        #region Protected-Members

        /// <summary>
        /// The subject the id must name (the <c>mission failure</c> and <c>voyage failure</c> aliases), or null to infer it
        /// from the id prefix.
        /// </summary>
        protected virtual BlackBoxSubjectTypeEnum? ForcedSubject
        {
            get { return null; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxShowSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string id = (settings.Id ?? "").Trim();
            if (id.Length == 0)
            {
                Status("Error: an id is required (bbx_, msn_, or vyg_).");
                return 1;
            }

            bool pure = settings.Brief || settings.Json;
            return await RunBlackBoxAsync(pure, async client =>
            {
                BlackBoxSubjectTypeEnum? subject = ForcedSubject ?? BlackBoxCli.SubjectOf(id);
                if (settings.Brief)
                {
                    string? brief = subject == BlackBoxSubjectTypeEnum.Mission ? await client.GetMissionBlackBoxBriefAsync(id, settings.RedactHosts, cancellationToken).ConfigureAwait(false)
                        : subject == BlackBoxSubjectTypeEnum.Voyage ? await client.GetVoyageBlackBoxBriefAsync(id, settings.RedactHosts, cancellationToken).ConfigureAwait(false)
                        : await client.GetBlackBoxBriefAsync(id, settings.RedactHosts, cancellationToken).ConfigureAwait(false);
                    Console.Out.Write(brief ?? "");
                    if (!String.IsNullOrEmpty(brief) && !brief.EndsWith("\n", StringComparison.Ordinal)) Console.Out.WriteLine();
                    return 0;
                }

                BlackBoxEntry? entry = subject == BlackBoxSubjectTypeEnum.Mission ? await client.GetMissionBlackBoxAsync(id, true, cancellationToken).ConfigureAwait(false)
                    : subject == BlackBoxSubjectTypeEnum.Voyage ? await client.GetVoyageBlackBoxAsync(id, true, cancellationToken).ConfigureAwait(false)
                    : await client.GetBlackBoxAsync(id, true, cancellationToken).ConfigureAwait(false);
                if (entry == null)
                {
                    Status("Error: no black box entry for " + id + ".");
                    return BlackBoxCli.NotFoundExitCode;
                }

                if (settings.Json)
                {
                    WritePureJson(entry);
                    return 0;
                }

                Render(entry);
                return 0;
            }).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Render(BlackBoxEntry e)
        {
            string subject = !String.IsNullOrEmpty(e.MissionId) ? e.MissionId! + (String.IsNullOrEmpty(e.MissionTitle) ? "" : "  " + e.MissionTitle) : (e.VoyageId ?? "-");
            List<IRenderable> rows = new List<IRenderable>
            {
                new Markup("[dodgerblue1]Entry:[/]       [dim]" + Markup.Escape(e.Id) + "[/]"),
                new Markup("[dodgerblue1]Subject:[/]     " + Markup.Escape(subject)),
                new Markup("[dodgerblue1]Vessel:[/]      " + Markup.Escape(e.VesselName ?? e.VesselId ?? "-")),
                new Markup("[dodgerblue1]Where:[/]       " + Markup.Escape(e.Phase + " / " + e.Step)),
                new Markup("[dodgerblue1]Kind:[/]        " + Markup.Escape(e.FailureKind?.ToString() ?? "-")),
                new Markup("[dodgerblue1]Status:[/]      " + Markup.Escape((e.StatusBefore ?? "-") + " -> " + (e.StatusAfter ?? "-"))),
                new Markup("[dodgerblue1]Triage:[/]      [bold]" + Markup.Escape(e.Triage.ToString()) + "[/]"),
                new Markup("[dodgerblue1]Resolution:[/]  " + Markup.Escape(e.Resolution.ToString()) + (e.Pinned ? "  [gold1](pinned)[/]" : "")),
                new Markup("[dodgerblue1]Attempt:[/]     " + e.Attempt + (e.IsCurrent ? "" : "  [dim](superseded)[/]")),
                new Markup("[dodgerblue1]Captured:[/]    " + e.CreatedUtc.ToString("yyyy-MM-dd HH:mm") + " UTC"),
            };
            if (!String.IsNullOrEmpty(e.ReasonExcerpt)) rows.Add(new Markup("[dodgerblue1]Reason:[/]      [red]" + Markup.Escape(e.ReasonExcerpt!) + "[/]"));
            if (!String.IsNullOrEmpty(e.Record?.Analysis?.Summary)) rows.Add(new Markup("[dodgerblue1]Summary:[/]     " + Markup.Escape(e.Record!.Analysis!.Summary!)));
            Panel panel = new Panel(new Rows(rows));
            panel.Header = new PanelHeader("[red]Black Box[/]");
            panel.Border = BoxBorder.Rounded;
            panel.BorderStyle = new Style(Color.DodgerBlue1);
            AnsiConsole.Write(panel);

            List<BlackBoxRecoveryPath> paths = e.Record?.Analysis?.RecoveryPaths?.OrderBy(p => p.Rank).ToList() ?? new List<BlackBoxRecoveryPath>();
            if (paths.Count > 0)
            {
                Table table = new Table().Border(TableBorder.Rounded).BorderColor(Color.DodgerBlue1);
                table.Title = new TableTitle("Recovery paths");
                table.AddColumn("Rank");
                table.AddColumn("Path");
                table.AddColumn("Title");
                table.AddColumn("Risk");
                foreach (BlackBoxRecoveryPath p in paths)
                    table.AddRow(p.Rank.ToString(), "[green]" + Markup.Escape(p.Id) + "[/]", Markup.Escape(p.Title), Markup.Escape(p.Risk.ToString()) + (p.DataLossRisk ? " [red](data loss)[/]" : ""));
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("[dim]Dry run a path:[/] armada blackbox recover " + Markup.Escape(e.Id) + " " + Markup.Escape(paths[0].Id));
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]No recovery paths are recorded for this failure.[/]");
            }

            AnsiConsole.MarkupLine("[dim]FIX THIS brief:[/] armada blackbox show " + Markup.Escape(e.Id) + " --brief | pbcopy");
        }

        #endregion
    }
}
