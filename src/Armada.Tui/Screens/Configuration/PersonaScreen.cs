namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Persona detail (dashboard <c>PersonaDetail.tsx</c>, route <c>/personas/:name</c>): actions View JSON, Edit
    /// (description, prompt template, default captain), Duplicate, Open Backing Prompt, and Delete (not for built-in
    /// personas); panels Overview (ID, name, description, prompt template link, default captain, built-in, active,
    /// created, last updated) and Backing Prompt, an inline editor for the backing prompt template's description and
    /// content (<c>Ctrl+E</c> opens <c>$EDITOR</c>) with Save Prompt, Reset to Default for built-in templates, and Open
    /// Full Template.
    /// </summary>
    public class PersonaScreen : EntityDetailScreen<Persona>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Persona"; }
        }

        /// <summary>
        /// Overview panel.
        /// </summary>
        public LinkDetailView Overview { get; } = new LinkDetailView();

        /// <summary>
        /// Backing prompt summary.
        /// </summary>
        public LinkDetailView PromptSummary { get; } = new LinkDetailView();

        /// <summary>
        /// Backing prompt editor form.
        /// </summary>
        public EntityForm? PromptForm { get; private set; } = null;

        /// <summary>
        /// Backing prompt description field.
        /// </summary>
        public InputField? PromptDescription { get; private set; } = null;

        /// <summary>
        /// Backing prompt content field.
        /// </summary>
        public TextAreaField? PromptContent { get; private set; } = null;

        /// <summary>
        /// The backing prompt template, or null.
        /// </summary>
        public PromptTemplate? Template { get; private set; } = null;

        #endregion

        #region Private-Members

        private List<PromptTemplate> _Templates = new List<PromptTemplate>();
        private List<Captain> _Captains = new List<Captain>();
        private PromptTemplate? _LoadedTemplate = null;
        private string? _TemplateError = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PersonaScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<Persona?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetPersonaAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(Persona entity, CancellationToken token)
        {
            Task<List<PromptTemplate>> templates = EntityLookups.PromptTemplatesAsync(Context.Client, token);
            Task<List<Captain>> captains = EntityLookups.CaptainsAsync(Context.Client, token);
            await Task.WhenAll(templates, captains).ConfigureAwait(false);
            _Templates = templates.Result;
            _Captains = captains.Result;
            _LoadedTemplate = null;
            _TemplateError = null;
            if (!String.IsNullOrEmpty(entity.PromptTemplateName))
            {
                try
                {
                    _LoadedTemplate = await Context.Client.GetPromptTemplateAsync(entity.PromptTemplateName, token).ConfigureAwait(false);
                }
                catch (ArmadaApiException ex)
                {
                    _TemplateError = ex.Message;
                }
            }
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddJsonAction();
            AddAction("edit", "Edit", () => { if (Entity != null) PersonaForms.OpenDetailForm(Context, Entity, _Templates, _Captains, Reload); }, () => Entity != null && CanEditPersona(), "e");
            AddAction("duplicate", "Duplicate", () => { if (Entity != null) PersonaForms.Duplicate(Context, Entity); }, () => Entity != null);
            AddAction("open-prompt", "Open Backing Prompt", OpenTemplate, () => !String.IsNullOrEmpty(Entity?.PromptTemplateName));
            AddAction("save-prompt", "Save Prompt", SavePrompt, () => Template != null && PromptForm != null && PromptForm.View.IsDirty);
            AddAction("reset-prompt", "Reset to Default", ResetPrompt, () => Template != null && Template.IsBuiltIn);
            AddAction("open-template", "Open Full Template", OpenTemplate, () => Template != null);
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && !Entity.IsBuiltIn && CanEditPersona(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("overview", "Overview", Overview);
            EntityForm form = new EntityForm(Context);
            PromptDescription = form.Text("Template Description", "", Context.Loc.T("Optional prompt template description..."));
            PromptContent = form.Area("Prompt Content", "", 14, false, "This is the prompt template Armada resolves when this persona is assigned to a mission. Edit it here to change future mission instructions.");
            form.View.SaveButton.Label = "Save Prompt";
            form.View.DiscardButton.Visible = false;
            form.View.SaveRequested += (s, e) => SavePrompt();
            PromptForm = form;
            StackPanel prompt = new StackPanel();
            prompt.Add(PromptSummary, 4);
            prompt.Add(form.View, null);
            AddPanel("prompt", "Backing Prompt", prompt);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Persona entity)
        {
            return entity.Name;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Persona entity)
        {
            List<string> statuses = new List<string> { entity.Active ? "Active" : "Inactive" };
            if (entity.IsBuiltIn) statuses.Add("Built-in");
            return statuses;
        }

        /// <inheritdoc />
        protected override void Populate(Persona p)
        {
            Overview.Reset();
            Overview.Row("ID", p.Id, t => t.Code);
            Overview.Row("Name", p.Name);
            Overview.Row("Description", EntityUi.Dash(p.Description));
            Overview.Link("Prompt Template Name", EntityUi.Dash(p.PromptTemplateName), String.IsNullOrEmpty(p.PromptTemplateName) ? (Action?)null : OpenTemplate);
            Captain? captain = _Captains.FirstOrDefault(c => c.Id == p.DefaultCaptainId);
            string captainText = String.IsNullOrEmpty(p.DefaultCaptainId) ? T("None (default routing)") : captain != null ? captain.Name + " (" + captain.Id + ")" : p.DefaultCaptainId!;
            Overview.Link("Default Captain", captainText, String.IsNullOrEmpty(p.DefaultCaptainId) ? (Action?)null : () => Context.Navigate("/captains/" + p.DefaultCaptainId));
            Overview.Row("Built-in", p.IsBuiltIn ? T("Built-in") : T("No"));
            Overview.Row("Active", EntityUi.Badge(Context, p.Active ? "Active" : "Inactive"));
            Overview.Row("Visibility", T(ScopeRules.Label(p.Scope)));
            Overview.Row("Created", EntityUi.Date(Context, p.CreatedUtc));
            Overview.Row("Last Updated", EntityUi.Date(Context, p.LastUpdateUtc));

            if (PromptForm == null || !PromptForm.View.IsDirty || Template == null || _LoadedTemplate == null || Template.Name != _LoadedTemplate.Name) ApplyTemplate(_LoadedTemplate);
        }

        #endregion

        #region Private-Methods

        private bool CanEditPersona()
        {
            Persona? p = Entity;
            return p != null && ScopeRules.CanEdit(Context.Session, p.Scope, p.TenantId, p.UserId);
        }

        private void ApplyTemplate(PromptTemplate? template)
        {
            Template = template;
            PromptSummary.Reset();
            if (template == null)
            {
                PromptSummary.Row("Backing Prompt", _TemplateError ?? T("No prompt template is linked to this persona."));
                if (PromptForm != null) PromptForm.View.Visible = false;
                return;
            }

            PromptSummary.Link("Template Name", template.Name, OpenTemplate);
            PromptSummary.Row("Template Type", template.IsBuiltIn ? T("Built-in") : T("Custom"));
            PromptSummary.Row("Category", template.Category);
            if (PromptForm != null && PromptDescription != null && PromptContent != null)
            {
                PromptForm.View.Visible = true;
                PromptDescription.Value = template.Description ?? "";
                PromptContent.Value = template.Content ?? "";
                PromptForm.MarkClean();
            }
        }

        private void OpenTemplate()
        {
            string? name = Template?.Name ?? Entity?.PromptTemplateName;
            if (!String.IsNullOrEmpty(name)) Context.Navigate("/prompt-templates/" + Uri.EscapeDataString(name!));
        }

        private void SavePrompt()
        {
            PromptTemplate? template = Template;
            if (template == null || PromptContent == null || PromptDescription == null) return;
            PromptTemplateUpdateRequest body = new PromptTemplateUpdateRequest();
            body.Content = PromptContent.Value;
            body.Description = EntityUi.Blank(PromptDescription.Value);
            EntityUi.Run<PromptTemplate?>(Context, ct => Context.Client.UpdatePromptTemplateAsync(template.Name, body, ct), result =>
            {
                if (result != null)
                {
                    _LoadedTemplate = result;
                    ApplyTemplate(result);
                }

                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Prompt template \"{{name}}\" saved.", "name", result?.Name ?? template.Name));
            }, "Prompt save failed.");
        }

        private void ResetPrompt()
        {
            PromptTemplate? template = Template;
            if (template == null || !template.IsBuiltIn) return;
            Context.Confirm("Reset Backing Prompt",
                EntityUi.T(Context, "Reset prompt template \"{{name}}\" to its built-in default content? Your customizations will be lost.", "name", template.Name), () =>
                {
                    EntityUi.Run<PromptTemplate?>(Context, ct => Context.Client.ResetPromptTemplateAsync(template.Name, ct), result =>
                    {
                        if (result != null)
                        {
                            _LoadedTemplate = result;
                            ApplyTemplate(result);
                        }

                        EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Prompt template \"{{name}}\" reset to default.", "name", result?.Name ?? template.Name));
                    }, "Prompt reset failed.");
                }, "Reset to Default");
        }

        private void RequestDelete()
        {
            Persona? p = Entity;
            if (p == null) return;
            if (p.IsBuiltIn)
            {
                EntityUi.Toast(Context, NotificationSeverityEnum.Error, T("Built-in personas cannot be deleted."));
                return;
            }

            Context.Confirm("Delete Persona", EntityUi.T(Context, "Delete persona \"{{name}}\"? This cannot be undone.", "name", p.Name), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeletePersonaAsync(p.Name, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Persona \"{{name}}\" deleted.", "name", p.Name));
                    Context.Navigate("/configuration?tab=personas");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
