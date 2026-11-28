namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// List model profiles with their tier and served context.
    /// </summary>
    [Description("List model profiles")]
    public class ModelProfileListCommand : BaseCommand<BaseSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
        {
            int page = settings.Page ?? 1;
            int size = settings.PageSize ?? 100;
            EnumerationResult<ModelProfile>? result = await GetAsync<EnumerationResult<ModelProfile>>(
                "/api/v1/model-profiles?pageNumber=" + page.ToString(CultureInfo.InvariantCulture) + "&pageSize=" + size.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            if (result == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No model profiles found.[/]");
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Model Profiles", null);
            table.AddColumn("Id");
            table.AddColumn("Model");
            table.AddColumn("Runtime");
            table.AddColumn("Context");
            table.AddColumn("Tier");
            table.AddColumn("Suggested");
            foreach (ModelProfile profile in result.Objects)
            {
                table.AddRow(
                    $"[dim]{Markup.Escape(profile.Id)}[/]",
                    Markup.Escape(profile.Identity.Model ?? ""),
                    Markup.Escape(profile.Identity.Runtime),
                    profile.Identity.ServedContextTokens.HasValue ? profile.Identity.ServedContextTokens.Value.ToString("N0", CultureInfo.InvariantCulture) : "",
                    Markup.Escape(profile.Tiering.Tier?.ToString() ?? ""),
                    Markup.Escape(profile.Tiering.SuggestedTier?.ToString() ?? ""));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
