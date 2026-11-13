namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox retention</c>: the retention policy, entry and byte counts, and the last and next GC run.
    /// </summary>
    [Description("Show black box retention: policy, usage, and garbage collection runs")]
    public class BlackBoxRetentionCommand : BlackBoxCommandBase<BaseSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxRetentionStatus? r = await client.GetBlackBoxRetentionAsync(cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(r);
                    return 0;
                }

                if (r == null) return 1;
                Table table = new Table().Border(TableBorder.Rounded).BorderColor(Color.DodgerBlue1).AddColumn("Setting").AddColumn("Value");
                table.Title = new TableTitle("Black box retention");
                table.AddRow("Retention days", r.Policy.RetentionDays.ToString());
                table.AddRow("Keep latest per mission", r.Policy.KeepLatestPerMission.ToString());
                table.AddRow("Max entries per vessel", r.Policy.MaxEntriesPerVessel.ToString());
                table.AddRow("Max total bytes", Markup.Escape(BlackBoxCli.Bytes(r.Policy.MaxTotalBytes)));
                table.AddRow("Delete with mission / vessel", r.Policy.DeleteWithMission + " / " + r.Policy.DeleteWithVessel);
                table.AddRow("GC interval (minutes)", r.Policy.GcIntervalMinutes.ToString());
                table.AddRow("Entries", r.TotalEntries + " (" + r.PinnedEntries + " pinned, " + r.ExpiredEntries + " expired, " + r.DeletingEntries + " deleting)");
                table.AddRow("Bytes", Markup.Escape(BlackBoxCli.Bytes(r.TotalBytes)));
                table.AddRow("Last GC", r.LastRun == null ? "never" : r.LastRun.StartedUtc.ToString("yyyy-MM-dd HH:mm") + " UTC, removed " + (r.LastRun.Expired + r.LastRun.Evicted + r.LastRun.Cascaded));
                table.AddRow("Next GC", r.NextRunUtc.HasValue ? r.NextRunUtc.Value.ToString("yyyy-MM-dd HH:mm") + " UTC" : "not scheduled");
                AnsiConsole.Write(table);
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
