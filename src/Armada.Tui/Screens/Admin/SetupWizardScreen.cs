namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The setup wizard (dashboard <c>components/SetupWizard.tsx</c>, TUI route <c>/setup</c>): Objective, Fleet,
    /// Vessel, Captain, Dispatch, and Handoff. Each of the Fleet, Vessel, and Captain steps reuses an existing record
    /// or creates one; Dispatch sends one low-risk onboarding mission through the direct dispatch endpoint; Handoff
    /// follows the mission until it settles and links into onboarding, backlog, planning, workspace, workflow
    /// profiles, environments, checks, and playbooks. Skip Setup and Finish Setup set the "setup completed"
    /// preference and land on Missions. <c>Ctrl+S</c> in a step form runs the step's primary action. Not
    /// thread-safe.
    /// </summary>
    public class SetupWizardScreen : ScreenBase
    {
        #region Public-Members

        /// <summary>
        /// Step titles (English).
        /// </summary>
        public static IReadOnlyList<string> StepTitles { get; } = new List<string> { "Objective", "Fleet", "Vessel", "Captain", "Dispatch", "Handoff" };

        /// <summary>
        /// Step summaries (English).
        /// </summary>
        public static IReadOnlyList<string> StepSummaries { get; } = new List<string>
        {
            "Configure Armada to dispatch one safe first mission.",
            "Create or choose the group that owns your repository.",
            "Register the git repository that captains will work in.",
            "Create or choose an AI runtime so dispatch has capacity.",
            "Send a low-risk onboarding mission directly to Armada.",
            "Refresh the mission and continue into onboarding, backlog, planning, and delivery setup.",
        };

        /// <summary>
        /// Mission statuses after which the handoff step stops polling.
        /// </summary>
        public static IReadOnlyList<MissionStatusEnum> SettledStatuses { get; } = new List<MissionStatusEnum>
        {
            MissionStatusEnum.Complete, MissionStatusEnum.Failed, MissionStatusEnum.Cancelled, MissionStatusEnum.WorkProduced,
            MissionStatusEnum.LandingFailed, MissionStatusEnum.PullRequestOpen,
        };

        /// <summary>
        /// Current step (0-5).
        /// </summary>
        public int Current { get; private set; } = 0;

        /// <summary>
        /// True while existing resources load.
        /// </summary>
        public bool Loading { get; private set; } = true;

        /// <summary>
        /// True while a step action runs.
        /// </summary>
        public bool Busy { get; private set; } = false;

        /// <summary>
        /// Result message of the last action (already translated), or null.
        /// </summary>
        public string? ResultMessage { get; private set; } = null;

        /// <summary>
        /// Kind of <see cref="ResultMessage"/>.
        /// </summary>
        public SetupWizardResultKindEnum ResultKind { get; private set; } = SetupWizardResultKindEnum.Info;

        /// <summary>
        /// Fleets.
        /// </summary>
        public List<Fleet> Fleets { get; private set; } = new List<Fleet>();

        /// <summary>
        /// Vessels.
        /// </summary>
        public List<Vessel> Vessels { get; private set; } = new List<Vessel>();

        /// <summary>
        /// Captains.
        /// </summary>
        public List<Captain> Captains { get; private set; } = new List<Captain>();

        /// <summary>
        /// Fleet step mode.
        /// </summary>
        public SetupWizardModeEnum FleetMode { get; private set; } = SetupWizardModeEnum.New;

        /// <summary>
        /// Vessel step mode.
        /// </summary>
        public SetupWizardModeEnum VesselMode { get; private set; } = SetupWizardModeEnum.New;

        /// <summary>
        /// Captain step mode.
        /// </summary>
        public SetupWizardModeEnum CaptainMode { get; private set; } = SetupWizardModeEnum.New;

        /// <summary>
        /// Existing fleet picker.
        /// </summary>
        public SelectField<string> FleetSelect { get; } = new SelectField<string>();

        /// <summary>
        /// New fleet name.
        /// </summary>
        public InputField FleetName { get; } = new InputField();

        /// <summary>
        /// New fleet description.
        /// </summary>
        public InputField FleetDescription { get; } = new InputField();

        /// <summary>
        /// Existing vessel picker.
        /// </summary>
        public SelectField<string> VesselSelect { get; } = new SelectField<string>();

        /// <summary>
        /// New vessel name.
        /// </summary>
        public InputField VesselName { get; } = new InputField();

        /// <summary>
        /// Default branch.
        /// </summary>
        public InputField DefaultBranch { get; } = new InputField();

        /// <summary>
        /// Repository URL.
        /// </summary>
        public InputField RepoUrl { get; } = new InputField();

        /// <summary>
        /// Working directory.
        /// </summary>
        public InputField WorkingDirectory { get; } = new InputField();

        /// <summary>
        /// Landing mode ("" for the Admiral default).
        /// </summary>
        public SelectField<string> LandingMode { get; } = new SelectField<string>();

        /// <summary>
        /// Enable model context accumulation.
        /// </summary>
        public ToggleField EnableModelContext { get; } = new ToggleField(true, "Enable model context accumulation");

        /// <summary>
        /// Allow concurrent missions.
        /// </summary>
        public ToggleField AllowConcurrentMissions { get; } = new ToggleField(false, "Allow concurrent missions on this vessel");

        /// <summary>
        /// Project context.
        /// </summary>
        public MultilineField ProjectContext { get; } = new MultilineField();

        /// <summary>
        /// Style guide.
        /// </summary>
        public MultilineField StyleGuide { get; } = new MultilineField();

        /// <summary>
        /// Existing idle captain picker.
        /// </summary>
        public SelectField<string> CaptainSelect { get; } = new SelectField<string>();

        /// <summary>
        /// New captain name.
        /// </summary>
        public InputField CaptainName { get; } = new InputField();

        /// <summary>
        /// Runtime ("" when not chosen).
        /// </summary>
        public SelectField<string> Runtime { get; } = new SelectField<string>();

        /// <summary>
        /// Model override.
        /// </summary>
        public InputField Model { get; } = new InputField();

        /// <summary>
        /// Capability tier ("" for not set).
        /// </summary>
        public SelectField<string> Tier { get; } = new SelectField<string>();

        /// <summary>
        /// System instructions.
        /// </summary>
        public MultilineField SystemInstructions { get; } = new MultilineField();

        /// <summary>
        /// Mux config directory.
        /// </summary>
        public InputField MuxConfigDirectory { get; } = new InputField();

        /// <summary>
        /// Mux endpoint name.
        /// </summary>
        public InputField MuxEndpoint { get; } = new InputField();

        /// <summary>
        /// Show the advanced Mux overrides.
        /// </summary>
        public ToggleField MuxAdvanced { get; } = new ToggleField(false, "Advanced Mux Overrides");

        /// <summary>
        /// Mux base URL override.
        /// </summary>
        public InputField MuxBaseUrl { get; } = new InputField();

        /// <summary>
        /// Mux adapter type override.
        /// </summary>
        public InputField MuxAdapterType { get; } = new InputField();

        /// <summary>
        /// Mux temperature override.
        /// </summary>
        public InputField MuxTemperature { get; } = new InputField();

        /// <summary>
        /// Mux max tokens override.
        /// </summary>
        public InputField MuxMaxTokens { get; } = new InputField();

        /// <summary>
        /// Mux system prompt path override.
        /// </summary>
        public InputField MuxSystemPromptPath { get; } = new InputField();

        /// <summary>
        /// Mux approval policy override ("" for default).
        /// </summary>
        public SelectField<string> MuxApprovalPolicy { get; } = new SelectField<string>();

        /// <summary>
        /// Saved Mux endpoints for the config directory.
        /// </summary>
        public List<MuxEndpointInfo> MuxEndpoints { get; private set; } = new List<MuxEndpointInfo>();

        /// <summary>
        /// Mission title.
        /// </summary>
        public InputField MissionTitle { get; } = new InputField();

        /// <summary>
        /// Mission description.
        /// </summary>
        public MultilineField MissionDescription { get; } = new MultilineField();

        /// <summary>
        /// Priority text (invalid becomes 100).
        /// </summary>
        public InputField Priority { get; } = new InputField();

        /// <summary>
        /// Fleet in use, or empty.
        /// </summary>
        public string ActiveFleetId { get; private set; } = "";

        /// <summary>
        /// Vessel in use, or empty.
        /// </summary>
        public string ActiveVesselId { get; private set; } = "";

        /// <summary>
        /// Captain in use, or empty.
        /// </summary>
        public string ActiveCaptainId { get; private set; } = "";

        /// <summary>
        /// The dispatched mission, or null.
        /// </summary>
        public Mission? DispatchedMission { get; private set; } = null;

        /// <summary>
        /// Dispatch warning (no captain could take the mission yet), or empty.
        /// </summary>
        public string DispatchWarning { get; private set; } = "";

        /// <summary>
        /// Vessel readiness for the handoff step, or null.
        /// </summary>
        public VesselReadinessResult? Readiness { get; private set; } = null;

        /// <summary>
        /// Workflow profiles that apply to the vessel.
        /// </summary>
        public List<WorkflowProfile> MatchingProfiles { get; private set; } = new List<WorkflowProfile>();

        /// <summary>
        /// Environments of the vessel.
        /// </summary>
        public List<DeploymentEnvironment> MatchingEnvironments { get; private set; } = new List<DeploymentEnvironment>();

        /// <summary>
        /// Panels by step.
        /// </summary>
        public IReadOnlyList<SetupWizardPanel> Panels
        {
            get { return _Panels; }
        }

        /// <summary>
        /// Navigation buttons (Skip Setup, Back, Next).
        /// </summary>
        public SetupWizardActionBar Navigation { get; } = new SetupWizardActionBar();

        /// <summary>
        /// Sidebar destinations related to the current step (the dashboard highlights these).
        /// </summary>
        public IReadOnlyList<string> Highlights
        {
            get { return HighlightsFor(Current); }
        }

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Tab", "Next field"),
                    new KeyValuePair<string, string>("Ctrl+S", "Run step"),
                    new KeyValuePair<string, string>("Enter", "Press"),
                };
            }
        }

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, string> _LandingModeHints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [""] = "Uses the Admiral-wide landing settings.",
            ["None"] = "Finished work stays on a branch for you to review. Choose Local Merge to land it automatically.",
            ["LocalMerge"] = "Finished work is merged into the working directory and pushed to its origin remote, so the checkout needs one.",
            ["PullRequest"] = "Finished work is pushed and opened as a pull request (needs the GitHub CLI).",
            ["MergeQueue"] = "Finished work is queued; processing the merge queue tests and merges it.",
        };

        private readonly List<SetupWizardPanel> _Panels = new List<SetupWizardPanel>();
        private readonly SetupWizardTextView _HandoffText = new SetupWizardTextView();
        private readonly Button _Skip;
        private readonly Button _Back;
        private readonly Button _Next;
        private Button? _FleetPrimary = null;
        private Button? _VesselPrimary = null;
        private Button? _CaptainPrimary = null;
        private Button? _DispatchPrimary = null;
        private Button? _RefreshMission = null;
        private SetupWizardActionBar _FleetModes = new SetupWizardActionBar();
        private SetupWizardActionBar _VesselModes = new SetupWizardActionBar();
        private SetupWizardActionBar _CaptainModes = new SetupWizardActionBar();
        private FormView? _VesselForm = null;
        private FormView? _CaptainForm = null;
        private bool _NextSetupLoading = false;
        private bool _MuxLoading = false;
        private string _MuxError = "";
        private Timer? _PollTimer = null;
        private int _PollGeneration = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and start loading existing fleets, vessels, and captains.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public SetupWizardScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            InitializeFields();
            _Skip = Navigation.Add("Skip Setup", Finish);
            _Back = Navigation.Add("Back", () => GoTo(Current - 1));
            _Next = Navigation.Add("Start Setup", () => { if (CanAdvance()) GoTo(Current + 1); });
            BuildPanels();
            ShowStep();
            LoadResources();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sidebar destinations the dashboard highlights for a step.
        /// </summary>
        /// <param name="step">Step index.</param>
        /// <returns>Routes.</returns>
        public static IReadOnlyList<string> HighlightsFor(int step)
        {
            switch (step)
            {
                case 1:
                case 2:
                    return new List<string> { "/vessels" };
                case 3:
                    return new List<string> { "/captains" };
                case 4:
                    return new List<string> { "/dispatch" };
                case 5:
                    return new List<string> { "/vessels", "/dispatch", "/planning", "/configuration", "/delivery" };
                default:
                    return new List<string>();
            }
        }

        /// <summary>
        /// The Mux runtime options JSON the dashboard builds (<c>lib/mux.ts</c>), or null for other runtimes.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="configDirectory">Config directory.</param>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="baseUrl">Base URL.</param>
        /// <param name="adapterType">Adapter type.</param>
        /// <param name="temperature">Temperature text.</param>
        /// <param name="maxTokens">Max tokens text.</param>
        /// <param name="systemPromptPath">System prompt path.</param>
        /// <param name="approvalPolicy">Approval policy.</param>
        /// <returns>JSON or null.</returns>
        public static string? BuildMuxRuntimeOptionsJson(string runtime, string configDirectory, string endpoint, string baseUrl, string adapterType, string temperature, string maxTokens, string systemPromptPath, string approvalPolicy)
        {
            if (!String.Equals((runtime ?? "").Trim(), "Mux", StringComparison.Ordinal)) return null;
            MuxCaptainOptions options = new MuxCaptainOptions();
            options.SchemaVersion = 1;
            options.ConfigDirectory = Normalize(configDirectory);
            options.Endpoint = Normalize(endpoint);
            options.BaseUrl = Normalize(baseUrl);
            options.AdapterType = Normalize(adapterType);
            options.Temperature = Double.TryParse((temperature ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t) && !Double.IsNaN(t) && !Double.IsInfinity(t) ? t : (double?)null;
            options.MaxTokens = ParseLeadingInt(maxTokens);
            options.SystemPromptPath = Normalize(systemPromptPath);
            options.ApprovalPolicy = Normalize(approvalPolicy);
            JsonSerializerOptions json = new JsonSerializerOptions();
            json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            return JsonSerializer.Serialize(options, json);
        }

        /// <summary>
        /// Parse the priority field like the dashboard (<c>parseInt</c>; anything else becomes 100).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Priority.</returns>
        public static int ParsePriority(string? text)
        {
            int? value = ParseLeadingInt(text);
            return value ?? 100;
        }

        /// <summary>
        /// True when the Next button is enabled (the step's record is chosen, or the mission is dispatched).
        /// </summary>
        /// <returns>True when Next is allowed.</returns>
        public bool CanAdvance()
        {
            switch (Current)
            {
                case 0: return !Loading;
                case 1: return ActiveFleet() != null;
                case 2: return ActiveVessel() != null;
                case 3: return ActiveCaptain() != null;
                case 4: return DispatchedMission != null;
                default: return true;
            }
        }

        /// <summary>
        /// Go to a step (clears the result message).
        /// </summary>
        /// <param name="index">Step index.</param>
        public void GoTo(int index)
        {
            if (index < 0 || index >= StepTitles.Count) return;
            Current = index;
            ResultMessage = null;
            ShowStep();
            if (Current == StepTitles.Count - 1) EnterHandoff();
            else StopPolling();
        }

        /// <summary>
        /// Switch the fleet step between existing and new.
        /// </summary>
        /// <param name="mode">Mode.</param>
        public void SetFleetMode(SetupWizardModeEnum mode)
        {
            if (mode == SetupWizardModeEnum.Existing && Fleets.Count == 0) return;
            FleetMode = mode;
            RebuildFleetBody();
        }

        /// <summary>
        /// Switch the vessel step between existing and new.
        /// </summary>
        /// <param name="mode">Mode.</param>
        public void SetVesselMode(SetupWizardModeEnum mode)
        {
            if (mode == SetupWizardModeEnum.Existing && Vessels.Count == 0) return;
            VesselMode = mode;
            RebuildVesselBody();
        }

        /// <summary>
        /// Switch the captain step between an existing idle captain and a new one.
        /// </summary>
        /// <param name="mode">Mode.</param>
        public void SetCaptainMode(SetupWizardModeEnum mode)
        {
            if (mode == SetupWizardModeEnum.Existing && IdleCaptains().Count == 0) return;
            CaptainMode = mode;
            RebuildCaptainBody();
        }

        /// <summary>
        /// Idle captains (the only ones offered for reuse).
        /// </summary>
        /// <returns>Captains.</returns>
        public List<Captain> IdleCaptains()
        {
            return Captains.Where(c => c.State == CaptainStateEnum.Idle).ToList();
        }

        /// <summary>
        /// Fleet step: use the chosen fleet or create one.
        /// </summary>
        public void SubmitFleet()
        {
            if (Busy || Loading) return;
            ResultMessage = null;
            if (FleetMode == SetupWizardModeEnum.Existing)
            {
                Fleet? selected = Fleets.FirstOrDefault(f => f.Id == FleetSelect.Value);
                if (selected == null)
                {
                    SetResult(SetupWizardResultKindEnum.Error, L("Choose a fleet before continuing."));
                    return;
                }

                ActiveFleetId = selected.Id;
                SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Using {{entity}} \"{{name}}\".", LocalizationArgs.Of("entity", L("Fleet").ToLowerInvariant(), "name", selected.Name)));
                Advance(2);
                return;
            }

            if (FleetName.Value.Trim().Length == 0)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Fleet name is required."));
                return;
            }

            Fleet payload = new Fleet(FleetName.Value.Trim());
            string description = FleetDescription.Value.Trim();
            payload.Description = description.Length > 0 ? description : null;
            RunStep(() => Context.Client.CreateFleetAsync(payload), fleet =>
            {
                if (fleet == null) return;
                Fleets = Upsert(Fleets, fleet, f => f.Id);
                RefreshFleetOptions();
                FleetSelect.SetValue(fleet.Id);
                ActiveFleetId = fleet.Id;
                FleetMode = SetupWizardModeEnum.Existing;
                RebuildFleetBody();
                SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Created {{entity}} \"{{name}}\".", LocalizationArgs.Of("entity", L("Fleet").ToLowerInvariant(), "name", fleet.Name)));
                Advance(2);
            }, message => Context.Loc.T("{{entity}} creation failed: {{message}}", LocalizationArgs.Of("entity", L("Fleet"), "message", message)));
        }

        /// <summary>
        /// Vessel step: use the chosen vessel or register one.
        /// </summary>
        public void SubmitVessel()
        {
            if (Busy || Loading) return;
            ResultMessage = null;
            if (VesselMode == SetupWizardModeEnum.Existing)
            {
                Vessel? selected = Vessels.FirstOrDefault(v => v.Id == VesselSelect.Value);
                if (selected == null)
                {
                    SetResult(SetupWizardResultKindEnum.Error, L("Choose a vessel before continuing."));
                    return;
                }

                ActiveVesselId = selected.Id;
                if (!String.IsNullOrEmpty(selected.FleetId)) ActiveFleetId = selected.FleetId!;
                SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Using {{entity}} \"{{name}}\".", LocalizationArgs.Of("entity", L("Vessel").ToLowerInvariant(), "name", selected.Name)));
                Advance(3);
                return;
            }

            if (VesselName.Value.Trim().Length == 0)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Vessel name is required."));
                return;
            }

            if (RepoUrl.Value.Trim().Length == 0)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Repository URL is required."));
                return;
            }

            string landing = LandingMode.Value ?? "";
            if (landing == "LocalMerge" && WorkingDirectory.Value.Trim().Length == 0)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Local Merge needs a working directory to merge into."));
                return;
            }

            Vessel payload = new Vessel(VesselName.Value.Trim(), RepoUrl.Value.Trim());
            payload.DefaultBranch = DefaultBranch.Value.Trim().Length > 0 ? DefaultBranch.Value.Trim() : "main";
            payload.EnableModelContext = EnableModelContext.Value;
            payload.AllowConcurrentMissions = AllowConcurrentMissions.Value;
            if (ActiveFleetId.Length > 0) payload.FleetId = ActiveFleetId;
            if (WorkingDirectory.Value.Trim().Length > 0) payload.WorkingDirectory = WorkingDirectory.Value.Trim();
            if (ProjectContext.Value.Trim().Length > 0) payload.ProjectContext = ProjectContext.Value.Trim();
            if (StyleGuide.Value.Trim().Length > 0) payload.StyleGuide = StyleGuide.Value.Trim();
            if (landing.Length > 0 && Enum.TryParse<LandingModeEnum>(landing, out LandingModeEnum mode)) payload.LandingMode = mode;
            RunStep(() => Context.Client.CreateVesselAsync(payload), vessel =>
            {
                if (vessel == null) return;
                Vessels = Upsert(Vessels, vessel, v => v.Id);
                RefreshVesselOptions();
                VesselSelect.SetValue(vessel.Id);
                ActiveVesselId = vessel.Id;
                VesselMode = SetupWizardModeEnum.Existing;
                RebuildVesselBody();
                SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Registered vessel \"{{name}}\".", LocalizationArgs.Of("name", vessel.Name)));
                Advance(3);
            }, message => Context.Loc.T("Vessel registration failed: {{message}}", LocalizationArgs.Of("message", message)));
        }

        /// <summary>
        /// Captain step: use the chosen idle captain or create one.
        /// </summary>
        public void SubmitCaptain()
        {
            if (Busy || Loading) return;
            ResultMessage = null;
            if (CaptainMode == SetupWizardModeEnum.Existing)
            {
                Captain? selected = IdleCaptains().FirstOrDefault(c => c.Id == CaptainSelect.Value);
                if (selected == null)
                {
                    SetResult(SetupWizardResultKindEnum.Error, L("Choose a captain before continuing."));
                    return;
                }

                ActiveCaptainId = selected.Id;
                SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Using {{entity}} \"{{name}}\".", LocalizationArgs.Of("entity", L("Captain").ToLowerInvariant(), "name", selected.Name)));
                Advance(4);
                return;
            }

            if (CaptainName.Value.Trim().Length == 0)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Captain name is required."));
                return;
            }

            string runtime = Runtime.Value ?? "";
            if (runtime.Length == 0 || !Enum.TryParse<AgentRuntimeEnum>(runtime, out AgentRuntimeEnum runtimeEnum))
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Choose a captain runtime."));
                return;
            }

            if (runtime == "Mux" && MuxEndpoint.Value.Trim().Length == 0)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Mux captains require a named Mux endpoint."));
                return;
            }

            Captain payload = new Captain(CaptainName.Value.Trim(), runtimeEnum);
            payload.Model = Model.Value.Trim().Length > 0 ? Model.Value.Trim() : null;
            string tier = Tier.Value ?? "";
            payload.Tier = tier.Length > 0 && Enum.TryParse<CaptainTierEnum>(tier, out CaptainTierEnum tierEnum) ? tierEnum : (CaptainTierEnum?)null;
            payload.SystemInstructions = SystemInstructions.Value.Trim().Length > 0 ? SystemInstructions.Value.Trim() : null;
            payload.RuntimeOptionsJson = BuildMuxRuntimeOptionsJson(runtime, MuxConfigDirectory.Value, MuxEndpoint.Value, MuxBaseUrl.Value, MuxAdapterType.Value, MuxTemperature.Value, MuxMaxTokens.Value, MuxSystemPromptPath.Value, MuxApprovalPolicy.Value ?? "");
            RunStep(() => Context.Client.CreateCaptainAsync(payload), captain =>
            {
                if (captain == null) return;
                Captains = Upsert(Captains, captain, c => c.Id);
                RefreshCaptainOptions();
                CaptainSelect.SetValue(captain.Id);
                ActiveCaptainId = captain.Id;
                CaptainMode = SetupWizardModeEnum.Existing;
                RebuildCaptainBody();
                SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Created {{entity}} \"{{name}}\".", LocalizationArgs.Of("entity", L("Captain").ToLowerInvariant(), "name", captain.Name)));
                Advance(4);
            }, message => Context.Loc.T("{{entity}} creation failed: {{message}}", LocalizationArgs.Of("entity", L("Captain"), "message", message)));
        }

        /// <summary>
        /// Dispatch step: send the onboarding mission through <c>POST /api/v1/missions</c>.
        /// </summary>
        public void SubmitDispatch()
        {
            if (Busy) return;
            ResultMessage = null;
            DispatchWarning = "";
            Vessel? vessel = ActiveVessel();
            if (vessel == null)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Choose or create a vessel before dispatching."));
                return;
            }

            if (ActiveCaptain() == null)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Choose or create a captain before dispatching."));
                return;
            }

            if (MissionTitle.Value.Trim().Length == 0 || MissionDescription.Value.Trim().Length == 0)
            {
                SetResult(SetupWizardResultKindEnum.Error, L("Mission title and description are required."));
                return;
            }

            DispatchRequest request = new DispatchRequest();
            request.VesselId = vessel.Id;
            request.Title = MissionTitle.Value.Trim();
            request.Description = MissionDescription.Value.Trim();
            request.Priority = ParsePriority(Priority.Value);
            RunStep(() => DispatchAsync(request), response =>
            {
                if (response == null || response.Mission == null)
                {
                    SetResult(SetupWizardResultKindEnum.Error, L("Dispatch succeeded but no mission was returned."));
                    return;
                }

                DispatchedMission = response.Mission;
                DispatchWarning = response.Warning ?? "";
                if (DispatchWarning.Length > 0) SetResult(SetupWizardResultKindEnum.Info, DispatchWarning);
                else SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Dispatched mission \"{{title}}\".", LocalizationArgs.Of("title", response.Mission.Title)));
                Advance(5);
            }, message => Context.Loc.T("Dispatch failed: {{message}}", LocalizationArgs.Of("message", message)));
        }

        /// <summary>
        /// Handoff step: reload the dispatched mission.
        /// </summary>
        public void RefreshMission()
        {
            Mission? mission = DispatchedMission;
            if (mission == null || Busy) return;
            RunStep(() => Context.Client.GetMissionAsync(mission.Id), loaded =>
            {
                if (loaded == null) return;
                DispatchedMission = loaded;
                RebuildHandoff();
                SetResult(SetupWizardResultKindEnum.Success, Context.Loc.T("Mission status refreshed: {{status}}.", LocalizationArgs.Of("status", L(loaded.Status.ToString()))));
            }, message => Context.Loc.T("Mission refresh failed: {{message}}", LocalizationArgs.Of("message", message)));
        }

        /// <summary>
        /// Mark setup complete and land on Missions (Skip Setup, Finish Setup).
        /// </summary>
        public void Finish()
        {
            FinishAndNavigate("/missions");
        }

        /// <summary>
        /// Mark setup complete and navigate.
        /// </summary>
        /// <param name="route">Route.</param>
        public void FinishAndNavigate(string route)
        {
            Context.Prefs.Current.SetupCompleted = true;
            Context.Prefs.Save();
            StopPolling();
            Context.Navigate(route);
        }

        /// <summary>
        /// Reload the saved Mux endpoints for the config directory.
        /// </summary>
        public void LoadMuxEndpoints()
        {
            if ((Runtime.Value ?? "") != "Mux") return;
            _MuxLoading = true;
            string dir = MuxConfigDirectory.Value.Trim();
            Task.Run(async () =>
            {
                List<MuxEndpointInfo> endpoints = new List<MuxEndpointInfo>();
                string error = "";
                try
                {
                    MuxEndpointListResult? result = await Context.Client.ListMuxEndpointsAsync(dir.Length > 0 ? dir : null).ConfigureAwait(false);
                    if (result == null || !result.Success)
                    {
                        error = result != null && !String.IsNullOrEmpty(result.ErrorMessage) ? result.ErrorMessage : result != null && !String.IsNullOrEmpty(result.ErrorCode) ? result.ErrorCode : L("Mux endpoint discovery failed.");
                    }
                    else
                    {
                        endpoints = result.Endpoints ?? new List<MuxEndpointInfo>();
                    }
                }
                catch (ArmadaApiException ex)
                {
                    error = ex.Message;
                }

                Context.Dispatcher.Post(() =>
                {
                    _MuxLoading = false;
                    MuxEndpoints = endpoints;
                    _MuxError = error;
                    UpdateMuxHint();
                });
            });
        }

        /// <summary>
        /// Text the Mux endpoint hint shows (dashboard <c>MuxRuntimeFields</c>).
        /// </summary>
        /// <returns>Translated hint, or empty for other runtimes.</returns>
        public string MuxEndpointHint()
        {
            if ((Runtime.Value ?? "") != "Mux") return "";
            if (_MuxLoading) return L("Loading saved Mux endpoints...");
            if (_MuxError.Length > 0) return _MuxError;
            if (MuxEndpoints.Count == 0) return L("No saved Mux endpoints were found for this config directory.");
            return Context.Loc.T("{{count}} saved Mux endpoint(s) available.", LocalizationArgs.Of("count", MuxEndpoints.Count));
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            ArmadaCommand next = new ArmadaCommand("setup.next", "Next", CommandMenuEnum.Actions, () => { if (CanAdvance() && Current < StepTitles.Count - 1) GoTo(Current + 1); });
            next.IsEnabled = () => CanAdvance() && Current < StepTitles.Count - 1;
            ArmadaCommand back = new ArmadaCommand("setup.back", "Back", CommandMenuEnum.Actions, () => GoTo(Current - 1));
            back.IsEnabled = () => Current > 0;
            ArmadaCommand skip = new ArmadaCommand("setup.skip", "Skip Setup", CommandMenuEnum.Actions, Finish);
            return new List<ArmadaCommand> { next, back, skip };
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            StopPolling();
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 6) return;
            UpdateButtons();
            string stepCount = Context.Loc.T("Step {{current}} of {{total}}", LocalizationArgs.Of("current", Current + 1, "total", StepTitles.Count));
            int x = SurfaceText.Draw(surface, 0, 0, T("Setup Wizard") + "  ", Theme.Muted, width);
            SurfaceText.Draw(surface, x, 0, T("Launch Armada With One Mission"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), Math.Max(0, width - x - TextCells.Width(stepCount) - 1));
            SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(stepCount)), 0, stepCount, Theme.Muted, width);

            int px = 0;
            for (int i = 0; i < StepTitles.Count && px < width; i++)
            {
                string marker = i < Current ? "[x]" : i == Current ? "[" + (i + 1) + "]" : " " + (i + 1) + " ";
                string chip = marker + " " + T(StepTitles[i]);
                CellStyle style = i == Current ? Theme.TabActive : i < Current ? Theme.Success : Theme.Muted;
                px += SurfaceText.Draw(surface, px, 1, chip, style, width - px) + 2;
            }

            SurfaceText.Draw(surface, 0, 2, T(StepSummaries[Current]), Theme.Muted, width);
            int y = 3;
            IReadOnlyList<string> highlights = Highlights;
            if (highlights.Count > 0)
            {
                string related = T("Related") + ": " + String.Join(", ", highlights.Select(h => T(NavLabel(h))));
                SurfaceText.Draw(surface, 0, y, related, Theme.Info, width);
            }

            y = 5;
            int navHeight = Math.Max(1, Navigation.HeightFor(width));
            bool loadingLine = Loading && Current == 0;
            int resultLines = (ResultMessage != null ? 1 : 0) + (loadingLine ? 1 : 0);
            int panelHeight = Math.Max(3, height - y - navHeight - resultLines - 1);
            Scope.RenderChild(surface, _Panels[Current], new Rect(0, y, width, panelHeight));
            int ry = y + panelHeight;
            if (loadingLine && ry < height)
            {
                SurfaceText.Draw(surface, 0, ry++, T("Loading existing Armada resources..."), Theme.Info, width);
            }

            if (ResultMessage != null && ry < height)
            {
                CellStyle rs = ResultKind == SetupWizardResultKindEnum.Error ? Theme.Error : ResultKind == SetupWizardResultKindEnum.Success ? Theme.Success : Theme.Info;
                string prefix = ResultKind == SetupWizardResultKindEnum.Error ? "! " : ResultKind == SetupWizardResultKindEnum.Success ? "ok " : "i ";
                SurfaceText.Draw(surface, 0, ry++, prefix + ResultMessage, rs, width);
            }

            int navY = height - navHeight;
            Scope.RenderChild(surface, Navigation, new Rect(0, navY, width, navHeight));
        }

        #endregion

        #region Private-Methods

        private static string NavLabel(string route)
        {
            switch (route)
            {
                case "/vessels": return "Vessels";
                case "/captains": return "Captains";
                case "/dispatch": return "Dispatch";
                case "/planning": return "Planning";
                case "/configuration": return "Configuration";
                case "/delivery": return "Delivery";
                default: return route;
            }
        }

        private static string? Normalize(string? value)
        {
            string trimmed = (value ?? "").Trim();
            return trimmed.Length > 0 ? trimmed : null;
        }

        private static int? ParseLeadingInt(string? text)
        {
            string trimmed = (text ?? "").Trim();
            int end = 0;
            if (end < trimmed.Length && (trimmed[end] == '-' || trimmed[end] == '+')) end++;
            int digitsStart = end;
            while (end < trimmed.Length && Char.IsDigit(trimmed[end])) end++;
            if (end == digitsStart) return null;
            return Int32.TryParse(trimmed.Substring(0, end), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
        }

        private static List<T> Upsert<T>(List<T> items, T item, Func<T, string> id)
        {
            List<T> result = new List<T>();
            bool found = false;
            foreach (T existing in items)
            {
                if (id(existing) == id(item))
                {
                    result.Add(item);
                    found = true;
                }
                else
                {
                    result.Add(existing);
                }
            }

            if (!found) result.Insert(0, item);
            return result;
        }

        private string L(string text)
        {
            return Context.Loc.T(text);
        }

        private Fleet? ActiveFleet()
        {
            return Fleets.FirstOrDefault(f => f.Id == ActiveFleetId);
        }

        private Vessel? ActiveVessel()
        {
            return Vessels.FirstOrDefault(v => v.Id == ActiveVesselId);
        }

        private Captain? ActiveCaptain()
        {
            return Captains.FirstOrDefault(c => c.Id == ActiveCaptainId);
        }

        private void SetResult(SetupWizardResultKindEnum kind, string message)
        {
            ResultKind = kind;
            ResultMessage = message;
        }

        private void Advance(int step)
        {
            SetupWizardResultKindEnum kind = ResultKind;
            string? message = ResultMessage;
            GoTo(step);
            ResultKind = kind;
            ResultMessage = message;
        }

        private void RunStep<TResult>(Func<Task<TResult>> work, Action<TResult> onSuccess, Func<string, string> errorMessage)
        {
            Busy = true;
            Task.Run(async () =>
            {
                try
                {
                    TResult result = await work().ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        Busy = false;
                        onSuccess(result);
                    });
                }
                catch (Exception ex)
                {
                    string message = String.IsNullOrEmpty(ex.Message) ? "Request failed." : ex.Message;
                    Context.Dispatcher.Post(() =>
                    {
                        Busy = false;
                        SetResult(SetupWizardResultKindEnum.Error, errorMessage(message));
                    });
                }
            });
        }

        private async Task<SetupWizardDispatchResponse?> DispatchAsync(DispatchRequest request)
        {
            using (StringContent content = new StringContent(ArmadaJson.Serialize(request), Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await Context.Client.SendRawAsync(HttpMethod.Post, "/api/v1/missions", content).ConfigureAwait(false))
            {
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    SetupWizardErrorBody? error = null;
                    try
                    {
                        error = ArmadaJson.Deserialize<SetupWizardErrorBody>(text);
                    }
                    catch (JsonException)
                    {
                        error = null;
                    }

                    string message = error != null && !String.IsNullOrEmpty(error.Message) ? error.Message! : "HTTP " + (int)response.StatusCode;
                    throw new InvalidOperationException(message);
                }

                SetupWizardDispatchResponse? wrapped = ArmadaJson.Deserialize<SetupWizardDispatchResponse>(text);
                if (wrapped != null && wrapped.Mission != null && !String.IsNullOrEmpty(wrapped.Mission.Title)) return wrapped;
                Mission? mission = ArmadaJson.Deserialize<Mission>(text);
                SetupWizardDispatchResponse plain = new SetupWizardDispatchResponse();
                plain.Mission = mission != null && !String.IsNullOrEmpty(mission.Title) ? mission : null;
                return plain;
            }
        }

        private void InitializeFields()
        {
            IModalHost modals = Context.Modals;
            foreach (SelectField<string> select in new SelectField<string>[] { FleetSelect, VesselSelect, LandingMode, CaptainSelect, Runtime, Tier, MuxApprovalPolicy }) select.ModalHost = modals;
            FleetSelect.PickerTitle = "Fleet";
            VesselSelect.PickerTitle = "Vessel";
            CaptainSelect.PickerTitle = "Captain";
            LandingMode.PickerTitle = "Landing Mode";
            Runtime.PickerTitle = "Runtime";
            Tier.PickerTitle = "Capability Tier";
            MuxApprovalPolicy.PickerTitle = "Mux Approval Policy";
            FleetSelect.Placeholder = "No fleets found";
            VesselSelect.Placeholder = "No vessels found";
            CaptainSelect.Placeholder = "No idle captains found";

            FleetName.Value = L("Armada Starter Fleet");
            FleetDescription.Value = L("Created from the setup wizard.");
            VesselName.Placeholder = "e.g., Armada";
            DefaultBranch.Value = "main";
            DefaultBranch.Placeholder = "main";
            RepoUrl.Placeholder = "https://github.com/org/repo.git or /path/to/repo";
            WorkingDirectory.Placeholder = "Optional local checkout path";
            LandingMode.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("", L("Default")),
                new SelectOption<string>("None", L("None (safest for setup)")),
                new SelectOption<string>("LocalMerge", L("Local Merge")),
                new SelectOption<string>("PullRequest", L("Pull Request")),
                new SelectOption<string>("MergeQueue", L("Merge Queue")),
            };
            LandingMode.SetValue("None");
            LandingMode.ValueChanged += (s, e) => UpdateLandingHint();
            ProjectContext.Placeholder = "Optional architecture, build, or repository notes for captains.";
            StyleGuide.Placeholder = "Optional conventions captains should follow.";
            ProjectContext.ExternalEditor = text => Context.External.EditTextAsync(text, ".md");
            ProjectContext.Dispatcher = Context.Dispatcher;
            StyleGuide.ExternalEditor = text => Context.External.EditTextAsync(text, ".md");
            StyleGuide.Dispatcher = Context.Dispatcher;

            CaptainName.Value = L("Setup Captain");
            Runtime.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("", L("Select runtime...")),
                new SelectOption<string>("ClaudeCode", "Claude Code"),
                new SelectOption<string>("Codex", "Codex"),
                new SelectOption<string>("Gemini", "Gemini"),
                new SelectOption<string>("Cursor", "Cursor"),
                new SelectOption<string>("Mux", "Mux"),
            };
            Runtime.SetValue("ClaudeCode");
            Runtime.ValueChanged += (s, e) =>
            {
                RebuildCaptainBody();
                LoadMuxEndpoints();
            };
            Model.Placeholder = "Optional runtime-specific model override";
            Tier.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("", L("Not set")),
                new SelectOption<string>("Economy", L("Economy")),
                new SelectOption<string>("Standard", L("Standard")),
                new SelectOption<string>("Premium", L("Premium")),
            };
            Tier.SetValue("Standard");
            SystemInstructions.Value = L("For setup missions, prefer read-only repository inspection unless the mission explicitly asks for code changes.");
            SystemInstructions.ExternalEditor = text => Context.External.EditTextAsync(text, ".md");
            SystemInstructions.Dispatcher = Context.Dispatcher;
            MuxConfigDirectory.Placeholder = "Optional path, e.g. C:\\Users\\you\\.mux";
            MuxConfigDirectory.Submitted += (s, e) => LoadMuxEndpoints();
            MuxEndpoint.Placeholder = "Required endpoint name";
            MuxAdvanced.ValueChanged += (s, e) => RebuildCaptainBody();
            MuxBaseUrl.Placeholder = "Optional override";
            MuxAdapterType.Placeholder = "Optional override";
            MuxTemperature.Placeholder = "Optional number";
            MuxMaxTokens.Placeholder = "Optional integer";
            MuxSystemPromptPath.Placeholder = "Optional path";
            MuxApprovalPolicy.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("", L("Default (auto)")),
                new SelectOption<string>("auto", "auto"),
                new SelectOption<string>("autoapprove", "autoapprove"),
                new SelectOption<string>("deny", "deny"),
                new SelectOption<string>("ask", "ask"),
            };
            MuxApprovalPolicy.SetValue("");

            MissionTitle.Value = L("Repository onboarding survey");
            MissionDescription.Value = L("Inspect this repository and report a concise onboarding summary. Do not modify files. Identify the project type, important directories, build/test commands, and one safe follow-up task.");
            MissionDescription.ExternalEditor = text => Context.External.EditTextAsync(text, ".md");
            MissionDescription.Dispatcher = Context.Dispatcher;
            Priority.Value = "100";
        }

        private void BuildPanels()
        {
            SetupWizardPanel welcome = new SetupWizardPanel("Set up Armada by dispatching one first mission");
            SetupWizardTextView welcomeText = new SetupWizardTextView();
            welcomeText.SetLines(new List<SetupWizardLine>
            {
                new SetupWizardLine(L("What this wizard will do"), t => t.Accent),
                new SetupWizardLine(""),
                new SetupWizardLine(L("Pick a fleet"), t => t.Text.WithAttribute(CellAttributes.Bold, true), 2),
                new SetupWizardLine(L("Create or reuse the repository group Armada should organize work under."), t => t.Muted, 4),
                new SetupWizardLine(L("Register a vessel"), t => t.Text.WithAttribute(CellAttributes.Bold, true), 2),
                new SetupWizardLine(L("Provide the target git repository and optional project context, without leaving the wizard."), t => t.Muted, 4),
                new SetupWizardLine(L("Prepare captain capacity"), t => t.Text.WithAttribute(CellAttributes.Bold, true), 2),
                new SetupWizardLine(L("Create or reuse an AI runtime so Armada has a captain available for assignment."), t => t.Muted, 4),
                new SetupWizardLine(L("Dispatch directly"), t => t.Text.WithAttribute(CellAttributes.Bold, true), 2),
                new SetupWizardLine(L("Send a read-only onboarding mission through the dispatch endpoint and monitor the returned mission."), t => t.Muted, 4),
                new SetupWizardLine(L("Hand off into onboarding"), t => t.Text.WithAttribute(CellAttributes.Bold, true), 2),
                new SetupWizardLine(L("From there, continue in Vessel Onboarding, Backlog, Planning, Workspace, Workflow Profiles, Environments, and Checks."), t => t.Muted, 4),
                new SetupWizardLine(""),
                new SetupWizardLine(L("The default mission is intentionally low-risk: it asks the captain to inspect and summarize the repository without modifying files. The wizard stops once Armada can dispatch safely, then hands you into the richer onboarding and delivery surfaces.")),
            });
            welcome.SetBody(welcomeText);
            AddPanel(welcome);

            SetupWizardPanel fleet = new SetupWizardPanel("Choose the fleet for this setup");
            fleet.Intro.Add(new SetupWizardLine(L("Fleets group related repositories. Use an existing fleet if Armada is already configured, or create a starter fleet now.")));
            _FleetModes = ModeBar(() => SetFleetMode(SetupWizardModeEnum.Existing), () => SetFleetMode(SetupWizardModeEnum.New), "Use Existing");
            fleet.SetModeBar(_FleetModes);
            _FleetPrimary = fleet.Actions.Add("Create Fleet", SubmitFleet);
            AddPanel(fleet);

            SetupWizardPanel vessel = new SetupWizardPanel("Register the vessel Armada will dispatch to");
            vessel.Intro.Add(new SetupWizardLine(L("A vessel is a git repository. The setup mission will run against the vessel you choose here.")));
            vessel.ContextLabel = "Fleet";
            _VesselModes = ModeBar(() => SetVesselMode(SetupWizardModeEnum.Existing), () => SetVesselMode(SetupWizardModeEnum.New), "Use Existing");
            vessel.SetModeBar(_VesselModes);
            _VesselPrimary = vessel.Actions.Add("Register Vessel", SubmitVessel);
            AddPanel(vessel);

            SetupWizardPanel captain = new SetupWizardPanel("Prepare a captain for dispatch");
            captain.Intro.Add(new SetupWizardLine(L("A captain is an AI runtime registered with Armada. Direct dispatch assigns work to an available captain, so this step ensures the pool has capacity.")));
            captain.ContextLabel = "Vessel";
            _CaptainModes = ModeBar(() => SetCaptainMode(SetupWizardModeEnum.Existing), () => SetCaptainMode(SetupWizardModeEnum.New), "Use Existing Idle");
            captain.SetModeBar(_CaptainModes);
            _CaptainPrimary = captain.Actions.Add("Create Captain", SubmitCaptain);
            AddPanel(captain);

            SetupWizardPanel dispatch = new SetupWizardPanel("Dispatch the first mission");
            dispatch.Intro.Add(new SetupWizardLine(L("This uses Armada's direct mission dispatch path. It does not create a voyage from the setup wizard.")));
            FormView dispatchForm = new FormView();
            dispatchForm.ShowButtons = false;
            dispatchForm.AddField("Mission Title", MissionTitle);
            dispatchForm.AddField("Mission Description", MissionDescription, null, 7);
            dispatchForm.AddField("Priority", Priority, "Scheduling priority for the mission. Lower values are higher priority in Armada.");
            dispatchForm.SaveRequested += (s, e) => SubmitDispatch();
            dispatch.SetBody(dispatchForm);
            _DispatchPrimary = dispatch.Actions.Add("Dispatch Mission", SubmitDispatch);
            AddPanel(dispatch);

            SetupWizardPanel handoff = new SetupWizardPanel("Mission dispatched, handoff ready");
            handoff.Intro.Add(new SetupWizardLine(L("Armada can dispatch safely now. Use the handoff actions below to move this vessel into onboarding, backlog, planning, workspace, workflow-profile, environment, and first-check setup.")));
            handoff.SetBody(_HandoffText);
            AddPanel(handoff);

            RebuildFleetBody();
            RebuildVesselBody();
            RebuildCaptainBody();
            RebuildHandoff();
        }

        private void AddPanel(SetupWizardPanel panel)
        {
            panel.Localizer = Localizer;
            panel.ApplyTheme(Theme);
            _Panels.Add(panel);
        }

        private SetupWizardActionBar ModeBar(Action existing, Action created, string existingLabel)
        {
            SetupWizardActionBar bar = new SetupWizardActionBar();
            bar.Add(existingLabel, existing);
            bar.Add("Create New", created);
            return bar;
        }

        private void ShowStep()
        {
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            AddChild(_Panels[Current]);
            AddChild(Navigation);
            _Panels[Current].FocusBody();
            Scope.Focus(_Panels[Current]);
            if (active) Scope.SetActive(true);
        }

        private void UpdateButtons()
        {
            _Back.Visible = Current > 0;
            _Next.Visible = Current < StepTitles.Count - 1;
            _Next.Label = Current == 0 ? "Start Setup" : "Next";
            _Next.Enabled = CanAdvance();
            MarkMode(_FleetModes, FleetMode, Fleets.Count > 0);
            MarkMode(_VesselModes, VesselMode, Vessels.Count > 0);
            MarkMode(_CaptainModes, CaptainMode, IdleCaptains().Count > 0);
            if (_FleetPrimary != null)
            {
                _FleetPrimary.Label = Busy ? "Saving..." : FleetMode == SetupWizardModeEnum.Existing ? "Use Fleet" : "Create Fleet";
                _FleetPrimary.Enabled = !Busy && !Loading;
            }

            if (_VesselPrimary != null)
            {
                _VesselPrimary.Label = Busy ? "Saving..." : VesselMode == SetupWizardModeEnum.Existing ? "Use Vessel" : "Register Vessel";
                _VesselPrimary.Enabled = !Busy && !Loading;
            }

            if (_CaptainPrimary != null)
            {
                _CaptainPrimary.Label = Busy ? "Saving..." : CaptainMode == SetupWizardModeEnum.Existing ? "Use Captain" : "Create Captain";
                _CaptainPrimary.Enabled = !Busy && !Loading;
            }

            if (_DispatchPrimary != null)
            {
                _DispatchPrimary.Label = Busy ? "Dispatching..." : "Dispatch Mission";
                _DispatchPrimary.Enabled = !Busy && ActiveVessel() != null && ActiveCaptain() != null;
            }

            if (_RefreshMission != null)
            {
                _RefreshMission.Label = Busy ? "Refreshing..." : "Refresh Mission Status";
                _RefreshMission.Enabled = !Busy && DispatchedMission != null;
            }

            _Panels[2].ContextValue = ActiveFleet()?.Name ?? L("No fleet selected");
            _Panels[3].ContextValue = ActiveVessel()?.Name ?? L("No vessel selected");
            _Panels[4].Intro.Clear();
            _Panels[4].Intro.Add(new SetupWizardLine(L("This uses Armada's direct mission dispatch path. It does not create a voyage from the setup wizard.")));
            _Panels[4].Intro.Add(new SetupWizardLine(L("Fleet") + ": " + (ActiveFleet()?.Name ?? "-") + "    " + L("Vessel") + ": " + (ActiveVessel()?.Name ?? "-") + "    " + L("Available Captain") + ": " + (ActiveCaptain()?.Name ?? "-"), t => t.Text));
        }

        private static void MarkMode(SetupWizardActionBar bar, SetupWizardModeEnum mode, bool hasExisting)
        {
            if (bar.Buttons.Count < 2) return;
            bar.Buttons[0].Enabled = hasExisting;
            bar.Buttons[0].Hint = mode == SetupWizardModeEnum.Existing ? "*" : null;
            bar.Buttons[1].Hint = mode == SetupWizardModeEnum.New ? "*" : null;
        }

        private void RebuildFleetBody()
        {
            FormView form = new FormView();
            form.ShowButtons = false;
            if (FleetMode == SetupWizardModeEnum.Existing)
            {
                form.AddField("Fleet", FleetSelect);
            }
            else
            {
                form.AddField("Fleet Name", FleetName);
                form.AddField("Description", FleetDescription);
            }

            form.SaveRequested += (s, e) => SubmitFleet();
            _Panels[1].SetBody(form);
        }

        private void RebuildVesselBody()
        {
            FormView form = new FormView();
            form.ShowButtons = false;
            if (VesselMode == SetupWizardModeEnum.Existing)
            {
                form.AddField("Vessel", VesselSelect);
            }
            else
            {
                form.AddField("Vessel Name", VesselName);
                form.AddField("Default Branch", DefaultBranch);
                form.AddField("Repository URL", RepoUrl);
                form.AddField("Working Directory", WorkingDirectory);
                form.AddField("Landing Mode", LandingMode, LandingHint());
                form.AddField("Model Context", EnableModelContext);
                form.AddField("Concurrency", AllowConcurrentMissions);
                form.AddField("Project Context", ProjectContext, null, 4);
                form.AddField("Style Guide", StyleGuide, null, 4);
            }

            form.SaveRequested += (s, e) => SubmitVessel();
            _VesselForm = form;
            _Panels[2].SetBody(form);
        }

        private void RebuildCaptainBody()
        {
            FormView form = new FormView();
            form.ShowButtons = false;
            bool mux = (Runtime.Value ?? "") == "Mux";
            if (CaptainMode == SetupWizardModeEnum.Existing)
            {
                form.AddField("Captain", CaptainSelect);
            }
            else
            {
                form.AddField("Captain Name", CaptainName);
                form.AddField("Runtime", Runtime);
                form.AddField("Model", Model);
                form.AddField("Capability Tier", Tier);
                form.AddField("System Instructions", SystemInstructions, null, 4);
                if (mux)
                {
                    form.AddField("Mux Config Directory", MuxConfigDirectory, "Optional mux config directory override. Leave blank to use mux defaults.");
                    form.AddField("Mux Endpoint", MuxEndpoint, MuxEndpointHint());
                    form.AddField("Advanced", MuxAdvanced);
                    if (MuxAdvanced.Value)
                    {
                        form.AddField("Mux Base URL", MuxBaseUrl);
                        form.AddField("Mux Adapter Type", MuxAdapterType);
                        form.AddField("Mux Temperature", MuxTemperature);
                        form.AddField("Mux Max Tokens", MuxMaxTokens);
                        form.AddField("Mux System Prompt Path", MuxSystemPromptPath);
                        form.AddField("Mux Approval Policy", MuxApprovalPolicy);
                    }
                }
            }

            form.SaveRequested += (s, e) => SubmitCaptain();
            _CaptainForm = form;
            if (_Panels.Count < 4) return;
            SetupWizardPanel panel = _Panels[3];
            panel.Actions.Clear();
            _CaptainPrimary = panel.Actions.Add("Create Captain", SubmitCaptain);
            if (mux && CaptainMode == SetupWizardModeEnum.New)
            {
                panel.Actions.Add("Refresh Mux Endpoints", LoadMuxEndpoints);
                panel.Actions.Add("Choose Mux Endpoint", ChooseMuxEndpoint);
            }

            panel.SetBody(form);
        }

        private void ChooseMuxEndpoint()
        {
            if (MuxEndpoints.Count == 0)
            {
                SetResult(SetupWizardResultKindEnum.Info, MuxEndpointHint());
                return;
            }

            List<SelectOption<string>> options = MuxEndpoints.Select(e => new SelectOption<string>(e.Name, e.Name, e.AdapterType + (String.IsNullOrEmpty(e.Model) ? "" : " (" + e.Model + ")"))).ToList();
            PickerModal<string> picker = new PickerModal<string>("Mux Endpoint", options, Context.Loc, Context.Theme.Current);
            Context.Modals.Show(picker, result =>
            {
                if (result is SelectOption<string> chosen) MuxEndpoint.Value = chosen.Value;
            });
        }

        private string LandingHint()
        {
            string key = LandingMode.Value ?? "";
            return _LandingModeHints.TryGetValue(key, out string? hint) ? hint : _LandingModeHints[""];
        }

        private void UpdateLandingHint()
        {
            if (_VesselForm == null) return;
            FormRow? row = _VesselForm.Rows.FirstOrDefault(r => ReferenceEquals(r.Field, LandingMode));
            if (row != null) row.Hint = LandingHint();
        }

        private void UpdateMuxHint()
        {
            if (_CaptainForm == null) return;
            FormRow? row = _CaptainForm.Rows.FirstOrDefault(r => ReferenceEquals(r.Field, MuxEndpoint));
            if (row != null) row.Hint = MuxEndpointHint();
        }

        private void RefreshFleetOptions()
        {
            string? current = FleetSelect.Value;
            FleetSelect.Options = Fleets.Select(f => new SelectOption<string>(f.Id, f.Name)).ToList();
            FleetSelect.SetValue(current);
        }

        private void RefreshVesselOptions()
        {
            string? current = VesselSelect.Value;
            VesselSelect.Options = Vessels.Select(v => new SelectOption<string>(v.Id, v.Name + " (" + (String.IsNullOrEmpty(v.DefaultBranch) ? "main" : v.DefaultBranch) + ")")).ToList();
            VesselSelect.SetValue(current);
        }

        private void RefreshCaptainOptions()
        {
            string? current = CaptainSelect.Value;
            CaptainSelect.Options = IdleCaptains().Select(c => new SelectOption<string>(c.Id, c.Name + " (" + c.Runtime + ", " + c.State + ")")).ToList();
            CaptainSelect.SetValue(current);
        }

        private void LoadResources()
        {
            Loading = true;
            Task.Run(async () =>
            {
                try
                {
                    EnumerationResult<Fleet>? fleets = await Context.Client.ListFleetsAsync(new ArmadaPageQuery(1, 9999)).ConfigureAwait(false);
                    EnumerationResult<Vessel>? vessels = await Context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 9999)).ConfigureAwait(false);
                    EnumerationResult<Captain>? captains = await Context.Client.ListCaptainsAsync(new ArmadaPageQuery(1, 9999)).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        Fleets = fleets?.Objects ?? new List<Fleet>();
                        Vessels = vessels?.Objects ?? new List<Vessel>();
                        Captains = captains?.Objects ?? new List<Captain>();
                        RefreshFleetOptions();
                        RefreshVesselOptions();
                        RefreshCaptainOptions();
                        if (Fleets.Count > 0)
                        {
                            FleetMode = SetupWizardModeEnum.Existing;
                            if (String.IsNullOrEmpty(FleetSelect.Value)) FleetSelect.SetValue(Fleets[0].Id);
                        }

                        if (Vessels.Count > 0)
                        {
                            VesselMode = SetupWizardModeEnum.Existing;
                            if (String.IsNullOrEmpty(VesselSelect.Value)) VesselSelect.SetValue(Vessels[0].Id);
                        }

                        List<Captain> idle = IdleCaptains();
                        if (idle.Count > 0)
                        {
                            CaptainMode = SetupWizardModeEnum.Existing;
                            if (String.IsNullOrEmpty(CaptainSelect.Value)) CaptainSelect.SetValue(idle[0].Id);
                        }

                        RebuildFleetBody();
                        RebuildVesselBody();
                        RebuildCaptainBody();
                        Loading = false;
                    });
                }
                catch (Exception ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        SetResult(SetupWizardResultKindEnum.Error, Context.Loc.T("Unable to load existing Armada resources: {{message}}", LocalizationArgs.Of("message", ex.Message)));
                        Loading = false;
                    });
                }
            });
        }

        private void EnterHandoff()
        {
            RebuildHandoff();
            if (ActiveVesselId.Length > 0) LoadNextSetupState();
            StartPolling();
        }

        private void LoadNextSetupState()
        {
            string vesselId = ActiveVesselId;
            string fleetId = ActiveFleetId;
            _NextSetupLoading = true;
            RebuildHandoff();
            Task.Run(async () =>
            {
                VesselReadinessResult? readiness = null;
                List<WorkflowProfile> profiles = new List<WorkflowProfile>();
                List<DeploymentEnvironment> environments = new List<DeploymentEnvironment>();
                try
                {
                    readiness = await Context.Client.GetVesselReadinessAsync(vesselId).ConfigureAwait(false);
                    EnumerationResult<WorkflowProfile>? profileResult = await Context.Client.ListWorkflowProfilesAsync(new ArmadaPageQuery(1, 9999)).ConfigureAwait(false);
                    DeploymentEnvironmentQuery envQuery = new DeploymentEnvironmentQuery();
                    envQuery.PageNumber = 1;
                    envQuery.PageSize = 9999;
                    envQuery.VesselId = vesselId;
                    EnumerationResult<DeploymentEnvironment>? envResult = await Context.Client.ListEnvironmentsAsync(envQuery).ConfigureAwait(false);
                    profiles = (profileResult?.Objects ?? new List<WorkflowProfile>()).Where(p =>
                        p.Scope == WorkflowProfileScopeEnum.Global
                        || (p.Scope == WorkflowProfileScopeEnum.Fleet && fleetId.Length > 0 && p.FleetId == fleetId)
                        || (p.Scope == WorkflowProfileScopeEnum.Vessel && p.VesselId == vesselId)).ToList();
                    environments = envResult?.Objects ?? new List<DeploymentEnvironment>();
                }
                catch (Exception)
                {
                    readiness = null;
                    profiles = new List<WorkflowProfile>();
                    environments = new List<DeploymentEnvironment>();
                }

                Context.Dispatcher.Post(() =>
                {
                    Readiness = readiness;
                    MatchingProfiles = profiles;
                    MatchingEnvironments = environments;
                    _NextSetupLoading = false;
                    RebuildHandoff();
                });
            });
        }

        private void StartPolling()
        {
            StopPolling();
            int generation = ++_PollGeneration;
            _PollTimer = new Timer(_ => Context.Dispatcher.Post(() => PollTick(generation)), null, 5000, 5000);
        }

        private void StopPolling()
        {
            _PollGeneration++;
            Timer? timer = _PollTimer;
            _PollTimer = null;
            timer?.Dispose();
        }

        private void PollTick(int generation)
        {
            if (generation != _PollGeneration || Current != StepTitles.Count - 1) return;
            Mission? mission = DispatchedMission;
            if (mission == null || SettledStatuses.Contains(mission.Status)) return;
            ScreenOps.Quiet(Context, () => Context.Client.GetMissionAsync(mission.Id), loaded =>
            {
                if (loaded == null || generation != _PollGeneration) return;
                DispatchedMission = loaded;
                RebuildHandoff();
            });
        }

        private string HandoffBacklogPath()
        {
            Vessel? vessel = ActiveVessel();
            string fleetId = ActiveFleetId.Length > 0 ? ActiveFleetId : vessel?.FleetId ?? "";
            List<string> parts = new List<string> { "tab=backlog" };
            if (fleetId.Length > 0) parts.Add("fleetId=" + Uri.EscapeDataString(fleetId));
            if (ActiveVesselId.Length > 0) parts.Add("vesselId=" + Uri.EscapeDataString(ActiveVesselId));
            return "/dispatch?" + String.Join("&", parts);
        }

        private void RebuildHandoff()
        {
            List<SetupWizardLine> lines = new List<SetupWizardLine>();
            if (DispatchWarning.Length > 0)
            {
                lines.Add(new SetupWizardLine(DispatchWarning, t => t.Info));
                lines.Add(new SetupWizardLine(""));
            }

            Mission? mission = DispatchedMission;
            if (mission != null)
            {
                lines.Add(new SetupWizardLine(L("Mission") + ": " + mission.Title));
                lines.Add(new SetupWizardLine(L("Mission ID") + ": " + IdShort(mission.Id)));
                lines.Add(new SetupWizardLine(L("Status") + ": " + L(mission.Status.ToString()), t => StatusBadge.Style(mission.Status.ToString(), t)));
                lines.Add(new SetupWizardLine(L("Captain ID") + ": " + IdShort(mission.CaptainId)));
                lines.Add(new SetupWizardLine(L("Vessel ID") + ": " + IdShort(mission.VesselId)));
                lines.Add(new SetupWizardLine(L("Branch") + ": " + (String.IsNullOrEmpty(mission.BranchName) ? "-" : mission.BranchName)));
            }
            else
            {
                lines.Add(new SetupWizardLine(L("No mission has been dispatched yet."), t => t.Muted));
            }

            lines.Add(new SetupWizardLine(""));
            string loading = L("Loading...");
            lines.Add(new SetupWizardLine(L("Readiness") + ": " + (_NextSetupLoading ? loading : Readiness != null ? Readiness.SetupChecklistSatisfiedCount + "/" + Readiness.SetupChecklistTotalCount : "-")));
            lines.Add(new SetupWizardLine(L("Blocking Issues") + ": " + (_NextSetupLoading ? loading : Readiness != null ? Readiness.ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")));
            lines.Add(new SetupWizardLine(L("Workflow Profiles") + ": " + (_NextSetupLoading ? loading : MatchingProfiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            lines.Add(new SetupWizardLine(L("Environments") + ": " + (_NextSetupLoading ? loading : MatchingEnvironments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            VesselSetupChecklistItem? next = Readiness?.SetupChecklist?.FirstOrDefault(i => !i.IsSatisfied);
            if (next != null)
            {
                lines.Add(new SetupWizardLine(""));
                lines.Add(new SetupWizardLine(L("Next Recommended Step"), t => t.Accent));
                lines.Add(new SetupWizardLine(next.Title, t => t.Text.WithAttribute(CellAttributes.Bold, true), 2));
                lines.Add(new SetupWizardLine(next.Message, t => t.Muted, 2));
            }

            lines.Add(new SetupWizardLine(""));
            string[][] guide = new string[][]
            {
                new string[] { "Vessel Onboarding", "Review the readiness checklist, fix blocking issues, and follow the recommended onboarding actions for this vessel." },
                new string[] { "Backlog", "Capture the follow-up work uncovered by the onboarding mission and keep it attached to this vessel." },
                new string[] { "Planning", "Turn one of those follow-up tasks into a captain-backed execution plan before you dispatch code-changing work." },
                new string[] { "Workspace", "Inspect the repository directly, review the onboarding findings, and continue setup or debugging without leaving Armada." },
                new string[] { "Workflow Profiles", "Teach Armada how this repository builds, tests, deploys, rolls back, and verifies itself." },
                new string[] { "Environments", "Capture at least one named rollout target with approval, verification, and monitoring metadata." },
                new string[] { "Checks", "Run the first structured check once readiness and profile setup are in place." },
                new string[] { "Playbooks", "Optional: capture reusable dispatch and planning guidance if your team wants consistent execution patterns." },
            };
            foreach (string[] item in guide)
            {
                lines.Add(new SetupWizardLine(L(item[0]), t => t.Text.WithAttribute(CellAttributes.Bold, true), 2));
                lines.Add(new SetupWizardLine(L(item[1]), t => t.Muted, 4));
            }

            _HandoffText.SetLines(lines);
            RebuildHandoffActions();
        }

        private void RebuildHandoffActions()
        {
            if (_Panels.Count < 6) return;
            SetupWizardPanel panel = _Panels[5];
            bool hadFocus = panel.Scope.Focused == panel.Actions;
            int focusedButton = -1;
            for (int i = 0; i < panel.Actions.Buttons.Count; i++)
            {
                if (ReferenceEquals(panel.Actions.Scope.Focused, panel.Actions.Buttons[i])) focusedButton = i;
            }

            panel.Actions.Clear();
            _RefreshMission = panel.Actions.Add("Refresh Mission Status", RefreshMission);
            Mission? mission = DispatchedMission;
            Vessel? vessel = ActiveVessel();
            if (mission != null) panel.Actions.Add("Open Mission", () => FinishAndNavigate("/missions/" + mission.Id));
            if (vessel != null)
            {
                string vesselId = vessel.Id;
                string fleetId = ActiveFleetId.Length > 0 ? ActiveFleetId : vessel.FleetId ?? "";
                panel.Actions.Add("Open Vessel Onboarding", () => FinishAndNavigate("/vessels/" + vesselId + "/onboarding"));
                panel.Actions.Add("Open Backlog", () => FinishAndNavigate(HandoffBacklogPath()));
                panel.Actions.Add("Open Planning", () => FinishAndNavigate("/planning?fromSetupWizard=true&vesselId=" + Uri.EscapeDataString(vesselId) + (fleetId.Length > 0 ? "&fleetId=" + Uri.EscapeDataString(fleetId) : "")));
                panel.Actions.Add("Open Workspace", () => FinishAndNavigate("/workspace/" + vesselId));
                bool hasProfiles = MatchingProfiles.Count > 0;
                panel.Actions.Add(hasProfiles ? "Open Workflow Profiles" : "Create Workflow Profile", () => FinishAndNavigate(hasProfiles ? "/configuration?tab=workflow-profiles" : "/workflow-profiles/new?scope=Vessel&vesselId=" + Uri.EscapeDataString(vesselId)));
                bool hasEnvironments = MatchingEnvironments.Count > 0;
                panel.Actions.Add(hasEnvironments ? "Open Environments" : "Create Environment", () => FinishAndNavigate(hasEnvironments ? "/delivery?tab=environments" : "/environments/new?vesselId=" + Uri.EscapeDataString(vesselId) + "&kind=Development"));
                string branch = vessel.DefaultBranch ?? "";
                panel.Actions.Add("Run First Check", () => FinishAndNavigate("/delivery?tab=checks&vesselId=" + Uri.EscapeDataString(vesselId) + "&branchName=" + Uri.EscapeDataString(branch)));
            }

            panel.Actions.Add("Open Playbooks", () => FinishAndNavigate("/configuration?tab=playbooks"));
            panel.Actions.Add("Finish Setup", Finish);
            if (hadFocus && focusedButton >= 0 && focusedButton < panel.Actions.Buttons.Count) panel.Actions.Scope.Focus(panel.Actions.Buttons[focusedButton]);
        }

        private static string IdShort(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            return id.Length > 12 ? id.Substring(0, 12) : id;
        }

        #endregion
    }
}
