namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Skill detail (dashboard <c>SkillDetail.tsx</c>, route <c>/skills/:id</c>, and <c>/skills/new</c> for create):
    /// Overview (ID, created, last updated, status, name, category, description, visibility, and the read-only notice
    /// for users who cannot edit it) and Content (the injected text as Markdown) panels; actions Edit (the form with
    /// inline and <c>$EDITOR</c> content editing), View JSON, and Delete with confirmation.
    /// </summary>
    public class SkillScreen : EntityDetailScreen<Skill>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Skill"; }
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
        /// Content panel.
        /// </summary>
        public MarkdownView Content { get; } = new MarkdownView();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public SkillScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Skill"; }
        }

        /// <inheritdoc />
        protected override Task<Skill?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetSkillAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddAction("create", "Create Skill", () => SkillForms.Open(Context, null, Created), () => IsCreateMode);
            AddAction("edit", "Edit", () => { if (Entity != null && CanManage()) SkillForms.Open(Context, Entity, s => Reload()); }, () => Entity != null && CanManage(), "e");
            AddJsonAction();
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && CanManage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("overview", "Overview", Overview);
            AddPanel("content", "Content", Content);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Skill entity)
        {
            return entity.Name;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Skill entity)
        {
            return new[] { entity.Active ? "Active" : "Inactive" };
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            SkillForms.Open(Context, null, Created);
        }

        /// <inheritdoc />
        protected override void Populate(Skill s)
        {
            Overview.Reset();
            if (!CanManage()) Overview.Row("Note", T("You can view this skill, but only tenant administrators can change it."), t => t.Warning);
            Overview.Section("Skill");
            Overview.Row("ID", s.Id, t => t.Code);
            Overview.Row("Created", EntityUi.Date(Context, s.CreatedUtc));
            Overview.Row("Last Updated", EntityUi.Date(Context, s.LastUpdateUtc));
            Overview.Row("Status", EntityUi.Badge(Context, s.Active ? "Active" : "Inactive"));
            Overview.Row("Name", s.Name);
            Overview.Row("Category", EntityUi.Dash(s.Category));
            Overview.Row("Description", EntityUi.Dash(s.Description));
            Overview.Row("Visibility", T(ScopeRules.Label(s.Scope)));
            Overview.Row("Content", Localizer.FormatNumber((s.Content ?? "").Length) + " " + T("chars"));
            Content.Markdown = String.IsNullOrEmpty(s.Content) ? T("Markdown or plain text injected into mission prompts for projects that attach this skill.") : s.Content;
        }

        #endregion

        #region Private-Methods

        private bool CanManage()
        {
            Skill? s = Entity;
            return s != null && ScopeRules.CanEdit(Context.Session, s.Scope, s.TenantId, s.UserId);
        }

        private void Created(Skill created)
        {
            Context.Navigate("/skills/" + created.Id);
        }

        private void RequestDelete()
        {
            Skill? s = Entity;
            if (s == null || !CanManage()) return;
            Context.Confirm("Delete Skill", T("Delete this skill? This cannot be undone."), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteSkillAsync(s.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, T("Skill deleted."));
                    Context.Navigate("/configuration?tab=skills");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
