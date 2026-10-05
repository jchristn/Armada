namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The deployment create and edit form shared by the Deployments tab and the deployment detail screen (the
    /// dashboard's Create/Edit Deployment modal and detail form): vessel, workflow profile, environment or environment
    /// name, release, source ref, mission and voyage IDs, title, summary, notes, and "execute immediately when
    /// approval is not required". Choosing an environment fills the environment name and an empty vessel; choosing a
    /// release fills an empty vessel; the vessel narrows the environment and release pickers. Use on the UI loop.
    /// </summary>
    public static class DeploymentForms
    {
        #region Public-Methods

        /// <summary>
        /// Load the reference lists and open the form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Deployment to edit, or null to create.</param>
        /// <param name="prefill">Prefill for a new deployment, or null.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved deployment.</param>
        public static void Open(TuiContext context, Deployment? existing, DeploymentUpsertRequest? prefill, Action<Deployment> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, async ct =>
            {
                DeploymentReferenceData data = new DeploymentReferenceData();
                Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(context.Client, ct);
                Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(context.Client, ct);
                Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(context.Client, ct);
                Task<List<Release>> releases = EntityLookups.ReleasesAsync(context.Client, ct);
                await Task.WhenAll(vessels, profiles, environments, releases).ConfigureAwait(false);
                data.Vessels = vessels.Result;
                data.Profiles = profiles.Result;
                data.Environments = environments.Result;
                data.Releases = releases.Result;
                return data;
            }, data => Show(context, existing, prefill, data, onSaved), "Failed to load deployment reference data.");
        }

        /// <summary>
        /// Open the form with reference data already loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Deployment to edit, or null.</param>
        /// <param name="prefill">Prefill, or null.</param>
        /// <param name="data">Reference data.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static Modals.FormDialog Show(TuiContext context, Deployment? existing, DeploymentUpsertRequest? prefill, DeploymentReferenceData data, Action<Deployment> onSaved)
        {
            DeploymentUpsertRequest seed = prefill ?? new DeploymentUpsertRequest();
            if (existing != null)
            {
                seed = new DeploymentUpsertRequest
                {
                    VesselId = existing.VesselId,
                    WorkflowProfileId = existing.WorkflowProfileId,
                    EnvironmentId = existing.EnvironmentId,
                    EnvironmentName = existing.EnvironmentName,
                    ReleaseId = existing.ReleaseId,
                    SourceRef = existing.SourceRef,
                    MissionId = existing.MissionId,
                    VoyageId = existing.VoyageId,
                    Title = existing.Title,
                    Summary = existing.Summary,
                    Notes = existing.Notes,
                    AutoExecute = true
                };
            }

            EntityForm form = new EntityForm(context);
            SelectField<string> vessel = form.Select("Vessel", EntityLookups.Options(data.Vessels, v => v.Id, v => v.Name), seed.VesselId, "Select a vessel");
            SelectField<string> profile = form.Select("Workflow Profile", EntityLookups.Options(data.Profiles, p => p.Id, p => p.Name), seed.WorkflowProfileId, "Resolved default");
            SelectField<string> environment = form.Select("Environment", EnvironmentOptions(data, seed.VesselId), seed.EnvironmentId, "Resolve by environment name");
            InputField environmentName = form.Text("Environment Name", seed.EnvironmentName, context.Loc.T("staging, production, customer-a"));
            SelectField<string> release = form.Select("Release", ReleaseOptions(data, seed.VesselId), seed.ReleaseId, "No linked release");
            InputField sourceRef = form.Text("Source Ref", seed.SourceRef, context.Loc.T("branch, tag, or commit"));
            InputField missionId = form.Text("Mission ID", seed.MissionId, "mis_...");
            InputField voyageId = form.Text("Voyage ID", seed.VoyageId, "voy_...");
            InputField title = form.Text("Title", String.IsNullOrEmpty(seed.Title) ? "Deployment" : seed.Title);
            TextAreaField summary = form.Area("Summary", seed.Summary, 3);
            TextAreaField notes = form.Area("Notes", seed.Notes, 4);
            CheckField autoExecute = form.Check("Execute immediately when approval is not required", seed.AutoExecute ?? true);

            vessel.ValueChanged += (s, e) =>
            {
                form.SetOptions(environment, EnvironmentOptions(data, vessel.Value), "Resolve by environment name");
                form.SetOptions(release, ReleaseOptions(data, vessel.Value), "No linked release");
            };
            environment.ValueChanged += (s, e) =>
            {
                DeploymentEnvironment? selected = data.Environments.FirstOrDefault(x => x.Id == environment.Value);
                if (selected == null) return;
                environmentName.Value = selected.Name;
                if (String.IsNullOrEmpty(vessel.Value) && !String.IsNullOrEmpty(selected.VesselId)) vessel.Choose(vessel.Options.FirstOrDefault(o => o.Value == selected.VesselId));
            };
            release.ValueChanged += (s, e) =>
            {
                Release? selected = data.Releases.FirstOrDefault(x => x.Id == release.Value);
                if (selected != null && String.IsNullOrEmpty(vessel.Value) && !String.IsNullOrEmpty(selected.VesselId)) vessel.Choose(vessel.Options.FirstOrDefault(o => o.Value == selected.VesselId));
            };
            form.MarkClean();

            Deployment? saved = null;
            string dialogTitle = existing != null ? "Edit Deployment" : "Create Deployment";
            string saveLabel = existing != null ? "Save Changes" : "Create Deployment";
            return EntityUi.ShowForm(context, dialogTitle, form, saveLabel, async ct =>
            {
                DeploymentUpsertRequest payload = new DeploymentUpsertRequest
                {
                    VesselId = EntityUi.Blank(vessel.Value),
                    WorkflowProfileId = EntityUi.Blank(profile.Value),
                    EnvironmentId = EntityUi.Blank(environment.Value),
                    EnvironmentName = EntityUi.Blank(environmentName.Value),
                    ReleaseId = EntityUi.Blank(release.Value),
                    MissionId = EntityUi.Blank(missionId.Value),
                    VoyageId = EntityUi.Blank(voyageId.Value),
                    Title = EntityUi.Blank(title.Value),
                    SourceRef = EntityUi.Blank(sourceRef.Value),
                    Summary = EntityUi.Blank(summary.Value),
                    Notes = EntityUi.Blank(notes.Value),
                    ObjectiveIds = prefill?.ObjectiveIds,
                    AutoExecute = autoExecute.Value
                };
                saved = existing != null
                    ? await context.Client.UpdateDeploymentAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateDeploymentAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Deployment \"{{title}}\" saved.", "title", saved.Title)
                    : EntityUi.T(context, "Deployment \"{{title}}\" created.", "title", saved.Title);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        #endregion

        #region Private-Methods

        private static List<SelectOption<string>> EnvironmentOptions(DeploymentReferenceData data, string? vesselId)
        {
            return EntityLookups.Options(data.Environments.Where(e => String.IsNullOrEmpty(vesselId) || e.VesselId == vesselId), e => e.Id, e => e.Name);
        }

        private static List<SelectOption<string>> ReleaseOptions(DeploymentReferenceData data, string? vesselId)
        {
            return EntityLookups.Options(data.Releases.Where(r => String.IsNullOrEmpty(vesselId) || r.VesselId == vesselId), r => r.Id, r => r.Title);
        }

        #endregion
    }
}
