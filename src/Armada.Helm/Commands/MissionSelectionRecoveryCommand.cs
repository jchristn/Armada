namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>
    /// Set the audited fallback and soft-reserve permissions for a Pending mission. Each change is an immutable revision;
    /// running work is not affected. Requires a tenant or system administrator.
    /// </summary>
    [Description("Set audited captain-selection recovery permissions for a Pending mission")]
    public sealed class MissionSelectionRecoveryCommand : BaseCommand<MissionSelectionRecoverySettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionSelectionRecoverySettings settings, CancellationToken cancellationToken)
        {
            await EnsureServerAsync().ConfigureAwait(false);
            int expected;
            if (settings.ExpectedRevision.HasValue)
            {
                expected = settings.ExpectedRevision.Value;
            }
            else
            {
                CaptainSelectionSummary? summary = await GetApiClient().GetCaptainSelectionAsync(settings.MissionId, cancellationToken).ConfigureAwait(false);
                if (summary == null) return 1;
                if (!summary.CanManageRecovery)
                {
                    AnsiConsole.MarkupLine("[red]Recovery permissions require a tenant or system administrator and a Pending mission.[/]");
                    return 1;
                }

                expected = summary.Recovery?.Revision ?? 0;
                if (!IsJsonMode(settings)) AnsiConsole.MarkupLine(Markup.Escape(MissionSelectionCommand.DescribeRecovery(summary.Recovery)));
            }

            string change = "fallback " + (settings.AllowFallback ? "allowed" : "not allowed") + ", reserve " + (settings.ReserveException ? "exception allowed" : "protected");
            if (!settings.Yes && !IsJsonMode(settings)
                && !AnsiConsole.Confirm("Set recovery permissions for [bold]" + Markup.Escape(settings.MissionId) + "[/] to " + Markup.Escape(change) + "?"))
                return 1;

            UsageSelectionRecoveryRequest request = new UsageSelectionRecoveryRequest
            {
                ExpectedRevision = expected,
                AllowFallback = settings.AllowFallback,
                ReserveException = settings.ReserveException,
                Reason = settings.Reason!.Trim()
            };
            UsageSelectionRecovery? saved = await GetApiClient().AuthorizeCaptainSelectionRecoveryAsync(settings.MissionId, request, cancellationToken).ConfigureAwait(false);
            if (IsJsonMode(settings)) { WriteJson(saved); return saved == null ? 1 : 0; }
            if (saved == null) return 1;
            AnsiConsole.MarkupLine("[green]Saved recovery revision " + saved.Revision.ToString(CultureInfo.InvariantCulture) + ".[/] "
                + Markup.Escape(MissionSelectionCommand.DescribeRecovery(saved)));
            return 0;
        }
    }
}
