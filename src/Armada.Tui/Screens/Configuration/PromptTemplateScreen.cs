namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Prompt template detail and create (dashboard <c>PromptTemplateDetail.tsx</c>, routes
    /// <c>/prompt-templates/:name</c> and <c>/prompt-templates/create</c>): the Template panel edits the description
    /// and content (name and category too when creating) with a character count, an "Unsaved changes" marker,
    /// <c>Ctrl+E</c> for <c>$EDITOR</c>, and <c>Ctrl+S</c> to save; the Parameters panel and the Insert Parameter action
    /// (<c>Ctrl+P</c>) insert a placeholder at the cursor, grouped Mission, Vessel, Captain, Pipeline Context, and
    /// System. Actions: Save, Insert Parameter, Duplicate, View JSON, Reset to Default (built-ins, with confirmation),
    /// and Back. Read-only for users who cannot edit the template.
    /// </summary>
    public class PromptTemplateScreen : EntityDetailScreen<PromptTemplate>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Prompt Template"; }
        }

        /// <inheritdoc />
        public override bool IsCreateMode
        {
            get { return String.IsNullOrEmpty(EntityId); }
        }

        /// <summary>
        /// Template information.
        /// </summary>
        public LinkDetailView Info { get; } = new LinkDetailView();

        /// <summary>
        /// Parameter palette panel.
        /// </summary>
        public LinkDetailView Parameters { get; } = new LinkDetailView();

        /// <summary>
        /// The editor form.
        /// </summary>
        public EntityForm? Form { get; private set; } = null;

        /// <summary>
        /// Name field (create mode only).
        /// </summary>
        public InputField? NameField { get; private set; } = null;

        /// <summary>
        /// Category field (create mode only).
        /// </summary>
        public InputField? CategoryField { get; private set; } = null;

        /// <summary>
        /// Description field.
        /// </summary>
        public InputField? DescriptionField { get; private set; } = null;

        /// <summary>
        /// Content field.
        /// </summary>
        public TextAreaField? ContentField { get; private set; } = null;

        #endregion

        #region Private-Members

        private bool _Saving = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PromptTemplateScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Insert a parameter placeholder at the content cursor and focus the content.
        /// </summary>
        /// <param name="parameter">Placeholder, for example <c>{MissionId}</c>.</param>
        public void InsertParameter(string parameter)
        {
            if (ContentField == null || ContentField.ReadOnly || Form == null) return;
            ShowPanel("template");
            ContentField.Insert(parameter);
            Form.View.Scope.Focus(ContentField);
        }

        /// <summary>
        /// Open the parameter picker.
        /// </summary>
        public void OpenParameterPicker()
        {
            if (ContentField == null || ContentField.ReadOnly) return;
            List<ActionMenuItem> items = PromptParameter.All
                .Select(p => new ActionMenuItem(p.Name + "  " + T(p.Description), () => InsertParameter(p.Name), T(p.Group)))
                .ToList();
            ActionMenu.Show(Context.Modals, "Parameters", items, Context.Loc, Context.Theme.Current);
        }

        /// <summary>
        /// Validate and save (create or update).
        /// </summary>
        public void Save()
        {
            if (Form == null || ContentField == null || DescriptionField == null || _Saving || !CanEditTemplate()) return;
            if (!Form.View.ValidateAll()) return;
            string description = DescriptionField.Value.Trim();
            _Saving = true;
            if (IsCreateMode)
            {
                PromptTemplateCreateRequest request = new PromptTemplateCreateRequest();
                request.Name = NameField!.Value.Trim();
                request.Category = CategoryField!.Value.Trim();
                request.Content = ContentField.Value;
                request.Description = description.Length > 0 ? description : null;
                request.Active = true;
                EntityUi.Run<PromptTemplate?>(Context, ct => Context.Client.CreatePromptTemplateAsync(request, ct), result =>
                {
                    _Saving = false;
                    Form.MarkClean();
                    string name = result?.Name ?? request.Name;
                    EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Template \"{{name}}\" created.", "name", name));
                    Context.Router.Navigate("/prompt-templates/" + Uri.EscapeDataString(name), true);
                }, "Create failed.", ex => _Saving = false);
                return;
            }

            PromptTemplate? template = Entity;
            if (template == null)
            {
                _Saving = false;
                return;
            }

            PromptTemplateUpdateRequest update = new PromptTemplateUpdateRequest();
            update.Content = ContentField.Value;
            update.Description = description.Length > 0 ? description : null;
            EntityUi.Run<PromptTemplate?>(Context, ct => Context.Client.UpdatePromptTemplateAsync(template.Name, update, ct), result =>
            {
                _Saving = false;
                if (result != null)
                {
                    Entity = result;
                    Apply(result, true);
                }

                EntityUi.Toast(Context, NotificationSeverityEnum.Success, T("Template saved."));
            }, "Save failed.", ex => _Saving = false);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Prompt Template"; }
        }

        /// <inheritdoc />
        protected override Task<PromptTemplate?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetPromptTemplateAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddAction("save", "Save", Save, () => (IsCreateMode || Entity != null) && CanEditTemplate() && Form != null && (IsCreateMode || Form.View.IsDirty), "ctrl+s");
            AddAction("insert-parameter", "Insert Parameter", OpenParameterPicker, () => (IsCreateMode || Entity != null) && CanEditTemplate(), "ctrl+p");
            AddAction("duplicate", "Duplicate", () => { if (Entity != null) PromptTemplateActions.Duplicate(Context, Entity); }, () => Entity != null);
            AddJsonAction();
            AddAction("reset", "Reset to Default", () =>
            {
                PromptTemplate? t = Entity;
                if (t == null || !t.IsBuiltIn) return;
                PromptTemplateActions.Reset(Context, t.Name, true, r =>
                {
                    if (r == null) return;
                    Entity = r;
                    Apply(r, true);
                });
            }, () => Entity != null && Entity.IsBuiltIn && CanEditTemplate());
            AddAction("back", "Back", () => Context.Navigate("/configuration?tab=prompts"), null);
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Info.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            EntityForm form = new EntityForm(Context);
            if (IsCreateMode)
            {
                NameField = form.Text("Name", "", Context.Loc.T("mission.rules.custom"), false, null, v => String.IsNullOrWhiteSpace(v) ? "Template name is required." : null);
                CategoryField = form.Text("Category", "mission", Context.Loc.T("mission"), false, String.Join(", ", PromptParameter.Categories), v => String.IsNullOrWhiteSpace(v) ? "Template category is required." : null);
            }

            DescriptionField = form.Text("Description", "", Context.Loc.T("Template description..."));
            ContentField = form.Area("Template Content", "", 18, false, "Ctrl+P inserts a parameter at the cursor.");
            if (IsCreateMode) ContentField.Validator = v => String.IsNullOrWhiteSpace(v) ? "Template content is required." : null;
            form.View.DiscardButton.Visible = false;
            form.View.SaveRequested += (s, e) => Save();
            form.MarkClean();
            Form = form;
            StackPanel template = new StackPanel();
            template.Add(Info, IsCreateMode ? 1 : 4);
            template.Add(form.View, null);
            AddPanel("template", "Template", template);

            Parameters.Reset();
            Parameters.Section("Click a parameter to insert it at the cursor position.");
            string? group = null;
            foreach (PromptParameter p in PromptParameter.All)
            {
                if (p.Group != group)
                {
                    group = p.Group;
                    Parameters.Section(p.Group);
                }

                PromptParameter param = p;
                Parameters.Link(param.Name, T(param.Description), () => InsertParameter(param.Name));
            }

            AddPanel("parameters", "Parameters", Parameters);
            if (IsCreateMode)
            {
                Info.Reset();
                Info.Row("Type", T("Custom template"));
            }
        }

        /// <inheritdoc />
        protected override string HeaderTitle(PromptTemplate entity)
        {
            return entity.Name;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(PromptTemplate entity)
        {
            List<string> statuses = new List<string> { entity.Category };
            if (entity.IsBuiltIn) statuses.Add("Built-in");
            if (Form != null && Form.View.IsDirty) statuses.Add("Unsaved changes");
            return statuses;
        }

        /// <inheritdoc />
        protected override void Populate(PromptTemplate entity)
        {
            Apply(entity, false);
        }

        #endregion

        #region Private-Methods

        private bool CanEditTemplate()
        {
            if (IsCreateMode) return true;
            PromptTemplate? t = Entity;
            return t == null || ScopeRules.CanEdit(Context.Session, t.Scope, t.TenantId, t.UserId);
        }

        private void Apply(PromptTemplate t, bool force)
        {
            Info.Reset();
            Info.Row("ID", t.Id, x => x.Code);
            Info.Row("Category", t.Category);
            Info.Row("Active", EntityUi.Badge(Context, t.Active ? "Active" : "Inactive"));
            Info.Row("Created", EntityUi.Date(Context, t.CreatedUtc));
            Info.Row("Last Updated", EntityUi.Date(Context, t.LastUpdateUtc));
            Info.Row("Visibility", T(ScopeRules.Label(t.Scope)));
            if (Form == null || ContentField == null || DescriptionField == null) return;
            bool editable = CanEditTemplate();
            ContentField.ReadOnly = !editable;
            Form.View.SaveButton.Visible = editable;
            if (Form.View.IsDirty && !force) return;
            DescriptionField.Value = t.Description ?? "";
            ContentField.Value = t.Content ?? "";
            Form.MarkClean();
        }

        #endregion
    }
}
