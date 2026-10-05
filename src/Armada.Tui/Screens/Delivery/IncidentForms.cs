namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Incident create and edit forms. The short form is the Incidents tab's Create Incident modal (title, status,
    /// severity, vessel, environment, deployment, release, summary, impact); the full form is the incident detail
    /// editor and the <c>/incidents/new</c> create page (adds environment name, mission and voyage IDs, detected,
    /// mitigated, and closed times, rollback deployment, root cause, recovery notes, and postmortem). Choosing an
    /// environment fills the environment name and an empty vessel; choosing a deployment fills empty environment,
    /// vessel, release, mission, and voyage fields. Use on the UI loop.
    /// </summary>
    public static class IncidentForms
    {
        #region Public-Methods

        /// <summary>
        /// Load reference lists and open the form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Incident to edit, or null to create.</param>
        /// <param name="prefill">Prefill for a new incident, or null.</param>
        /// <param name="full">Show every field (detail editor and create page) instead of the short modal.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved incident.</param>
        public static void Open(TuiContext context, Incident? existing, IncidentUpsertRequest? prefill, bool full, Action<Incident> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, async ct =>
            {
                IncidentReferenceData data = new IncidentReferenceData();
                Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(context.Client, ct);
                Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(context.Client, ct);
                Task<List<Deployment>> deployments = EntityLookups.DeploymentsAsync(context.Client, ct);
                Task<List<Release>> releases = EntityLookups.ReleasesAsync(context.Client, ct);
                await Task.WhenAll(vessels, environments, deployments, releases).ConfigureAwait(false);
                data.Vessels = vessels.Result;
                data.Environments = environments.Result;
                data.Deployments = deployments.Result;
                data.Releases = releases.Result;
                return data;
            }, data => Show(context, existing, prefill, full, data, onSaved), "Failed to load incidents.");
        }

        /// <summary>
        /// Open the form with reference data loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Incident to edit, or null.</param>
        /// <param name="prefill">Prefill, or null.</param>
        /// <param name="full">Full form.</param>
        /// <param name="data">Reference data.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Show(TuiContext context, Incident? existing, IncidentUpsertRequest? prefill, bool full, IncidentReferenceData data, Action<Incident> onSaved)
        {
            IncidentUpsertRequest seed = prefill ?? new IncidentUpsertRequest();
            if (existing != null)
            {
                seed = new IncidentUpsertRequest
                {
                    Title = existing.Title,
                    Summary = existing.Summary,
                    Status = existing.Status,
                    Severity = existing.Severity,
                    EnvironmentId = existing.EnvironmentId,
                    EnvironmentName = existing.EnvironmentName,
                    DeploymentId = existing.DeploymentId,
                    ReleaseId = existing.ReleaseId,
                    VesselId = existing.VesselId,
                    MissionId = existing.MissionId,
                    VoyageId = existing.VoyageId,
                    RollbackDeploymentId = existing.RollbackDeploymentId,
                    Impact = existing.Impact,
                    RootCause = existing.RootCause,
                    RecoveryNotes = existing.RecoveryNotes,
                    Postmortem = existing.Postmortem,
                    DetectedUtc = existing.DetectedUtc,
                    MitigatedUtc = existing.MitigatedUtc,
                    ClosedUtc = existing.ClosedUtc
                };
            }

            EntityForm form = new EntityForm(context);
            InputField title = form.Text("Title", String.IsNullOrEmpty(seed.Title) ? "Incident" : seed.Title, "", true);
            SelectField<string> status = form.Enum("Status", seed.Status ?? IncidentStatusEnum.Open);
            SelectField<string> severity = form.Enum("Severity", seed.Severity ?? IncidentSeverityEnum.High);
            SelectField<string> vessel = form.Select("Vessel", EntityLookups.Options(data.Vessels, v => v.Id, v => v.Name), seed.VesselId, "Select a vessel");
            SelectField<string> environment = form.Select("Environment", EntityLookups.Options(data.Environments, e => e.Id, e => e.Name), seed.EnvironmentId, "Select an environment");
            InputField? environmentName = full ? form.Text("Environment Name", seed.EnvironmentName) : null;
            SelectField<string> deployment = form.Select("Deployment", EntityLookups.Options(data.Deployments, d => d.Id, d => d.Title), seed.DeploymentId, "No linked deployment");
            SelectField<string> release = form.Select("Release", EntityLookups.Options(data.Releases, r => r.Id, r => r.Title), seed.ReleaseId, "No linked release");
            InputField? missionId = full ? form.Text("Mission ID", seed.MissionId, "mis_...") : null;
            InputField? voyageId = full ? form.Text("Voyage ID", seed.VoyageId, "voy_...") : null;
            DateField? detected = null;
            DateField? mitigated = null;
            DateField? closed = null;
            InputField? rollbackDeploymentId = null;
            if (full)
            {
                detected = AddDate(form, "Detected", seed.DetectedUtc);
                mitigated = AddDate(form, "Mitigated", seed.MitigatedUtc);
                closed = AddDate(form, "Closed", seed.ClosedUtc);
                rollbackDeploymentId = form.Text("Rollback Deployment", seed.RollbackDeploymentId, "dpl_...");
            }

            TextAreaField summary = form.Area("Summary", seed.Summary, 3);
            TextAreaField impact = form.Area("Impact", seed.Impact, 3);
            TextAreaField? rootCause = full ? form.Area("Root Cause", seed.RootCause, 3) : null;
            TextAreaField? recoveryNotes = full ? form.Area("Recovery Notes", seed.RecoveryNotes, 4) : null;
            TextAreaField? postmortem = full ? form.Area("Postmortem", seed.Postmortem, 5) : null;

            environment.ValueChanged += (s, e) =>
            {
                DeploymentEnvironment? selected = data.Environments.FirstOrDefault(x => x.Id == environment.Value);
                if (selected == null) return;
                if (environmentName != null) environmentName.Value = selected.Name;
                if (String.IsNullOrEmpty(vessel.Value) && !String.IsNullOrEmpty(selected.VesselId)) vessel.SetValue(selected.VesselId);
            };
            deployment.ValueChanged += (s, e) =>
            {
                Deployment? selected = data.Deployments.FirstOrDefault(x => x.Id == deployment.Value);
                if (selected == null) return;
                if (String.IsNullOrEmpty(environment.Value) && !String.IsNullOrEmpty(selected.EnvironmentId)) environment.SetValue(selected.EnvironmentId);
                if (environmentName != null && String.IsNullOrEmpty(environmentName.Value) && !String.IsNullOrEmpty(selected.EnvironmentName)) environmentName.Value = selected.EnvironmentName!;
                if (String.IsNullOrEmpty(vessel.Value) && !String.IsNullOrEmpty(selected.VesselId)) vessel.SetValue(selected.VesselId);
                if (String.IsNullOrEmpty(release.Value) && !String.IsNullOrEmpty(selected.ReleaseId)) release.SetValue(selected.ReleaseId);
                if (missionId != null && String.IsNullOrEmpty(missionId.Value) && !String.IsNullOrEmpty(selected.MissionId)) missionId.Value = selected.MissionId!;
                if (voyageId != null && String.IsNullOrEmpty(voyageId.Value) && !String.IsNullOrEmpty(selected.VoyageId)) voyageId.Value = selected.VoyageId!;
            };
            form.MarkClean();

            Incident? saved = null;
            string dialogTitle = existing != null ? "Edit Incident" : "Create Incident";
            string saveLabel = existing != null ? "Save Incident" : "Create Incident";
            return EntityUi.ShowForm(context, dialogTitle, form, saveLabel, async ct =>
            {
                IncidentUpsertRequest payload = new IncidentUpsertRequest();
                payload.Title = EntityUi.Blank(title.Value);
                payload.Summary = EntityUi.Blank(summary.Value);
                payload.Status = EntityForm.EnumValue(status, IncidentStatusEnum.Open);
                payload.Severity = EntityForm.EnumValue(severity, IncidentSeverityEnum.High);
                payload.VesselId = EntityUi.Blank(vessel.Value);
                payload.EnvironmentId = EntityUi.Blank(environment.Value);
                payload.EnvironmentName = environmentName != null
                    ? EntityUi.Blank(environmentName.Value)
                    : (String.IsNullOrEmpty(environment.Value) ? null : data.Environments.FirstOrDefault(x => x.Id == environment.Value)?.Name);
                payload.DeploymentId = EntityUi.Blank(deployment.Value);
                payload.ReleaseId = EntityUi.Blank(release.Value);
                payload.Impact = EntityUi.Blank(impact.Value);
                if (full)
                {
                    payload.MissionId = EntityUi.Blank(missionId!.Value);
                    payload.VoyageId = EntityUi.Blank(voyageId!.Value);
                    payload.RollbackDeploymentId = EntityUi.Blank(rollbackDeploymentId!.Value);
                    payload.RootCause = EntityUi.Blank(rootCause!.Value);
                    payload.RecoveryNotes = EntityUi.Blank(recoveryNotes!.Value);
                    payload.Postmortem = EntityUi.Blank(postmortem!.Value);
                    payload.DetectedUtc = detected!.ValueUtc;
                    payload.MitigatedUtc = mitigated!.ValueUtc;
                    payload.ClosedUtc = closed!.ValueUtc;
                }
                else if (prefill != null)
                {
                    payload.MissionId = prefill.MissionId;
                    payload.VoyageId = prefill.VoyageId;
                }

                saved = existing != null
                    ? await context.Client.UpdateIncidentAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateIncidentAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Incident \"{{title}}\" saved.", "title", saved.Title)
                    : EntityUi.T(context, "Incident \"{{title}}\" created.", "title", saved.Title);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        #endregion

        #region Private-Methods

        private static DateField AddDate(EntityForm form, string label, DateTime? utc)
        {
            DateField field = new DateField();
            if (utc.HasValue) field.SetDate(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime());
            return form.View.AddField(label, field, "yyyy-mm-dd hh:mm, today, or -2h");
        }

        #endregion
    }
}
