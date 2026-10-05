namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The release fields shared by the Releases tab's create/edit modal and the release detail editor (dashboard
    /// <c>Releases.tsx</c> and <c>ReleaseDetail.tsx</c>): title (required), status, vessel, workflow profile, version,
    /// tag name, summary, notes, voyage IDs, mission IDs, and check run IDs (one per line or comma separated). Use on
    /// the UI loop.
    /// </summary>
    public class ReleaseForms
    {
        #region Public-Members

        /// <summary>
        /// The form.
        /// </summary>
        public EntityForm Form { get; }

        /// <summary>
        /// Title.
        /// </summary>
        public InputField Title { get; }

        /// <summary>
        /// Status.
        /// </summary>
        public SelectField<string> Status { get; }

        /// <summary>
        /// Vessel.
        /// </summary>
        public SelectField<string> Vessel { get; }

        /// <summary>
        /// Workflow profile.
        /// </summary>
        public SelectField<string> Profile { get; }

        /// <summary>
        /// Version.
        /// </summary>
        public InputField Version { get; }

        /// <summary>
        /// Tag name.
        /// </summary>
        public InputField TagName { get; }

        /// <summary>
        /// Summary.
        /// </summary>
        public TextAreaField Summary { get; }

        /// <summary>
        /// Notes.
        /// </summary>
        public TextAreaField Notes { get; }

        /// <summary>
        /// Voyage IDs.
        /// </summary>
        public TextAreaField VoyageIds { get; }

        /// <summary>
        /// Mission IDs.
        /// </summary>
        public TextAreaField MissionIds { get; }

        /// <summary>
        /// Check run IDs.
        /// </summary>
        public TextAreaField CheckRunIds { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build the fields.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="notesHeight">Rows for the notes field.</param>
        public ReleaseForms(TuiContext context, int notesHeight = 6)
        {
            Form = new EntityForm(context);
            Title = Form.Text("Title", "Draft Release", "", true);
            Status = Form.Enum("Status", ReleaseStatusEnum.Draft);
            Vessel = Form.Select("Vessel", new List<SelectOption<string>>(), "", "Resolve from linked work or select a vessel...");
            Profile = Form.Select("Workflow Profile", new List<SelectOption<string>>(), "", "Resolved default");
            Version = Form.Text("Version", "", "1.2.3");
            TagName = Form.Text("Tag Name", "", "v1.2.3");
            Summary = Form.Area("Summary", "", 3);
            Notes = Form.Area("Notes", "", notesHeight);
            VoyageIds = Form.Area("Voyage IDs", "", 3, false, null, ".txt");
            VoyageIds.Placeholder = "voy_...";
            MissionIds = Form.Area("Mission IDs", "", 3, false, null, ".txt");
            MissionIds.Placeholder = "mis_...";
            CheckRunIds = Form.Area("Check Run IDs", "", 3, false, null, ".txt");
            CheckRunIds.Placeholder = "chk_...";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load reference data and open the create or edit modal.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Release to edit, or null to create.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved release.</param>
        public static void Open(TuiContext context, Release? existing, Action<Release> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, async ct =>
            {
                Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(context.Client, ct);
                Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(context.Client, ct);
                await Task.WhenAll(vessels, profiles).ConfigureAwait(false);
                DeploymentReferenceData data = new DeploymentReferenceData();
                data.Vessels = vessels.Result;
                data.Profiles = profiles.Result;
                return data;
            }, data => Show(context, existing, data, onSaved), "Failed to load releases.");
        }

        /// <summary>
        /// Open the modal with reference data loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Release to edit, or null.</param>
        /// <param name="data">Vessels and profiles.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static Modals.FormDialog Show(TuiContext context, Release? existing, DeploymentReferenceData data, Action<Release> onSaved)
        {
            ReleaseForms fields = new ReleaseForms(context);
            fields.SetReferenceData(data.Vessels, data.Profiles);
            if (existing != null) fields.Fill(existing);
            fields.Form.MarkClean();
            Release? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Release" : "Create Release", fields.Form, existing != null ? "Save Changes" : "Create Release", async ct =>
            {
                ReleaseUpsertRequest payload = fields.BuildPayload(new List<string>());
                saved = existing != null
                    ? await context.Client.UpdateReleaseAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateReleaseAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Release \"{{title}}\" saved.", "title", saved.Title)
                    : EntityUi.T(context, "Release \"{{title}}\" created.", "title", saved.Title);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        /// <summary>
        /// Set the vessel and workflow profile options.
        /// </summary>
        /// <param name="vessels">Vessels.</param>
        /// <param name="profiles">Profiles.</param>
        public void SetReferenceData(List<Vessel> vessels, List<WorkflowProfile> profiles)
        {
            Form.SetOptions(Vessel, EntityLookups.Options(vessels, v => v.Id, v => v.Name), "Resolve from linked work or select a vessel...");
            Form.SetOptions(Profile, EntityLookups.Options(profiles, p => p.Id, p => p.Name), "Resolved default");
        }

        /// <summary>
        /// Fill from a release.
        /// </summary>
        /// <param name="r">Release.</param>
        public void Fill(Release r)
        {
            Title.Value = r.Title;
            Status.SetValue(r.Status.ToString());
            Vessel.SetValue(r.VesselId ?? "");
            Profile.SetValue(r.WorkflowProfileId ?? "");
            Version.Value = r.Version ?? "";
            TagName.Value = r.TagName ?? "";
            Summary.Value = r.Summary ?? "";
            Notes.Value = r.Notes ?? "";
            VoyageIds.Value = EntityUi.JoinLines(r.VoyageIds);
            MissionIds.Value = EntityUi.JoinLines(r.MissionIds);
            CheckRunIds.Value = EntityUi.JoinLines(r.CheckRunIds);
        }

        /// <summary>
        /// Fill from a create prefill.
        /// </summary>
        /// <param name="p">Prefill.</param>
        public void Fill(ReleaseUpsertRequest p)
        {
            Title.Value = String.IsNullOrEmpty(p.Title) ? "Draft Release" : p.Title!;
            Status.SetValue((p.Status ?? ReleaseStatusEnum.Draft).ToString());
            Vessel.SetValue(p.VesselId ?? "");
            Profile.SetValue(p.WorkflowProfileId ?? "");
            Version.Value = p.Version ?? "";
            TagName.Value = p.TagName ?? "";
            Summary.Value = p.Summary ?? "";
            Notes.Value = p.Notes ?? "";
            VoyageIds.Value = EntityUi.JoinLines(p.VoyageIds);
            MissionIds.Value = EntityUi.JoinLines(p.MissionIds);
            CheckRunIds.Value = EntityUi.JoinLines(p.CheckRunIds);
        }

        /// <summary>
        /// Build the upsert payload.
        /// </summary>
        /// <param name="objectiveIds">Backlog items to link (create from a backlog prefill).</param>
        /// <returns>Payload.</returns>
        public ReleaseUpsertRequest BuildPayload(List<string> objectiveIds)
        {
            return new ReleaseUpsertRequest
            {
                VesselId = EntityUi.Blank(Vessel.Value),
                WorkflowProfileId = EntityUi.Blank(Profile.Value),
                Title = EntityUi.Blank(Title.Value),
                Version = EntityUi.Blank(Version.Value),
                TagName = EntityUi.Blank(TagName.Value),
                Summary = EntityUi.Blank(Summary.Value),
                Notes = EntityUi.Blank(Notes.Value),
                Status = EntityForm.EnumValue(Status, ReleaseStatusEnum.Draft),
                VoyageIds = EntityUi.SplitLines(VoyageIds.Value),
                MissionIds = EntityUi.SplitLines(MissionIds.Value),
                CheckRunIds = EntityUi.SplitLines(CheckRunIds.Value),
                ObjectiveIds = objectiveIds ?? new List<string>()
            };
        }

        #endregion
    }
}
