namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Pipeline forms shared by the Pipelines tab and the pipeline detail screen (dashboard <c>Pipelines.tsx</c> and
    /// <c>PipelineDetail.tsx</c>): the create/edit modal with its stage list (persona, optional, review gate, on deny,
    /// description; add, edit, remove, and reorder with <c>Alt+Up</c>/<c>Alt+Down</c>), Duplicate, and the Run Pipeline
    /// modal that launches a voyage. Use on the UI loop thread.
    /// </summary>
    public static class PipelineForms
    {
        #region Public-Methods

        /// <summary>
        /// Load personas and open the create (null) or edit form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Pipeline to edit, or null to create.</param>
        /// <param name="detailMode">Detail edit (description and stages only), as on the pipeline page.</param>
        /// <param name="onSaved">Runs on the UI loop with the pipeline name after a save.</param>
        public static void Open(TuiContext context, Pipeline? existing, bool detailMode, Action<string> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, ct => EntityLookups.PersonasAsync(context.Client, ct), personas => Show(context, existing, detailMode, personas, onSaved), "Failed to load pipelines.");
        }

        /// <summary>
        /// Open the create or edit form with personas loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Pipeline to edit, or null.</param>
        /// <param name="detailMode">Detail edit.</param>
        /// <param name="personas">Personas.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static Modals.FormDialog Show(TuiContext context, Pipeline? existing, bool detailMode, List<Persona> personas, Action<string> onSaved)
        {
            EntityForm form = new EntityForm(context);
            InputField? name = null;
            if (existing == null) name = form.Text("Name", "", "", true);
            TextAreaField description = form.Area("Description", existing?.Description, 3);
            SelectField<string>? scope = null;
            if (!detailMode) scope = form.Scope(existing?.Scope ?? ScopeRules.ResolveCreateScope(context.Session, null));
            List<PipelineStage> stages = existing != null ? existing.Stages.OrderBy(s => s.Order).Select(CopyStage).ToList() : new List<PipelineStage>();
            RecordListField<PipelineStage> stageList = form.List<PipelineStage>("Stages", s => Describe(context, s), stages, 7, "a adds a stage; Enter edits; Del removes; Alt+Up/Down reorders.");
            stageList.Fingerprint = s => s.PersonaName + "|" + s.IsOptional + "|" + s.RequiresReview + "|" + s.ReviewDenyAction + "|" + s.Description;
            stageList.EmptyText = "No stages defined.";
            stageList.Editor = (stage, done) => EditStage(context, stage, personas, done);
            form.MarkClean();
            string savedName = existing?.Name ?? "";
            string title = existing != null ? "Edit Pipeline" : "Create Pipeline";
            return EntityUi.ShowForm(context, title, form, "Save", async ct =>
            {
                List<PipelineStage> payloadStages = new List<PipelineStage>();
                int order = 1;
                foreach (PipelineStage s in stageList.Items.Where(x => !String.IsNullOrWhiteSpace(x.PersonaName)))
                {
                    PipelineStage copy = CopyStage(s);
                    copy.Order = order++;
                    payloadStages.Add(copy);
                }

                Pipeline body = new Pipeline();
                body.Name = existing != null ? existing.Name : name!.Value.Trim();
                body.Description = EntityUi.Blank(description.Value);
                body.Stages = payloadStages;
                if (existing != null)
                {
                    body.Id = existing.Id;
                    body.Scope = scope != null ? EntityForm.EnumValue(scope, existing.Scope) : existing.Scope;
                    body.TenantId = existing.TenantId;
                    body.UserId = existing.UserId;
                    body.IsBuiltIn = existing.IsBuiltIn;
                    body.Active = existing.Active;
                    await context.Client.UpdatePipelineAsync(existing.Name, body, ct).ConfigureAwait(false);
                }
                else
                {
                    body.Scope = ScopeRules.ResolveCreateScope(context.Session, scope != null ? EntityForm.EnumValue(scope, ScopeEnum.TenantWide) : (ScopeEnum?)null);
                    await context.Client.CreatePipelineAsync(body, ct).ConfigureAwait(false);
                }

                savedName = body.Name;
                return null;
            }, () =>
            {
                string text = existing != null
                    ? EntityUi.T(context, "Pipeline \"{{name}}\" saved.", "name", savedName)
                    : EntityUi.T(context, "Pipeline \"{{name}}\" created.", "name", savedName);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(savedName);
            });
        }

        /// <summary>
        /// Open the stage editor (persona, optional, review gate, on deny, description).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="stage">Stage to edit, or null to add.</param>
        /// <param name="personas">Personas.</param>
        /// <param name="done">Receives the edited stage.</param>
        /// <returns>The dialog.</returns>
        public static Modals.FormDialog EditStage(TuiContext context, PipelineStage? stage, List<Persona> personas, Action<PipelineStage> done)
        {
            EntityForm form = new EntityForm(context);
            SelectField<string> persona = PersonaForms.RequiredSelect(form, "Persona", EntityLookups.Options(personas, p => p.Name, p => p.Name), stage?.PersonaName, "Select persona...");
            CheckField optional = form.Check("Optional", stage?.IsOptional ?? false);
            CheckField review = form.Check("Review gate", stage?.RequiresReview ?? false);
            List<SelectOption<string>> denyOptions = new List<SelectOption<string>>
            {
                new SelectOption<string>(ReviewDenyActionEnum.RetryStage.ToString(), context.Loc.T("Retry stage")),
                new SelectOption<string>(ReviewDenyActionEnum.FailPipeline.ToString(), context.Loc.T("Fail pipeline"))
            };
            SelectField<string> deny = form.Select("On Deny", denyOptions, (stage?.ReviewDenyAction ?? ReviewDenyActionEnum.RetryStage).ToString(), null, true, "Action to take if the review gate is denied");
            TextAreaField description = form.Area("Description", stage?.Description, 3);
            form.MarkClean();
            PipelineStage? result = null;
            return EntityUi.ShowForm(context, stage == null ? "Add Stage" : "Edit Stage", form, "Save", ct =>
            {
                PipelineStage s = stage != null ? CopyStage(stage) : new PipelineStage();
                s.PersonaName = persona.Value ?? "";
                s.IsOptional = optional.Value;
                s.RequiresReview = review.Value;
                s.ReviewDenyAction = EntityForm.EnumValue(deny, ReviewDenyActionEnum.RetryStage);
                s.Description = EntityUi.Blank(description.Value);
                result = s;
                return Task.FromResult<string?>(null);
            }, () =>
            {
                if (result != null) done(result);
            });
        }

        /// <summary>
        /// Duplicate a pipeline ("Name (Copy)") and open the copy.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="pipeline">Pipeline.</param>
        public static void Duplicate(TuiContext context, Pipeline pipeline)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            EntityUi.Run<Pipeline?>(context, ct =>
            {
                Pipeline body = new Pipeline();
                body.Name = PersonaForms.DuplicateName(pipeline.Name);
                body.Description = pipeline.Description;
                int order = 1;
                body.Stages = pipeline.Stages.OrderBy(s => s.Order).Select(s =>
                {
                    PipelineStage copy = new PipelineStage();
                    copy.PersonaName = s.PersonaName;
                    copy.IsOptional = s.IsOptional;
                    copy.Description = s.Description;
                    copy.RequiresReview = s.RequiresReview;
                    copy.ReviewDenyAction = s.ReviewDenyAction;
                    copy.Order = order++;
                    return copy;
                }).ToList();
                return context.Client.CreatePipelineAsync(body, ct);
            }, created =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Pipeline \"{{name}}\" duplicated.", "name", created.Name));
                context.Navigate("/pipelines/" + Uri.EscapeDataString(created.Name));
            }, "Duplicate failed.");
        }

        /// <summary>
        /// Open the Run Pipeline modal (vessel, title, objective) and launch a voyage.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="pipeline">Pipeline.</param>
        /// <param name="vessels">Vessels.</param>
        /// <returns>The dialog.</returns>
        public static Modals.FormDialog Run(TuiContext context, Pipeline pipeline, List<Vessel> vessels)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            EntityForm form = new EntityForm(context);
            form.Section("Dispatch a voyage that runs this pipeline's stages against a vessel.");
            SelectField<string> vessel = PersonaForms.RequiredSelect(form, "Vessel", EntityLookups.Options(vessels, v => v.Id, v => v.Name), vessels.FirstOrDefault()?.Id, "Select a vessel...");
            InputField title = form.Text("Title", EntityUi.T(context, "Run: {{name}}", "name", pipeline.Name), "", true);
            TextAreaField objective = form.Area("Objective / Description", "", 4);
            form.MarkClean();
            Voyage? voyage = null;
            return EntityUi.ShowForm(context, "Run Pipeline", form, "Launch Voyage", async ct =>
            {
                string vesselId = vessel.Value ?? "";
                string runTitle = String.IsNullOrWhiteSpace(title.Value) ? EntityUi.T(context, "Run: {{name}}", "name", pipeline.Name) : title.Value.Trim();
                VoyageCreateRequest request = new VoyageCreateRequest();
                request.Title = runTitle;
                request.VesselId = vesselId;
                request.Pipeline = pipeline.Name;
                DispatchRequest mission = new DispatchRequest();
                mission.VesselId = vesselId;
                mission.Title = String.IsNullOrWhiteSpace(title.Value) ? pipeline.Name : title.Value.Trim();
                mission.Description = EntityUi.Blank(objective.Value);
                request.Missions.Add(mission);
                voyage = await context.Client.CreateVoyageAsync(request, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                EntityUi.Toast(context, NotificationSeverityEnum.Success, context.Loc.T("Voyage launched."));
                if (voyage != null) context.Navigate("/voyages/" + voyage.Id);
            });
        }

        /// <summary>
        /// One-line stage summary for lists.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="stage">Stage.</param>
        /// <returns>Text.</returns>
        public static string Describe(TuiContext context, PipelineStage stage)
        {
            List<string> parts = new List<string> { String.IsNullOrEmpty(stage.PersonaName) ? context.Loc.T("(unset)") : stage.PersonaName };
            if (stage.IsOptional) parts.Add("[" + context.Loc.T("Optional") + "]");
            if (stage.RequiresReview) parts.Add("[" + context.Loc.T("Review gate") + ": " + DenyLabel(context, stage.ReviewDenyAction) + "]");
            if (!String.IsNullOrEmpty(stage.Description)) parts.Add("- " + stage.Description);
            return String.Join(" ", parts);
        }

        /// <summary>
        /// The dashboard's stage chain ("Worker [review] -> Judge").
        /// </summary>
        /// <param name="stages">Stages.</param>
        /// <returns>Text.</returns>
        public static string Chain(IEnumerable<PipelineStage>? stages)
        {
            List<PipelineStage> list = stages != null ? stages.OrderBy(s => s.Order).ToList() : new List<PipelineStage>();
            if (list.Count == 0) return "-";
            return String.Join(" -> ", list.Select(s => s.PersonaName + (s.RequiresReview ? " [review]" : "")));
        }

        /// <summary>
        /// Label of a deny action.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="action">Action.</param>
        /// <returns>Translated label.</returns>
        public static string DenyLabel(TuiContext context, ReviewDenyActionEnum action)
        {
            return context.Loc.T(action == ReviewDenyActionEnum.FailPipeline ? "Fail pipeline" : "Retry stage");
        }

        #endregion

        #region Private-Methods

        private static PipelineStage CopyStage(PipelineStage s)
        {
            PipelineStage copy = new PipelineStage();
            copy.Id = s.Id;
            copy.PipelineId = s.PipelineId;
            copy.Order = s.Order;
            if (!String.IsNullOrEmpty(s.PersonaName)) copy.PersonaName = s.PersonaName;
            copy.IsOptional = s.IsOptional;
            copy.Description = s.Description;
            copy.RequiresReview = s.RequiresReview;
            copy.ReviewDenyAction = s.ReviewDenyAction;
            return copy;
        }

        #endregion
    }
}
