namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal static class BacklogCommandSupport
    {
        public static async Task<Objective?> ResolveBacklogItemAsync(
            ArmadaApiClient client,
            string idOrTitle,
            CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(idOrTitle))
                return null;

            string search = idOrTitle.Trim();
            if (search.StartsWith(Constants.ObjectiveIdPrefix, StringComparison.OrdinalIgnoreCase))
                return await client.GetBacklogItemAsync(search, token).ConfigureAwait(false);

            EnumerationResult<Objective>? result = await client.ListBacklogAsync(new ObjectiveQuery
            {
                Search = search,
                PageNumber = 1,
                PageSize = 5000
            }, token).ConfigureAwait(false);
            List<Objective> matches = result?.Objects ?? new List<Objective>();
            if (matches.Count < 1)
                return null;

            Objective? exact = matches.FirstOrDefault(item =>
                String.Equals(item.Id, search, StringComparison.OrdinalIgnoreCase)
                || String.Equals(item.Title, search, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            Objective? contains = matches.FirstOrDefault(item =>
                item.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
            return contains ?? matches[0];
        }

        public static TEnum? ParseEnumOrNull<TEnum>(string? value, string optionName) where TEnum : struct, Enum
        {
            if (String.IsNullOrWhiteSpace(value))
                return null;
            if (Enum.TryParse<TEnum>(value.Trim(), true, out TEnum parsed))
                return parsed;
            throw new InvalidOperationException("Invalid value for " + optionName + ": " + value);
        }

        public static DateTime? ParseUtcOrNull(string? value, string optionName)
        {
            if (String.IsNullOrWhiteSpace(value))
                return null;
            if (DateTime.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException("Invalid UTC timestamp for " + optionName + ": " + value);
        }

        public static List<string>? NormalizeList(string[]? values)
        {
            if (values == null || values.Length < 1)
                return null;

            List<string> normalized = values
                .Where(item => !String.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return normalized.Count > 0 ? normalized : null;
        }

        public static ObjectiveUpsertRequest BuildUpsertRequest(BacklogMutationSettingsBase settings)
        {
            return new ObjectiveUpsertRequest
            {
                Title = String.IsNullOrWhiteSpace(settings.Title) ? null : settings.Title.Trim(),
                Description = String.IsNullOrWhiteSpace(settings.Description) ? null : settings.Description.Trim(),
                Status = ParseEnumOrNull<ObjectiveStatusEnum>(settings.Status, "--status"),
                Kind = ParseEnumOrNull<ObjectiveKindEnum>(settings.Kind, "--kind"),
                Category = String.IsNullOrWhiteSpace(settings.Category) ? null : settings.Category.Trim(),
                Priority = ParseEnumOrNull<ObjectivePriorityEnum>(settings.Priority, "--priority"),
                Rank = settings.Rank,
                BacklogState = ParseEnumOrNull<ObjectiveBacklogStateEnum>(settings.BacklogState, "--backlog-state"),
                Effort = ParseEnumOrNull<ObjectiveEffortEnum>(settings.Effort, "--effort"),
                Owner = String.IsNullOrWhiteSpace(settings.Owner) ? null : settings.Owner.Trim(),
                TargetVersion = String.IsNullOrWhiteSpace(settings.TargetVersion) ? null : settings.TargetVersion.Trim(),
                DueUtc = ParseUtcOrNull(settings.DueUtc, "--due-utc"),
                ParentObjectiveId = String.IsNullOrWhiteSpace(settings.ParentObjectiveId) ? null : settings.ParentObjectiveId.Trim(),
                BlockedByObjectiveIds = NormalizeList(settings.BlockedByObjectiveIds),
                RefinementSummary = String.IsNullOrWhiteSpace(settings.RefinementSummary) ? null : settings.RefinementSummary.Trim(),
                SuggestedPipelineId = String.IsNullOrWhiteSpace(settings.SuggestedPipelineId) ? null : settings.SuggestedPipelineId.Trim(),
                Tags = NormalizeList(settings.Tags),
                AcceptanceCriteria = NormalizeList(settings.AcceptanceCriteria),
                NonGoals = NormalizeList(settings.NonGoals),
                RolloutConstraints = NormalizeList(settings.RolloutConstraints),
                EvidenceLinks = NormalizeList(settings.EvidenceLinks),
                FleetIds = NormalizeList(settings.FleetIds),
                VesselIds = NormalizeList(settings.VesselIds)
            };
        }

        public static void RenderBacklogDetails(Objective objective)
        {
            Panel panel = new Panel(
                new Rows(
                    new Markup("[dodgerblue1]Id:[/]          [dim]" + Markup.Escape(objective.Id) + "[/]"),
                    new Markup("[dodgerblue1]Title:[/]       [bold]" + Markup.Escape(objective.Title) + "[/]"),
                    new Markup("[dodgerblue1]Status:[/]      [" + StatusColor(objective.Status) + "]" + objective.Status + "[/]"),
                    new Markup("[dodgerblue1]Backlog:[/]     [" + BacklogStateColor(objective.BacklogState) + "]" + objective.BacklogState + "[/]"),
                    new Markup("[dodgerblue1]Priority:[/]    [" + PriorityColor(objective.Priority) + "]" + objective.Priority + "[/]"),
                    new Markup("[dodgerblue1]Kind:[/]        " + objective.Kind),
                    new Markup("[dodgerblue1]Effort:[/]      " + objective.Effort),
                    new Markup("[dodgerblue1]Rank:[/]        " + objective.Rank),
                    new Markup("[dodgerblue1]Owner:[/]       " + Markup.Escape(objective.Owner ?? "-")),
                    new Markup("[dodgerblue1]Target:[/]      " + Markup.Escape(objective.TargetVersion ?? "-")),
                    new Markup("[dodgerblue1]Due UTC:[/]     " + (objective.DueUtc.HasValue ? objective.DueUtc.Value.ToString("yyyy-MM-dd HH:mm") : "-")),
                    new Markup("[dodgerblue1]Vessels:[/]     " + Markup.Escape(String.Join(", ", objective.VesselIds))),
                    new Markup("[dodgerblue1]Fleets:[/]      " + Markup.Escape(String.Join(", ", objective.FleetIds))),
                    new Markup("[dodgerblue1]Tags:[/]        " + Markup.Escape(String.Join(", ", objective.Tags))),
                    new Markup("[dodgerblue1]Created:[/]     " + objective.CreatedUtc.ToString("yyyy-MM-dd HH:mm")),
                    new Markup("[dodgerblue1]Updated:[/]     " + objective.LastUpdateUtc.ToString("yyyy-MM-dd HH:mm")),
                    new Markup("[dodgerblue1]Description:[/] " + Markup.Escape(objective.Description ?? "-")),
                    new Markup("[dodgerblue1]Summary:[/]     " + Markup.Escape(objective.RefinementSummary ?? "-"))));
            panel.Header = new PanelHeader("[bold dodgerblue1]Backlog Item[/]");
            panel.Border = BoxBorder.Rounded;
            panel.BorderStyle = new Style(Color.DodgerBlue1);
            AnsiConsole.Write(panel);
        }

        public static string StatusColor(ObjectiveStatusEnum value)
        {
            return value switch
            {
                ObjectiveStatusEnum.Completed => "green",
                ObjectiveStatusEnum.Released => "green",
                ObjectiveStatusEnum.Deployed => "green",
                ObjectiveStatusEnum.InProgress => "gold1",
                ObjectiveStatusEnum.Planned => "deepskyblue1",
                ObjectiveStatusEnum.Blocked => "red",
                ObjectiveStatusEnum.Cancelled => "grey",
                _ => "dim"
            };
        }

        public static string BacklogStateColor(ObjectiveBacklogStateEnum value)
        {
            return value switch
            {
                ObjectiveBacklogStateEnum.Refining => "gold1",
                ObjectiveBacklogStateEnum.ReadyForPlanning => "deepskyblue1",
                ObjectiveBacklogStateEnum.ReadyForDispatch => "green",
                ObjectiveBacklogStateEnum.Dispatched => "green",
                ObjectiveBacklogStateEnum.Triaged => "grey",
                _ => "dim"
            };
        }

        public static string PriorityColor(ObjectivePriorityEnum value)
        {
            return value switch
            {
                ObjectivePriorityEnum.P0 => "red",
                ObjectivePriorityEnum.P1 => "darkorange",
                ObjectivePriorityEnum.P2 => "deepskyblue1",
                _ => "grey"
            };
        }
    }

    /// <summary>
    /// Shared mutation settings used by backlog create and update commands.
    /// </summary>
    public abstract class BacklogMutationSettingsBase : BaseSettings
    {
        /// <summary>
        /// Gets or sets the backlog item title.
        /// </summary>
        [Description("Backlog item title")]
        [CommandOption("--title|-t <TITLE>")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the backlog item description.
        /// </summary>
        [Description("Backlog item description")]
        [CommandOption("--description|-d <TEXT>")]
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the objective status.
        /// </summary>
        [Description("Status: Draft, Scoped, Planned, InProgress, Released, Deployed, Completed, Blocked, Cancelled")]
        [CommandOption("--status <STATUS>")]
        public string? Status { get; set; }

        /// <summary>
        /// Gets or sets the objective kind.
        /// </summary>
        [Description("Kind: Feature, Bug, Refactor, Research, Chore, Initiative")]
        [CommandOption("--kind <KIND>")]
        public string? Kind { get; set; }

        /// <summary>
        /// Gets or sets the backlog category.
        /// </summary>
        [Description("Free-form category")]
        [CommandOption("--category <CATEGORY>")]
        public string? Category { get; set; }

        /// <summary>
        /// Gets or sets the objective priority.
        /// </summary>
        [Description("Priority: P0, P1, P2, P3")]
        [CommandOption("--priority <PRIORITY>")]
        public string? Priority { get; set; }

        /// <summary>
        /// Gets or sets the backlog rank.
        /// </summary>
        [Description("Rank in the backlog (lower sorts first)")]
        [CommandOption("--rank <RANK>")]
        public int? Rank { get; set; }

        /// <summary>
        /// Gets or sets the backlog state.
        /// </summary>
        [Description("Backlog state: Inbox, Triaged, Refining, ReadyForPlanning, ReadyForDispatch, Dispatched")]
        [CommandOption("--backlog-state <STATE>")]
        public string? BacklogState { get; set; }

        /// <summary>
        /// Gets or sets the effort estimate.
        /// </summary>
        [Description("Effort estimate: XS, S, M, L, XL")]
        [CommandOption("--effort <EFFORT>")]
        public string? Effort { get; set; }

        /// <summary>
        /// Gets or sets the owner.
        /// </summary>
        [Description("Owner (person or team)")]
        [CommandOption("--owner <OWNER>")]
        public string? Owner { get; set; }

        /// <summary>
        /// Gets or sets the target version.
        /// </summary>
        [Description("Target release version")]
        [CommandOption("--target-version <VERSION>")]
        public string? TargetVersion { get; set; }

        /// <summary>
        /// Gets or sets the due UTC timestamp.
        /// </summary>
        [Description("Due date, UTC (yyyy-MM-dd or ISO 8601)")]
        [CommandOption("--due-utc <DATE>")]
        public string? DueUtc { get; set; }

        /// <summary>
        /// Gets or sets the parent objective identifier.
        /// </summary>
        [Description("Parent backlog item ID (obj_)")]
        [CommandOption("--parent <ITEM>")]
        public string? ParentObjectiveId { get; set; }

        /// <summary>
        /// Gets or sets the blocking objective identifiers.
        /// </summary>
        [Description("Backlog item ID (obj_) that blocks this one (repeatable)")]
        [CommandOption("--blocked-by <ITEM>")]
        public string[]? BlockedByObjectiveIds { get; set; }

        /// <summary>
        /// Gets or sets the refinement summary.
        /// </summary>
        [Description("Refinement summary")]
        [CommandOption("--summary <TEXT>")]
        public string? RefinementSummary { get; set; }

        /// <summary>
        /// Gets or sets the suggested pipeline identifier.
        /// </summary>
        [Description("Suggested pipeline ID")]
        [CommandOption("--pipeline <PIPELINE>")]
        public string? SuggestedPipelineId { get; set; }

        /// <summary>
        /// Gets or sets the tags.
        /// </summary>
        [Description("Tag (repeatable)")]
        [CommandOption("--tag <TAG>")]
        public string[]? Tags { get; set; }

        /// <summary>
        /// Gets or sets the acceptance criteria.
        /// </summary>
        [Description("Acceptance criterion (repeatable)")]
        [CommandOption("--acceptance <TEXT>")]
        public string[]? AcceptanceCriteria { get; set; }

        /// <summary>
        /// Gets or sets the non-goals.
        /// </summary>
        [Description("Non-goal (repeatable)")]
        [CommandOption("--non-goal <TEXT>")]
        public string[]? NonGoals { get; set; }

        /// <summary>
        /// Gets or sets the rollout constraints.
        /// </summary>
        [Description("Rollout constraint (repeatable)")]
        [CommandOption("--constraint <TEXT>")]
        public string[]? RolloutConstraints { get; set; }

        /// <summary>
        /// Gets or sets the evidence links.
        /// </summary>
        [Description("Evidence link (repeatable)")]
        [CommandOption("--evidence <TEXT>")]
        public string[]? EvidenceLinks { get; set; }

        /// <summary>
        /// Gets or sets the fleet identifiers.
        /// </summary>
        [Description("Fleet ID (repeatable)")]
        [CommandOption("--fleet <FLEET>")]
        public string[]? FleetIds { get; set; }

        /// <summary>
        /// Gets or sets the vessel identifiers.
        /// </summary>
        [Description("Vessel ID (repeatable)")]
        [CommandOption("--vessel <VESSEL>")]
        public string[]? VesselIds { get; set; }
    }
}
