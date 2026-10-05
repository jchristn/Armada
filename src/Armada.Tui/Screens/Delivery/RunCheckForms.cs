namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The Run Check modal (dashboard <c>CheckRuns.tsx</c>): vessel, workflow profile, check type (all 19 types, narrowed
    /// to what the resolved profile offers), environment (from the resolved profile, for deploy, rollback, smoke,
    /// health, and verification types), label, mission, voyage, and deployment IDs, branch, commit, and a command
    /// override, plus a live Resolved Profile preview (<c>previewWorkflowProfileForVessel</c>) and Preflight readiness
    /// (<c>getVesselReadiness</c>); errors in the preflight block the run. Use on the UI loop.
    /// </summary>
    public static class RunCheckForms
    {
        #region Public-Methods

        /// <summary>
        /// True for check types that need an environment.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>True when an environment is required.</returns>
        public static bool RequiresEnvironment(CheckRunTypeEnum type)
        {
            return type == CheckRunTypeEnum.Deploy || type == CheckRunTypeEnum.Rollback || type == CheckRunTypeEnum.SmokeTest
                || type == CheckRunTypeEnum.HealthCheck || type == CheckRunTypeEnum.DeploymentVerification || type == CheckRunTypeEnum.RollbackVerification;
        }

        /// <summary>
        /// Check types a profile can run (all types when the profile is null or defines none).
        /// </summary>
        /// <param name="profile">Profile.</param>
        /// <returns>Types.</returns>
        public static List<CheckRunTypeEnum> AvailableTypes(WorkflowProfile? profile)
        {
            List<CheckRunTypeEnum> all = Enum.GetValues(typeof(CheckRunTypeEnum)).Cast<CheckRunTypeEnum>().ToList();
            if (profile == null) return all;
            List<CheckRunTypeEnum> types = new List<CheckRunTypeEnum>();
            if (!String.IsNullOrEmpty(profile.LintCommand)) types.Add(CheckRunTypeEnum.Lint);
            if (!String.IsNullOrEmpty(profile.BuildCommand)) types.Add(CheckRunTypeEnum.Build);
            if (!String.IsNullOrEmpty(profile.UnitTestCommand)) types.Add(CheckRunTypeEnum.UnitTest);
            if (!String.IsNullOrEmpty(profile.IntegrationTestCommand)) types.Add(CheckRunTypeEnum.IntegrationTest);
            if (!String.IsNullOrEmpty(profile.E2ETestCommand)) types.Add(CheckRunTypeEnum.E2ETest);
            if (!String.IsNullOrEmpty(profile.MigrationCommand)) types.Add(CheckRunTypeEnum.Migration);
            if (!String.IsNullOrEmpty(profile.SecurityScanCommand)) types.Add(CheckRunTypeEnum.SecurityScan);
            if (!String.IsNullOrEmpty(profile.PerformanceCommand)) types.Add(CheckRunTypeEnum.Performance);
            if (!String.IsNullOrEmpty(profile.PackageCommand)) types.Add(CheckRunTypeEnum.Package);
            if (!String.IsNullOrEmpty(profile.DeploymentVerificationCommand)) types.Add(CheckRunTypeEnum.DeploymentVerification);
            if (!String.IsNullOrEmpty(profile.RollbackVerificationCommand)) types.Add(CheckRunTypeEnum.RollbackVerification);
            if (!String.IsNullOrEmpty(profile.PublishArtifactCommand)) types.Add(CheckRunTypeEnum.PublishArtifact);
            if (!String.IsNullOrEmpty(profile.ReleaseVersioningCommand)) types.Add(CheckRunTypeEnum.ReleaseVersioning);
            if (!String.IsNullOrEmpty(profile.ChangelogGenerationCommand)) types.Add(CheckRunTypeEnum.Changelog);
            List<WorkflowEnvironmentProfile> envs = profile.Environments ?? new List<WorkflowEnvironmentProfile>();
            if (envs.Any(e => !String.IsNullOrEmpty(e.DeployCommand))) types.Add(CheckRunTypeEnum.Deploy);
            if (envs.Any(e => !String.IsNullOrEmpty(e.RollbackCommand))) types.Add(CheckRunTypeEnum.Rollback);
            if (envs.Any(e => !String.IsNullOrEmpty(e.SmokeTestCommand))) types.Add(CheckRunTypeEnum.SmokeTest);
            if (envs.Any(e => !String.IsNullOrEmpty(e.HealthCheckCommand))) types.Add(CheckRunTypeEnum.HealthCheck);
            if (envs.Any(e => !String.IsNullOrEmpty(e.DeploymentVerificationCommand)) && !types.Contains(CheckRunTypeEnum.DeploymentVerification)) types.Add(CheckRunTypeEnum.DeploymentVerification);
            if (envs.Any(e => !String.IsNullOrEmpty(e.RollbackVerificationCommand)) && !types.Contains(CheckRunTypeEnum.RollbackVerification)) types.Add(CheckRunTypeEnum.RollbackVerification);
            return types.Count > 0 ? types : all;
        }

        /// <summary>
        /// Load vessels and profiles and open the modal.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="prefill">Prefill, or null.</param>
        /// <param name="onRan">Runs on the UI loop with the finished run.</param>
        public static void Open(TuiContext context, CheckRunRequest? prefill, Action<CheckRun> onRan)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, async ct =>
            {
                DeploymentReferenceData data = new DeploymentReferenceData();
                Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(context.Client, ct);
                Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(context.Client, ct);
                await Task.WhenAll(vessels, profiles).ConfigureAwait(false);
                data.Vessels = vessels.Result;
                data.Profiles = profiles.Result;
                return data;
            }, data => Show(context, prefill, data.Vessels, data.Profiles, onRan), "Failed to load check runs.");
        }

        /// <summary>
        /// Open the modal with reference data loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="prefill">Prefill, or null.</param>
        /// <param name="vessels">Vessels.</param>
        /// <param name="profiles">Profiles.</param>
        /// <param name="onRan">Callback with the finished run.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Show(TuiContext context, CheckRunRequest? prefill, List<Vessel> vessels, List<WorkflowProfile> profiles, Action<CheckRun> onRan)
        {
            CheckRunRequest seed = prefill ?? new CheckRunRequest { Type = CheckRunTypeEnum.Build };
            EntityForm form = new EntityForm(context);
            SelectField<string> vessel = form.Select("Vessel", EntityLookups.Options(vessels, v => v.Id, v => v.Name), seed.VesselId, "Select a vessel...");
            SelectField<string> profile = form.Select("Workflow Profile", EntityLookups.Options(profiles, p => p.Id, p => p.Name), seed.WorkflowProfileId, "Resolved default");
            SelectField<string> type = form.Select("Check Type", EntityForm.EnumOptions<CheckRunTypeEnum>(), seed.Type.ToString(), null, true);
            SelectField<string> environment = form.Select("Environment", EnvironmentOptions(null, seed.EnvironmentName), seed.EnvironmentName, "Select an environment...");
            InputField label = form.Text("Label", seed.Label, context.Loc.T("Optional display label"));
            InputField missionId = form.Text("Mission ID", seed.MissionId, "mis_...");
            InputField voyageId = form.Text("Voyage ID", seed.VoyageId, "voy_...");
            InputField deploymentId = form.Text("Deployment ID", seed.DeploymentId, "dpl_...");
            InputField branch = form.Text("Branch", seed.BranchName);
            InputField commit = form.Text("Commit", seed.CommitHash);
            TextAreaField commandOverride = form.Area("Command Override", seed.CommandOverride, 4, false, "Optional ad-hoc command. Leave blank to use the selected or resolved workflow profile command.", ".sh");
            form.Section("Resolved Profile");
            TextBlock resolved = new TextBlock(context.Loc.T("No workflow profile resolved. Provide a command override or select a profile."));
            resolved.Translate = false;
            form.View.AddField("", resolved, null, 5);
            form.Section("Preflight");
            TextBlock preflight = new TextBlock(context.Loc.T("Select a vessel to evaluate readiness."));
            preflight.Translate = false;
            form.View.AddField("", preflight, null, 6);
            form.MarkClean();

            RunCheckState state = new RunCheckState();
            Action refreshPreview = () => RefreshPreview(context, form, state, vessel, profile, type, environment, commandOverride, resolved);
            Action refreshReadiness = () => RefreshReadiness(context, state, vessel, profile, type, environment, commandOverride, preflight);
            vessel.ValueChanged += (s, e) => { refreshPreview(); refreshReadiness(); };
            profile.ValueChanged += (s, e) => { refreshPreview(); refreshReadiness(); };
            type.ValueChanged += (s, e) =>
            {
                if (!RequiresEnvironment(EntityForm.EnumValue(type, CheckRunTypeEnum.Build)) && !String.IsNullOrEmpty(environment.Value)) environment.SetValue("");
                AutoEnvironment(state, type, environment);
                refreshReadiness();
            };
            environment.ValueChanged += (s, e) => refreshReadiness();
            commandOverride.ValueChanged += (s, e) => refreshReadiness();
            if (!String.IsNullOrEmpty(seed.VesselId))
            {
                refreshPreview();
                refreshReadiness();
            }

            CheckRun? ran = null;
            FormDialog dialog = EntityUi.ShowForm(context, "Run Check", form, "Run Check", async ct =>
            {
                CheckRunTypeEnum selectedType = EntityForm.EnumValue(type, CheckRunTypeEnum.Build);
                if (String.IsNullOrEmpty(vessel.Value)) return "Select a vessel before running a check.";
                if (RequiresEnvironment(selectedType) && String.IsNullOrEmpty(environment.Value) && String.IsNullOrWhiteSpace(commandOverride.Value)) return "Select an environment for this check type.";
                if (state.Readiness != null && state.Readiness.ErrorCount > 0) return "Preflight reported blocking errors. Resolve them before running this check.";
                CheckRunRequest request = new CheckRunRequest();
                request.VesselId = vessel.Value!;
                request.WorkflowProfileId = EntityUi.Blank(profile.Value);
                request.Type = selectedType;
                request.EnvironmentName = EntityUi.Blank(environment.Value);
                request.Label = EntityUi.Blank(label.Value);
                request.MissionId = EntityUi.Blank(missionId.Value);
                request.VoyageId = EntityUi.Blank(voyageId.Value);
                request.DeploymentId = EntityUi.Blank(deploymentId.Value);
                request.BranchName = EntityUi.Blank(branch.Value);
                request.CommitHash = EntityUi.Blank(commit.Value);
                request.CommandOverride = EntityUi.Blank(commandOverride.Value);
                ran = await context.Client.RunCheckAsync(request, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (ran == null) return;
                NotificationSeverityEnum severity = ran.Status == CheckRunStatusEnum.Passed ? NotificationSeverityEnum.Success
                    : ran.Status == CheckRunStatusEnum.Failed ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Info;
                EntityUi.Toast(context, severity, EntityUi.T(context, "Check run \"{{id}}\" completed with status {{status}}.", "id", ran.Id, "status", ran.Status.ToString()));
                onRan?.Invoke(ran);
            });
            dialog.Form.SaveButton.Label = "Run Check";
            return dialog;
        }

        #endregion

        #region Private-Methods

        private static List<SelectOption<string>> EnvironmentOptions(WorkflowProfile? profile, string? current)
        {
            List<SelectOption<string>> options = (profile?.Environments ?? new List<WorkflowEnvironmentProfile>())
                .Select(e => new SelectOption<string>(e.EnvironmentName, e.EnvironmentName)).ToList();
            if (!String.IsNullOrEmpty(current) && !options.Any(o => o.Value == current)) options.Add(new SelectOption<string>(current!, current!));
            return options;
        }

        private static void AutoEnvironment(RunCheckState state, SelectField<string> type, SelectField<string> environment)
        {
            if (!RequiresEnvironment(EntityForm.EnumValue(type, CheckRunTypeEnum.Build))) return;
            List<WorkflowEnvironmentProfile> envs = state.Preview?.ResolvedProfile?.Environments ?? new List<WorkflowEnvironmentProfile>();
            if (envs.Count == 1 && String.IsNullOrEmpty(environment.Value)) environment.SetValue(envs[0].EnvironmentName);
        }

        private static void RefreshPreview(TuiContext context, EntityForm form, RunCheckState state, SelectField<string> vessel, SelectField<string> profile, SelectField<string> type, SelectField<string> environment, TextAreaField commandOverride, TextBlock resolved)
        {
            string? vesselId = EntityUi.Blank(vessel.Value);
            int generation = ++state.PreviewGeneration;
            if (vesselId == null)
            {
                state.Preview = null;
                resolved.Text = context.Loc.T("No workflow profile resolved. Provide a command override or select a profile.");
                return;
            }

            resolved.Text = context.Loc.T("Resolving...");
            string? profileId = EntityUi.Blank(profile.Value);
            Task.Run(async () =>
            {
                WorkflowProfileResolutionPreviewResult? preview = null;
                try
                {
                    preview = await context.Client.PreviewWorkflowProfileForVesselAsync(vesselId, profileId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    preview = null;
                }

                context.Dispatcher.Post(() =>
                {
                    if (generation != state.PreviewGeneration) return;
                    state.Preview = preview;
                    WorkflowProfile? resolvedProfile = preview?.ResolvedProfile;
                    resolved.Text = DescribePreview(context, preview);
                    List<CheckRunTypeEnum> available = preview != null && preview.AvailableCheckTypes.Count > 0
                        ? preview.AvailableCheckTypes.Select(t => Enum.TryParse<CheckRunTypeEnum>(t, true, out CheckRunTypeEnum p) ? (CheckRunTypeEnum?)p : null).Where(t => t.HasValue).Select(t => t!.Value).ToList()
                        : AvailableTypes(resolvedProfile);
                    if (available.Count == 0) available = AvailableTypes(null);
                    string current = type.Value ?? CheckRunTypeEnum.Build.ToString();
                    type.Options = available.Select(t => new SelectOption<string>(t.ToString(), t.ToString())).ToList();
                    if (String.IsNullOrWhiteSpace(commandOverride.Value) && !available.Any(t => t.ToString() == current)) type.Choose(type.Options.FirstOrDefault());
                    else type.SetValue(current);
                    form.SetOptions(environment, EnvironmentOptions(resolvedProfile, environment.Value), "Select an environment...");
                    AutoEnvironment(state, type, environment);
                });
            });
        }

        private static void RefreshReadiness(TuiContext context, RunCheckState state, SelectField<string> vessel, SelectField<string> profile, SelectField<string> type, SelectField<string> environment, TextAreaField commandOverride, TextBlock preflight)
        {
            string? vesselId = EntityUi.Blank(vessel.Value);
            int generation = ++state.ReadinessGeneration;
            if (vesselId == null)
            {
                state.Readiness = null;
                preflight.Text = context.Loc.T("Select a vessel to evaluate readiness.");
                return;
            }

            preflight.Text = context.Loc.T("Loading...");
            VesselReadinessQuery query = new VesselReadinessQuery();
            query.WorkflowProfileId = EntityUi.Blank(profile.Value);
            query.CheckType = type.Value;
            query.EnvironmentName = EntityUi.Blank(environment.Value);
            query.IncludeWorkflowRequirements = String.IsNullOrWhiteSpace(commandOverride.Value);
            Task.Run(async () =>
            {
                VesselReadinessResult? readiness = null;
                try
                {
                    readiness = await context.Client.GetVesselReadinessAsync(vesselId, query, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    readiness = null;
                }

                context.Dispatcher.Post(() =>
                {
                    if (generation != state.ReadinessGeneration) return;
                    state.Readiness = readiness;
                    preflight.Text = DescribeReadiness(context, readiness);
                });
            });
        }

        private static string DescribePreview(TuiContext context, WorkflowProfileResolutionPreviewResult? preview)
        {
            if (preview?.ResolvedProfile == null) return context.Loc.T("No workflow profile resolved. Provide a command override or select a profile.");
            StringBuilder sb = new StringBuilder();
            sb.Append(preview.ResolvedProfile.Name).Append(" (").Append(preview.ResolvedProfile.Scope).Append(")\n");
            sb.Append(context.Loc.T("Resolution mode")).Append(": ").Append(preview.ResolutionMode).Append('\n');
            sb.Append(context.Loc.T("Available check types")).Append(": ").Append(preview.AvailableCheckTypes.Count > 0 ? String.Join(", ", preview.AvailableCheckTypes) : context.Loc.T("None")).Append('\n');
            if (preview.CommandPreviews.Count == 0) sb.Append(context.Loc.T("No resolved commands are available for this vessel/profile combination."));
            foreach (WorkflowProfileCommandPreview command in preview.CommandPreviews)
            {
                sb.Append(command.CheckType).Append(String.IsNullOrEmpty(command.EnvironmentName) ? "" : " [" + command.EnvironmentName + "]").Append(": ").Append(command.Command).Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        private static string DescribeReadiness(TuiContext context, VesselReadinessResult? readiness)
        {
            if (readiness == null) return context.Loc.T("Select a vessel to evaluate readiness.");
            StringBuilder sb = new StringBuilder();
            sb.Append(readiness.IsReady ? "+ " + context.Loc.T("Ready") : "x " + context.Loc.T("Not ready"));
            sb.Append("  ").Append(context.Loc.T("Errors")).Append(": ").Append(readiness.ErrorCount);
            sb.Append("  ").Append(context.Loc.T("Warnings")).Append(": ").Append(readiness.WarningCount).Append('\n');
            foreach (VesselReadinessIssue issue in readiness.Issues)
            {
                string marker = issue.Severity == ReadinessSeverityEnum.Error ? "x" : issue.Severity == ReadinessSeverityEnum.Warning ? "!" : "-";
                sb.Append(marker).Append(' ').Append(issue.Title).Append(": ").Append(issue.Message).Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        #endregion
    }
}
