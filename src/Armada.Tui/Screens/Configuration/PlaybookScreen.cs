namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Playbook detail (dashboard <c>PlaybookDetail.tsx</c>, route <c>/playbooks/:id</c>, and <c>/playbooks/new</c>
    /// for create): Overview (ID, created, last updated, status, the statistics characters, lines, and headings, file
    /// name, description, visibility, and the read-only notice) and Preview (the Markdown rendered) panels; actions
    /// Edit (the form with inline and <c>$EDITOR</c> Markdown editing), View JSON, Duplicate, and Delete with
    /// confirmation.
    /// </summary>
    public class PlaybookScreen : EntityDetailScreen<Playbook>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Playbook"; }
        }

        /// <inheritdoc />
        public override bool IsCreateMode
        {
            get { return String.Equals(EntityId, "new", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Overview panel.
        /// </summary>
        public LinkDetailView Overview { get; } = new LinkDetailView();

        /// <summary>
        /// Markdown preview panel.
        /// </summary>
        public MarkdownView Preview { get; } = new MarkdownView();

        #endregion

        #region Private-Members

        private static readonly Regex _Heading = new Regex(@"^#+\s", RegexOptions.Multiline | RegexOptions.Compiled);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PlaybookScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The dashboard's playbook statistics: characters, lines, and Markdown headings.
        /// </summary>
        /// <param name="content">Content.</param>
        /// <returns>Three numbers: characters, lines, headings.</returns>
        public static int[] Statistics(string? content)
        {
            string text = content ?? "";
            int lines = text.Length == 0 ? 0 : Regex.Split(text, "\r?\n").Length;
            return new[] { text.Length, lines, _Heading.Matches(text).Count };
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Playbook"; }
        }

        /// <inheritdoc />
        protected override Task<Playbook?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetPlaybookAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddAction("create", "Create Playbook", () => PlaybookForms.Open(Context, null, Created), () => IsCreateMode);
            AddAction("edit", "Edit", () => { if (Entity != null && CanManage()) PlaybookForms.Open(Context, Entity, p => Reload()); }, () => Entity != null && CanManage(), "e");
            AddJsonAction();
            AddAction("duplicate", "Duplicate", () => { if (Entity != null) PlaybookForms.Duplicate(Context, Entity, false); }, () => Entity != null && CanManage());
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && CanManage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("overview", "Overview", Overview);
            AddPanel("preview", "Preview", Preview);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Playbook entity)
        {
            return entity.FileName;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Playbook entity)
        {
            return new[] { entity.Active ? "Active" : "Inactive" };
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            PlaybookForms.Open(Context, null, Created);
        }

        /// <inheritdoc />
        protected override void Populate(Playbook p)
        {
            int[] stats = Statistics(p.Content);
            Overview.Reset();
            if (!CanManage()) Overview.Row("Note", T("You can view this playbook, but only tenant administrators can change it."), t => t.Warning);
            Overview.Section("Playbook");
            Overview.Row("ID", p.Id, t => t.Code);
            Overview.Row("Created", EntityUi.Date(Context, p.CreatedUtc));
            Overview.Row("Last Updated", EntityUi.Date(Context, p.LastUpdateUtc));
            Overview.Row("Status", EntityUi.Badge(Context, p.Active ? "Active" : "Inactive"));
            Overview.Row("File Name", p.FileName);
            Overview.Row("Description", EntityUi.Dash(p.Description));
            Overview.Row("Visibility", T(ScopeRules.Label(p.Scope)));
            Overview.Section("Statistics");
            Overview.Row("Characters", Localizer.FormatNumber(stats[0]));
            Overview.Row("Lines", Localizer.FormatNumber(stats[1]));
            Overview.Row("Headings", Localizer.FormatNumber(stats[2]));
            Preview.Markdown = p.Content ?? "";
        }

        #endregion

        #region Private-Methods

        private bool CanManage()
        {
            Playbook? p = Entity;
            return p != null && ScopeRules.CanEdit(Context.Session, p.Scope, p.TenantId, p.UserId);
        }

        private void Created(Playbook created)
        {
            Context.Navigate("/playbooks/" + created.Id);
        }

        private void RequestDelete()
        {
            Playbook? p = Entity;
            if (p == null || !CanManage()) return;
            Context.Confirm("Delete Playbook", EntityUi.T(Context, "Delete \"{{name}}\"? Existing mission snapshots remain immutable, but future dispatches will no longer be able to select it.", "name", p.FileName), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeletePlaybookAsync(p.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Playbook \"{{name}}\" deleted.", "name", p.FileName));
                    Context.Navigate("/configuration?tab=playbooks");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
