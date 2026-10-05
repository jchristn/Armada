namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The dashboard's Create Vessel / Edit Vessel dialog with every field of both dashboard forms (the Vessels tab
    /// form and the vessel page form): name, fleet, repository URL, default branch, local path, working directory, the
    /// GitHub token override (masked; blank keeps it, the checkbox clears it), landing mode with its explanation,
    /// branch cleanup, agent auto-approve, default pipeline, concurrent missions, model context switch, release and
    /// hotfix prefixes, protected branch patterns, the three branch policy switches, the dock boundary (secret scan,
    /// protected paths, identifier denylist), the auto-land gate, the in-dock Definition-of-Done gate, and the project
    /// context, style guide, and model context. List fields take one entry per line. Edits send the full record.
    /// </summary>
    public static class VesselForm
    {
        #region Public-Methods

        /// <summary>
        /// Open the dialog.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="editing">Vessel to edit, or null to create.</param>
        /// <param name="fleets">Fleets for the picker.</param>
        /// <param name="pipelines">Pipelines for the default pipeline picker.</param>
        /// <param name="saved">Called on the loop with the saved vessel.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is null.</exception>
        public static OpsFormDialog Open(OpsScreen screen, Vessel? editing, IEnumerable<Fleet> fleets, IEnumerable<Pipeline> pipelines, Action<Vessel?> saved)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            LocalizationService loc = screen.Context.Loc;
            OpsFormDialog dialog = screen.NewForm(editing != null ? "Edit Vessel" : "Create Vessel", "Save");
            dialog.WidthRatio = 0.9;

            InputField name = Input(editing?.Name ?? "", null);
            name.Validator = v => String.IsNullOrWhiteSpace(v) ? "Name is required." : null;
            List<SelectOption<string>> fleetOptions = new List<SelectOption<string>> { new SelectOption<string>("", loc.T("Select a fleet...")) };
            fleetOptions.AddRange((fleets ?? Enumerable.Empty<Fleet>()).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => new SelectOption<string>(f.Id, f.Name)));
            SelectField<string> fleet = screen.NewSelect("Fleet", fleetOptions);
            fleet.SetValue(editing?.FleetId ?? "");
            InputField repo = Input(editing?.RepoUrl ?? "", "https://github.com/org/repo.git");
            repo.Validator = v => String.IsNullOrWhiteSpace(v) ? "Repository URL is required." : null;
            InputField branch = Input(editing != null ? (String.IsNullOrEmpty(editing.DefaultBranch) ? "main" : editing.DefaultBranch) : "main", null);
            InputField localPath = Input(editing?.LocalPath ?? "", null);
            InputField workDir = Input(editing?.WorkingDirectory ?? "", null);

            bool hasToken = editing != null && editing.HasGitHubTokenOverride;
            InputField token = Input("", hasToken ? "Leave blank to keep existing override" : "Optional per-vessel GitHub token");
            token.Masked = true;
            string tokenHint = editing == null
                ? "Optional. Leave blank to use the global GitHub token from Armada settings."
                : hasToken
                    ? "This vessel already has an override. Leave blank to keep it, enter a new token to replace it, or clear it below."
                    : "No vessel override is stored. Armada will use the global GitHub token if one is configured.";
            OpsCheckField clearToken = new OpsCheckField("Clear existing GitHub token override", false);
            clearToken.Visible = hasToken;
            clearToken.Changed += (s, e) => { if (clearToken.Checked) token.Value = ""; };
            token.ValueChanged += (s, e) => { if (token.Value.Length > 0) clearToken.Checked = false; };

            SelectField<string> landing = screen.NewSelect("Landing Mode", LandingModeInfo.All.Select(m => new SelectOption<string>(m.Value, loc.T(m.Label))).ToList());
            landing.SetValue(editing != null ? (editing.LandingMode?.ToString() ?? "") : "LocalMerge");
            SelectField<string> cleanup = screen.NewSelect("Branch Cleanup", new List<SelectOption<string>>
            {
                new SelectOption<string>("", loc.T("Default")),
                new SelectOption<string>("LocalOnly", loc.T("Local Only")),
                new SelectOption<string>("LocalAndRemote", loc.T("Local and Remote")),
                new SelectOption<string>("None", loc.T("None")),
            });
            cleanup.SetValue(editing != null ? (editing.BranchCleanupPolicy?.ToString() ?? "") : "LocalAndRemote");
            SelectField<string> autoApprove = screen.NewSelect("Agent Auto-Approve", new List<SelectOption<string>>
            {
                new SelectOption<string>("inherit", loc.T("Use captain setting")),
                new SelectOption<string>("off", loc.T("Off for this vessel")),
                new SelectOption<string>("on", loc.T("On for this vessel")),
            });
            autoApprove.SetValue(editing?.AutoApprove == true ? "on" : editing?.AutoApprove == false ? "off" : "inherit");
            SelectField<string> pipeline = screen.NewSelect("Default Pipeline", BuildText.PipelineOptions(pipelines, loc));
            pipeline.SetValue(editing?.DefaultPipelineId ?? "");
            OpsCheckField concurrent = new OpsCheckField("Allow Concurrent Missions", editing?.AllowConcurrentMissions ?? false);
            OpsCheckField modelContextOn = new OpsCheckField("Enable Model Context", editing?.EnableModelContext ?? true);

            InputField releasePrefix = Input(editing != null && !String.IsNullOrEmpty(editing.ReleaseBranchPrefix) ? editing.ReleaseBranchPrefix : "release/", null);
            InputField hotfixPrefix = Input(editing != null && !String.IsNullOrEmpty(editing.HotfixBranchPrefix) ? editing.HotfixBranchPrefix : "hotfix/", null);
            OpsTextArea protectedBranches = Area(screen, String.Join("\n", editing?.ProtectedBranchPatterns ?? new List<string>()), "One pattern per line, e.g. main or release/*");
            OpsCheckField requireChecks = new OpsCheckField("Require Passing Checks To Land", editing?.RequirePassingChecksToLand ?? false);
            OpsCheckField requirePr = new OpsCheckField("Require PR For Protected Branches", editing?.RequirePullRequestForProtectedBranches ?? false);
            OpsCheckField requireQueue = new OpsCheckField("Require Merge Queue For Release Branches", editing?.RequireMergeQueueForReleaseBranches ?? false);

            OpsCheckField secretScan = new OpsCheckField("Scan Mission Diffs for Secrets", editing?.SecretScanEnabled ?? false);
            OpsTextArea protectedPaths = Area(screen, String.Join("\n", editing?.ProtectedPathPatterns ?? new List<string>()), "One glob per line, e.g. .env* or infra/**");
            OpsTextArea denylist = Area(screen, String.Join("\n", editing?.PrivateIdentifierDenylist ?? new List<string>()), "One value per line; do not list real secrets");

            OpsCheckField autoLand = new OpsCheckField("Auto-land small changes", editing?.AutoLandEnabled ?? false);
            InputField maxFiles = Input(editing != null && editing.AutoLandMaxFiles > 0 ? editing.AutoLandMaxFiles.ToString(CultureInfo.InvariantCulture) : "", "0");
            maxFiles.Validator = NonNegative;
            InputField maxLines = Input(editing != null && editing.AutoLandMaxLines > 0 ? editing.AutoLandMaxLines.ToString(CultureInfo.InvariantCulture) : "", "0");
            maxLines.Validator = NonNegative;
            OpsTextArea allowGlobs = Area(screen, String.Join("\n", editing?.AutoLandPathAllowGlobs ?? new List<string>()), "One glob per line, e.g. src/**");
            OpsTextArea denyGlobs = Area(screen, String.Join("\n", editing?.AutoLandPathDenyGlobs ?? new List<string>()), "One glob per line, e.g. infra/**");

            OpsCheckField dod = new OpsCheckField("Run in-dock build + tests before acceptance", editing?.DefinitionOfDoneEnabled ?? false);
            InputField dodBuild = Input(editing?.DefinitionOfDoneBuildCommand ?? "", "e.g. dotnet build");
            InputField dodTest = Input(editing?.DefinitionOfDoneTestCommand ?? "", "e.g. dotnet test");
            InputField dodTimeout = Input(editing != null && editing.DefinitionOfDoneTimeoutSeconds > 0 ? editing.DefinitionOfDoneTimeoutSeconds.ToString(CultureInfo.InvariantCulture) : "", "1800");
            dodTimeout.Validator = v => String.IsNullOrWhiteSpace(v) || (Int32.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 30 && n <= 7200) ? null : "Enter 30 to 7200 seconds.";

            OpsTextArea projectContext = Area(screen, editing?.ProjectContext ?? "", "");
            OpsTextArea styleGuide = Area(screen, editing?.StyleGuide ?? "", "");
            OpsTextArea modelContext = Area(screen, editing?.ModelContext ?? "", "Agent-accumulated context...");
            modelContext.Visible = modelContextOn.Checked;
            modelContextOn.Changed += (s, e) => modelContext.Visible = modelContextOn.Checked;

            dialog.AddField("Name", name);
            dialog.AddField("Fleet", fleet);
            dialog.AddField("Repository URL", repo);
            dialog.AddField("Default Branch", branch);
            dialog.AddField("Local Path", localPath, "Path to the bare git repository clone used by Armada");
            dialog.AddField("Working Directory", workDir, "Your local checkout where completed missions are merged");
            dialog.AddField("GitHub Token Override", token, tokenHint);
            dialog.AddField("", clearToken);
            dialog.Form.AddSection("Landing");
            dialog.AddField("Landing Mode", landing, LandingModeInfo.For(landing.Value).Description);
            FormRow landingRow = dialog.Form.Rows.Last();
            landing.ValueChanged += (s, e) => landingRow.Hint = LandingModeInfo.For(landing.Value).Description;
            dialog.AddField("Branch Cleanup", cleanup, "When and how mission branches are deleted after successful landing.");
            dialog.AddField("Agent Auto-Approve", autoApprove, "Whether CLI captains run missions on this vessel with their auto-approve (permission bypass) flags. Overrides the captain setting when set.");
            dialog.AddField("Default Pipeline", pipeline);
            dialog.AddField("", concurrent);
            dialog.AddField("", modelContextOn);
            dialog.Form.AddSection("Branch Policy");
            dialog.AddField("Release Branch Prefix", releasePrefix);
            dialog.AddField("Hotfix Branch Prefix", hotfixPrefix);
            dialog.AddField("Protected Branch Patterns", protectedBranches, null, 3);
            dialog.AddField("", requireChecks);
            dialog.AddField("", requirePr);
            dialog.AddField("", requireQueue);
            dialog.Form.AddSection("Dock Boundary");
            dialog.AddField("", secretScan);
            dialog.AddField("Protected Path Patterns", protectedPaths, "A mission that adds or modifies a matching path is flagged as a boundary violation.", 3);
            dialog.AddField("Private Identifier Denylist", denylist, "Added diff lines containing any of these literals are flagged. Do not list actual secrets here.", 3);
            dialog.Form.AddSection("Auto-Land Gate");
            dialog.AddField("", autoLand, "When enabled, a passing mission must satisfy the rules below to land unattended; otherwise it holds for review.");
            dialog.AddField("Max Files (0 = no limit)", maxFiles);
            dialog.AddField("Max Lines (0 = no limit)", maxLines);
            dialog.AddField("Auto-land Allowed Paths", allowGlobs, null, 3);
            dialog.AddField("Auto-land Denied Paths", denyGlobs, null, 3);
            dialog.Form.AddSection("Definition-of-Done Gate");
            dialog.AddField("", dod, "When enabled, the build and unit-test commands below run inside the mission checkout before landing; a failure blocks acceptance.");
            dialog.AddField("Build Command", dodBuild);
            dialog.AddField("Test Command", dodTest);
            dialog.AddField("Per-phase Timeout (seconds)", dodTimeout);
            dialog.Form.AddSection("Context");
            dialog.AddField("Project Context", projectContext, null, 5);
            dialog.AddField("Style Guide", styleGuide, null, 5);
            dialog.AddField("Model Context", modelContext, null, 5);

            dialog.Submit = d =>
            {
                VesselUpsertRequest body = editing != null ? VesselUpsertRequest.From(editing) : new VesselUpsertRequest();
                body.Name = name.Value.Trim();
                body.FleetId = String.IsNullOrEmpty(fleet.Value) ? null : fleet.Value;
                body.RepoUrl = repo.Value.Trim();
                body.DefaultBranch = String.IsNullOrWhiteSpace(branch.Value) ? "main" : branch.Value.Trim();
                body.LocalPath = Blank(localPath.Value);
                body.WorkingDirectory = Blank(workDir.Value);
                if (clearToken.Checked) body.GitHubTokenOverrideInput = "";
                else if (!String.IsNullOrWhiteSpace(token.Value)) body.GitHubTokenOverrideInput = token.Value.Trim();
                else body.GitHubTokenOverrideInput = null;
                body.LandingMode = Enum.TryParse<LandingModeEnum>(landing.Value ?? "", out LandingModeEnum lm) ? lm : (LandingModeEnum?)null;
                body.BranchCleanupPolicy = Enum.TryParse<BranchCleanupPolicyEnum>(cleanup.Value ?? "", out BranchCleanupPolicyEnum bc) ? bc : (BranchCleanupPolicyEnum?)null;
                body.AutoApprove = autoApprove.Value == "on" ? true : autoApprove.Value == "off" ? false : (bool?)null;
                body.DefaultPipelineId = String.IsNullOrEmpty(pipeline.Value) ? null : pipeline.Value;
                body.AllowConcurrentMissions = concurrent.Checked;
                body.EnableModelContext = modelContextOn.Checked;
                body.ReleaseBranchPrefix = String.IsNullOrWhiteSpace(releasePrefix.Value) ? "release/" : releasePrefix.Value.Trim();
                body.HotfixBranchPrefix = String.IsNullOrWhiteSpace(hotfixPrefix.Value) ? "hotfix/" : hotfixPrefix.Value.Trim();
                body.ProtectedBranchPatterns = BuildText.Lines(protectedBranches.Text);
                body.RequirePassingChecksToLand = requireChecks.Checked;
                body.RequirePullRequestForProtectedBranches = requirePr.Checked;
                body.RequireMergeQueueForReleaseBranches = requireQueue.Checked;
                body.SecretScanEnabled = secretScan.Checked;
                body.ProtectedPathPatterns = BuildText.Lines(protectedPaths.Text);
                body.PrivateIdentifierDenylist = BuildText.Lines(denylist.Text);
                body.AutoLandEnabled = autoLand.Checked;
                body.AutoLandMaxFiles = ParseOrZero(maxFiles.Value);
                body.AutoLandMaxLines = ParseOrZero(maxLines.Value);
                body.AutoLandPathAllowGlobs = BuildText.Lines(allowGlobs.Text);
                body.AutoLandPathDenyGlobs = BuildText.Lines(denyGlobs.Text);
                body.DefinitionOfDoneEnabled = dod.Checked;
                body.DefinitionOfDoneBuildCommand = dodBuild.Value.Trim();
                body.DefinitionOfDoneTestCommand = dodTest.Value.Trim();
                body.DefinitionOfDoneTimeoutSeconds = Int32.TryParse(dodTimeout.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) ? Math.Max(30, timeout) : 1800;
                body.ProjectContext = Blank(projectContext.Text);
                body.StyleGuide = Blank(styleGuide.Text);
                body.ModelContext = Blank(modelContext.Text);
                string label = body.Name;
                if (editing != null)
                {
                    screen.Call((c, t) => c.UpdateVesselAsync(editing.Id, body, t), r =>
                    {
                        d.Complete();
                        screen.Toast(NotificationSeverityEnum.Success, screen.Tr("Vessel \"{{name}}\" saved.", LocalizationArgs.Of("name", label)));
                        saved?.Invoke(r);
                    }, null, ex => d.Fail(ex is ArmadaApiException api && !String.IsNullOrEmpty(api.Message) ? api.Message : screen.Tr("Save failed.")));
                }
                else
                {
                    screen.Call((c, t) => c.CreateVesselAsync(body, t), r =>
                    {
                        d.Complete();
                        screen.Toast(NotificationSeverityEnum.Success, screen.Tr("Vessel \"{{name}}\" created.", LocalizationArgs.Of("name", label)));
                        saved?.Invoke(r);
                    }, null, ex => d.Fail(ex is ArmadaApiException api && !String.IsNullOrEmpty(api.Message) ? api.Message : screen.Tr("Save failed.")));
                }

                return false;
            };

            screen.Context.Modals.Show(dialog);
            return dialog;
        }

        /// <summary>
        /// The dashboard's duplicate payload for a vessel (<c>buildVesselDuplicatePayload</c>).
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>New vessel body.</returns>
        public static Vessel DuplicatePayload(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            Vessel copy = new Vessel(BuildText.DuplicateName(vessel.Name), vessel.RepoUrl ?? "");
            copy.FleetId = vessel.FleetId;
            copy.LocalPath = vessel.LocalPath;
            copy.WorkingDirectory = vessel.WorkingDirectory;
            copy.DefaultBranch = String.IsNullOrEmpty(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch;
            copy.ProjectContext = vessel.ProjectContext;
            copy.StyleGuide = vessel.StyleGuide;
            copy.EnableModelContext = vessel.EnableModelContext;
            copy.ModelContext = vessel.ModelContext;
            copy.LandingMode = vessel.LandingMode;
            copy.BranchCleanupPolicy = vessel.BranchCleanupPolicy;
            copy.RequirePassingChecksToLand = vessel.RequirePassingChecksToLand;
            copy.ProtectedBranchPatterns = (vessel.ProtectedBranchPatterns ?? new List<string>()).ToList();
            copy.ReleaseBranchPrefix = vessel.ReleaseBranchPrefix;
            copy.HotfixBranchPrefix = vessel.HotfixBranchPrefix;
            copy.RequirePullRequestForProtectedBranches = vessel.RequirePullRequestForProtectedBranches;
            copy.RequireMergeQueueForReleaseBranches = vessel.RequireMergeQueueForReleaseBranches;
            copy.AllowConcurrentMissions = vessel.AllowConcurrentMissions;
            copy.DefaultPipelineId = vessel.DefaultPipelineId;
            return copy;
        }

        #endregion

        #region Private-Methods

        private static InputField Input(string value, string? placeholder)
        {
            InputField input = new InputField();
            input.Value = value ?? "";
            if (placeholder != null) input.Placeholder = placeholder;
            return input;
        }

        private static OpsTextArea Area(OpsScreen screen, string text, string placeholder)
        {
            OpsTextArea area = new OpsTextArea();
            area.Text = text ?? "";
            area.Placeholder = placeholder ?? "";
            area.ExternalEditor = (t, done) => screen.EditExternally(t, done);
            return area;
        }

        private static string? NonNegative(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return Int32.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 ? null : "Enter 0 or a positive number.";
        }

        private static int ParseOrZero(string value)
        {
            return Int32.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Max(0, n) : 0;
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrEmpty(value) ? null : value;
        }

        #endregion
    }
}
