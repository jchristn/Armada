namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Model Profiles (the Model Profiles tab of <c>/captains</c>), the dashboard's Model Profiles tab: the models as
    /// served (runtime, provider, model, served context) with their tier and outcome-data tier suggestion. Server paging
    /// with a model filter; a row opens the profile. Not thread-safe.
    /// </summary>
    public class ModelProfilesScreen : OpsListScreen<ModelProfile>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "The models as served, shared by every captain that runs them: tier, recovery policy, timeouts and capabilities."; }
        }

        /// <summary>
        /// Model name filter (server side).
        /// </summary>
        public TextInput ModelFilter { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public ModelProfilesScreen(RouteMatch route, TuiContext context)
            : base(route, context, p => p.Id, "ModelProfilesScreen", "Model Profiles")
        {
            Grid.EmptyText = "No model profiles yet. A profile is recorded the first time a captain runs a model.";
            ModelFilter = TextFilter("Model", 18, "Filter...", true);

            Column("model", "Model", p => p.Identity.Model ?? "-", 3);
            Column("runtime", "Runtime", p => p.Identity.Runtime, 2);
            Column("provider", "Provider", p => p.Identity.Provider ?? "-", 2);
            Column("context", "Served Context", p => p.Identity.ServedContextTokens.HasValue ? p.Identity.ServedContextTokens.Value.ToString("N0", CultureInfo.InvariantCulture) : "-", 0, 15);
            Column("tier", "Tier", p => p.Tiering.Tier?.ToString() ?? "-", 0, 10);
            Column("suggested", "Suggested", p => p.Tiering.SuggestedTier?.ToString() ?? "", 0, 10);
            Column("lastUpdateUtc", "Updated", p => Context.Loc.FormatRelative(p.LastUpdateUtc, Context.Clock.UtcNow), 0, 14, p => p.LastUpdateUtc);

            RowActions.Add(new OpsAction<ModelProfile>("view", "View Detail", p => OpenRow(p), "o"));
            RowActions.Add(new OpsAction<ModelProfile>("json", "View JSON", p => ShowJson(Tr("Model Profile") + ": " + p.Id, p), "j"));
            RowActions.Add(new OpsAction<ModelProfile>("copy-id", "Copy ID", p => Copy(p.Id, "Model Profile ID"), "y"));

            Start("lastUpdateUtc", true);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<ModelProfile>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ModelProfileQuery q = new ModelProfileQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            string model = ModelFilter.Value.Trim();
            if (model.Length > 0) q.ModelContains = model;
            EnumerationResult<ModelProfile>? result = await Context.Client.ModelProfiles.ListAsync(q, token).ConfigureAwait(false);
            List<ModelProfile> rows = result?.Objects ?? new List<ModelProfile>();
            return new GridPage<ModelProfile>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override void OpenRow(ModelProfile row)
        {
            Context.Navigate("/model-profiles/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(ModelProfile row)
        {
            return Tr("Model Profile") + ": " + (row.Identity.Model ?? row.Id);
        }

        #endregion
    }
}
