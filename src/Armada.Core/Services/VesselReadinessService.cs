namespace Armada.Core.Services
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Computes vessel readiness and workflow preflight warnings for planning, dispatch, and checks. The checkout is
    /// inspected where it lives (see <see cref="VesselHostResolver"/>): the working directory on the Admiral host, or a
    /// checkout on a connected Harbor, whose git state, toolchains, and command dependencies are probed on that Harbor.
    /// </summary>
    public class VesselReadinessService
    {
        /// <summary>
        /// Finds where a vessel's checkout lives. When null, only the working directory on the Admiral host is considered.
        /// </summary>
        public VesselHostResolver? Hosts { get; set; } = null;

        private static readonly Regex _CommandSegmentSplit = new Regex(@"\s*(?:&&|\|\||;|\r?\n)\s*", RegexOptions.Compiled);
        private static readonly Regex _TokenRegex = new Regex("^\\s*(?:\"([^\"]+)\"|'([^']+)'|([^\\s]+))", RegexOptions.Compiled);
        private static readonly HashSet<string> _ShellBuiltins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cd", "echo", "set", "export", "if", "then", "fi", "for", "do", "done", "call", "rem", "@echo", "true", "false", "type"
        };
        private static readonly HashSet<string> _TerminalShellBuiltins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "echo", "@echo", "rem", "true", "false", "type"
        };
        private static readonly Dictionary<string, string[]> _VersionProbeArgs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "dotnet", new[] { "--version" } },
            { "node", new[] { "--version" } },
            { "npm", new[] { "--version" } },
            { "pnpm", new[] { "--version" } },
            { "yarn", new[] { "--version" } },
            { "python", new[] { "--version" } },
            { "docker", new[] { "--version" } },
            { "cargo", new[] { "--version" } },
            { "go", new[] { "version" } },
            { "java", new[] { "-version" } },
            { "mvn", new[] { "-v" } },
            { "gradle", new[] { "-v" } }
        };

        private readonly DatabaseDriver _Database;
        private readonly WorkflowProfileService _WorkflowProfiles;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselReadinessService(DatabaseDriver database, WorkflowProfileService workflowProfiles, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _WorkflowProfiles = workflowProfiles ?? throw new ArgumentNullException(nameof(workflowProfiles));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Evaluate vessel readiness for general use or for a specific check run. The checkout is found with
        /// <see cref="Hosts"/> when set (for the calling user's Harbors), otherwise on the Admiral host only.
        /// </summary>
        public async Task<VesselReadinessResult> EvaluateAsync(
            AuthContext auth,
            Vessel vessel,
            string? explicitWorkflowProfileId = null,
            CheckRunTypeEnum? requestedCheckType = null,
            string? requestedEnvironmentName = null,
            bool includeWorkflowRequirements = true,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            VesselHostResolution? resolution = null;
            if (Hosts != null && !VesselHostResolver.HasAdmiralWorkingDirectory(vessel))
                resolution = await Hosts.TryResolveAsync(vessel, auth.UserId, token).ConfigureAwait(false);

            return await EvaluateCoreAsync(auth, vessel, resolution, explicitWorkflowProfileId, requestedCheckType, requestedEnvironmentName, includeWorkflowRequirements, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Evaluate vessel readiness on a checkout already found (for example the one a check run is about to use).
        /// </summary>
        public Task<VesselReadinessResult> EvaluateAsync(
            AuthContext auth,
            Vessel vessel,
            VesselHost host,
            string? explicitWorkflowProfileId = null,
            CheckRunTypeEnum? requestedCheckType = null,
            string? requestedEnvironmentName = null,
            bool includeWorkflowRequirements = true,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (host == null) throw new ArgumentNullException(nameof(host));
            return EvaluateCoreAsync(auth, vessel, VesselHostResolution.Found(host), explicitWorkflowProfileId, requestedCheckType, requestedEnvironmentName, includeWorkflowRequirements, token);
        }

        private async Task<VesselReadinessResult> EvaluateCoreAsync(
            AuthContext auth,
            Vessel vessel,
            VesselHostResolution? resolution,
            string? explicitWorkflowProfileId,
            CheckRunTypeEnum? requestedCheckType,
            string? requestedEnvironmentName,
            bool includeWorkflowRequirements,
            CancellationToken token)
        {
            VesselReadinessResult result = new VesselReadinessResult
            {
                VesselId = vessel.Id,
                RequestedCheckType = requestedCheckType,
                RequestedEnvironmentName = requestedEnvironmentName
            };

            bool checkSpecific = requestedCheckType.HasValue;
            VesselHost? harborHost = resolution?.Host != null && resolution.Host.IsHarbor ? resolution.Host : null;

            bool hasWorkingDirectory = !String.IsNullOrWhiteSpace(vessel.WorkingDirectory)
                && Directory.Exists(vessel.WorkingDirectory);
            if (resolution?.Host != null && !resolution.Host.IsHarbor) hasWorkingDirectory = true;
            result.HasWorkingDirectory = hasWorkingDirectory || harborHost != null;
            if (hasWorkingDirectory && harborHost == null) result.CheckoutPath = resolution?.Host?.WorkingDirectory ?? vessel.WorkingDirectory;

            if (harborHost != null)
            {
                result.CheckoutPath = harborHost.WorkingDirectory;
                result.HarborId = harborHost.HarborId;
                result.HarborName = harborHost.HarborName;
                AddIssue(
                    result,
                    "working_directory_on_harbor",
                    ReadinessSeverityEnum.Info,
                    "Checkout on Harbor " + harborHost.HarborName,
                    "Checks, Workspace, and readiness probes run in the checkout " + harborHost.WorkingDirectory + " on Harbor " + harborHost.HarborName
                        + (String.IsNullOrWhiteSpace(vessel.WorkingDirectory)
                            ? " (the vessel has no working directory on the Admiral host)."
                            : " (the working directory " + vessel.WorkingDirectory + " does not exist on the Admiral host)."),
                    harborHost.WorkingDirectory);
                await PopulateHarborCheckoutAsync(result, harborHost, vessel.DefaultBranch, token).ConfigureAwait(false);
            }
            else if (!hasWorkingDirectory)
            {
                result.CheckoutErrorCode = resolution?.ErrorCode;
                AddIssue(
                    result,
                    "working_directory_missing",
                    checkSpecific ? ReadinessSeverityEnum.Error : ReadinessSeverityEnum.Warning,
                    "Working directory unavailable",
                    resolution?.Message ?? "This vessel does not have a configured working directory that exists on disk.",
                    vessel.WorkingDirectory);
            }
            else if (!LooksLikeGitWorkingTree(vessel.WorkingDirectory!))
            {
                AddIssue(
                    result,
                    "working_directory_not_git",
                    ReadinessSeverityEnum.Warning,
                    "Working directory is not a Git checkout",
                    "The configured working directory exists, but it does not appear to be a Git working tree.",
                    vessel.WorkingDirectory);
            }
            else
            {
                await PopulateRepositoryStateAsync(result, new LocalHostCommandExecutor(), vessel.WorkingDirectory!, vessel.DefaultBranch).ConfigureAwait(false);
                result.ToolchainProbes = DetectToolchains(vessel.WorkingDirectory!);
                result.DetectedToolchains = result.ToolchainProbes
                    .Where(probe => probe.Available)
                    .Select(probe => probe.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (VesselToolchainProbe probe in result.ToolchainProbes.Where(item => item.Expected && !item.Available))
                {
                    AddIssue(
                        result,
                        "expected_toolchain_missing",
                        ReadinessSeverityEnum.Warning,
                        "Expected toolchain missing",
                        "The repository appears to expect the '" + probe.Name + "' toolchain, but Armada could not find it on the host.",
                        probe.Evidence ?? probe.Name);
                }
            }

            bool bareRepoExists = !String.IsNullOrWhiteSpace(vessel.LocalPath) && Directory.Exists(vessel.LocalPath);
            bool hasRepoUrl = !String.IsNullOrWhiteSpace(vessel.RepoUrl);
            result.HasRepositoryContext = bareRepoExists || hasRepoUrl || harborHost != null;
            if (harborHost != null)
            {
                // The repository is the Harbor's checkout; nothing on the Admiral host is needed.
            }
            else if (!bareRepoExists && !hasRepoUrl)
            {
                AddIssue(
                    result,
                    "repository_context_missing",
                    ReadinessSeverityEnum.Error,
                    "Repository context unavailable",
                    "Armada has neither a usable bare repository path nor a RepoUrl from which it can recover one.",
                    vessel.LocalPath);
            }
            else if (!bareRepoExists && hasRepoUrl)
            {
                AddIssue(
                    result,
                    "repository_clone_missing",
                    ReadinessSeverityEnum.Warning,
                    "Bare repository will need to be reprovisioned",
                    "The configured bare repository path does not exist. Armada can usually recover it from RepoUrl on the next dock provisioning run.",
                    vessel.LocalPath);
            }

            if (String.IsNullOrWhiteSpace(vessel.DefaultBranch))
            {
                AddIssue(
                    result,
                    "default_branch_missing",
                    ReadinessSeverityEnum.Warning,
                    "Default branch missing",
                    "This vessel does not declare a default branch. Armada will fall back to main in some flows, but that may be incorrect.",
                    null);
            }

            if (!includeWorkflowRequirements)
            {
                FinalizeResult(result);
                return result;
            }

            WorkflowProfile? profile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, explicitWorkflowProfileId, token).ConfigureAwait(false);
            if (profile == null)
            {
                AddIssue(
                    result,
                    "workflow_profile_missing",
                    checkSpecific ? ReadinessSeverityEnum.Error : ReadinessSeverityEnum.Warning,
                    "Workflow profile not resolved",
                    checkSpecific
                        ? "No active workflow profile could be resolved for the requested vessel and check."
                        : "No active workflow profile is currently resolved for this vessel.",
                    explicitWorkflowProfileId);

                FinalizeResult(result);
                return result;
            }

            result.WorkflowProfileId = profile.Id;
            result.WorkflowProfileName = profile.Name;
            result.WorkflowProfileScope = profile.Scope;
            result.DeploymentEnvironments = profile.Environments
                .Where(environment => !String.IsNullOrWhiteSpace(environment.EnvironmentName))
                .Select(environment => environment.EnvironmentName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.DeploymentMetadata = BuildDeploymentMetadata(profile);

            WorkflowProfileValidationResult validation = await _WorkflowProfiles.ValidateAsync(profile, token).ConfigureAwait(false);
            result.AvailableCheckTypes = validation.AvailableCheckTypes ?? new List<string>();

            foreach (string warning in validation.Warnings)
            {
                AddIssue(result, "workflow_profile_warning", ReadinessSeverityEnum.Warning, "Workflow profile warning", warning, profile.Name);
            }

            foreach (string error in validation.Errors)
            {
                AddIssue(
                    result,
                    "workflow_profile_invalid",
                    checkSpecific ? ReadinessSeverityEnum.Error : ReadinessSeverityEnum.Warning,
                    "Workflow profile validation failed",
                    error,
                    profile.Name);
            }

            foreach (WorkflowInputReference input in FilterRelevantInputs(profile.RequiredInputs, requestedCheckType, requestedEnvironmentName))
            {
                InputReferenceResolutionResult inputResolution = ResolveInputReference(input);
                if (inputResolution.Exists)
                    continue;

                AddIssue(
                    result,
                    "required_input_missing",
                    checkSpecific ? ReadinessSeverityEnum.Error : ReadinessSeverityEnum.Warning,
                    "Required input missing",
                    BuildInputReferenceMessage(input, inputResolution.Message),
                    FormatInputReferenceDisplay(input));
                result.Issues[result.Issues.Count - 1].InputProvider = input.Provider;
            }

            Dictionary<string, bool> commandProbeCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (checkSpecific)
            {
                CheckRunTypeEnum checkType = requestedCheckType!.Value;
                if (RequiresEnvironment(checkType) && String.IsNullOrWhiteSpace(requestedEnvironmentName))
                {
                    AddIssue(
                        result,
                        "environment_required",
                        ReadinessSeverityEnum.Error,
                        "Environment required",
                        "This check type requires an environment name.",
                        checkType.ToString());
                }

                string? command = _WorkflowProfiles.ResolveCommand(profile, checkType, requestedEnvironmentName);
                if (String.IsNullOrWhiteSpace(command))
                {
                    AddIssue(
                        result,
                        "check_command_missing",
                        ReadinessSeverityEnum.Error,
                        "Requested check is not configured",
                        "No command is configured for " + checkType + ".",
                        checkType.ToString());
                }
                else if (RequiresEnvironment(checkType) && !String.IsNullOrWhiteSpace(requestedEnvironmentName)
                    && !EnvironmentExists(profile, requestedEnvironmentName))
                {
                    AddIssue(
                        result,
                        "environment_not_configured",
                        ReadinessSeverityEnum.Error,
                        "Environment is not configured",
                        "No environment named '" + requestedEnvironmentName + "' exists in the resolved workflow profile.",
                        requestedEnvironmentName);
                }
                else
                {
                    foreach (string missingDependency in await ProbeMissingCommandDependenciesAsync(command, vessel.WorkingDirectory, harborHost, commandProbeCache, token).ConfigureAwait(false))
                    {
                        AddIssue(
                            result,
                            "command_dependency_missing",
                            ReadinessSeverityEnum.Error,
                            "Command dependency missing",
                            "The command dependency '" + missingDependency + "' could not be found.",
                            command);
                    }
                }
            }
            else
            {
                foreach (string command in EnumerateConfiguredCommands(profile))
                {
                    foreach (string missingDependency in await ProbeMissingCommandDependenciesAsync(command, vessel.WorkingDirectory, harborHost, commandProbeCache, token).ConfigureAwait(false))
                    {
                        AddIssue(
                            result,
                            "command_dependency_missing",
                            ReadinessSeverityEnum.Warning,
                            "Command dependency missing",
                            "The command dependency '" + missingDependency + "' could not be found.",
                            command);
                    }
                }
            }

            PopulateChecklist(result, vessel, profile);
            FinalizeResult(result);
            return result;
        }

        private static bool RequiresEnvironment(CheckRunTypeEnum type)
        {
            return type == CheckRunTypeEnum.Deploy
                || type == CheckRunTypeEnum.Rollback
                || type == CheckRunTypeEnum.SmokeTest
                || type == CheckRunTypeEnum.HealthCheck
                || type == CheckRunTypeEnum.DeploymentVerification
                || type == CheckRunTypeEnum.RollbackVerification;
        }

        private static void FinalizeResult(VesselReadinessResult result)
        {
            result.SetupChecklistTotalCount = result.SetupChecklist.Count;
            result.SetupChecklistSatisfiedCount = result.SetupChecklist.Count(item => item.IsSatisfied);
            result.ErrorCount = result.Issues.Count(issue => issue.Severity == ReadinessSeverityEnum.Error);
            result.WarningCount = result.Issues.Count(issue => issue.Severity == ReadinessSeverityEnum.Warning);
            result.IsReady = result.ErrorCount == 0;
        }

        private static void PopulateChecklist(VesselReadinessResult result, Vessel vessel, WorkflowProfile? profile)
        {
            string vesselRoute = "/vessels/" + vessel.Id;
            string vesselEditRoute = vesselRoute + "?edit=1";
            string createWorkflowProfileRoute = "/workflow-profiles/new?scope=Vessel&vesselId=" + Uri.EscapeDataString(vessel.Id);
            string workflowProfileRoute = !String.IsNullOrWhiteSpace(result.WorkflowProfileId)
                ? "/workflow-profiles/" + result.WorkflowProfileId
                : createWorkflowProfileRoute;
            bool requiredInputsSatisfied = !result.Issues.Any(issue =>
                String.Equals(issue.Code, "required_input_missing", StringComparison.OrdinalIgnoreCase));
            bool workflowValidationSatisfied = !result.Issues.Any(issue =>
                String.Equals(issue.Code, "workflow_profile_invalid", StringComparison.OrdinalIgnoreCase));
            bool deploymentWorkflowSatisfied = result.DeploymentMetadata.EnvironmentCount > 0
                && result.DeploymentMetadata.HasDeployCommand
                && (result.DeploymentMetadata.HasHealthCheckCommand
                    || result.DeploymentMetadata.HasSmokeTestCommand
                    || result.DeploymentMetadata.HasDeploymentVerificationCommand);
            bool expectedToolchainsSatisfied = result.ToolchainProbes
                .Where(item => item.Expected)
                .All(item => item.Available);
            bool branchPolicySatisfied = !String.IsNullOrWhiteSpace(vessel.DefaultBranch)
                && (!vessel.RequirePullRequestForProtectedBranches
                    || (vessel.ProtectedBranchPatterns?.Count ?? 0) > 0)
                && (!vessel.RequireMergeQueueForReleaseBranches
                    || !String.IsNullOrWhiteSpace(vessel.ReleaseBranchPrefix));

            result.SetupChecklist = new List<VesselSetupChecklistItem>
            {
                new VesselSetupChecklistItem
                {
                    Code = "working_directory",
                    Severity = ReadinessSeverityEnum.Error,
                    Title = "Working directory",
                    Message = "Configure a valid working directory so Armada can inspect and execute local workflows.",
                    IsSatisfied = result.HasWorkingDirectory,
                    ActionLabel = "Edit vessel",
                    ActionRoute = vesselEditRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "repository_context",
                    Severity = ReadinessSeverityEnum.Error,
                    Title = "Repository context",
                    Message = "Ensure the vessel has either a usable bare repository path or a RepoUrl Armada can recover from.",
                    IsSatisfied = result.HasRepositoryContext,
                    ActionLabel = "Edit vessel",
                    ActionRoute = vesselEditRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "default_branch",
                    Severity = ReadinessSeverityEnum.Warning,
                    Title = "Default branch",
                    Message = "Set the correct default branch for landing, merge-queue, and ahead/behind inspection.",
                    IsSatisfied = !String.IsNullOrWhiteSpace(vessel.DefaultBranch),
                    ActionLabel = "Edit vessel",
                    ActionRoute = vesselEditRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "toolchains",
                    Severity = ReadinessSeverityEnum.Warning,
                    Title = "Expected toolchains",
                    Message = "Install the toolchains this repository appears to need before you rely on build, test, or deploy workflows.",
                    IsSatisfied = expectedToolchainsSatisfied,
                    ActionLabel = "Open workspace",
                    ActionRoute = "/workspace/" + vessel.Id
                },
                new VesselSetupChecklistItem
                {
                    Code = "workflow_profile",
                    Severity = ReadinessSeverityEnum.Warning,
                    Title = "Workflow profile",
                    Message = "Attach or resolve a workflow profile so Armada can build, test, release, and deploy this vessel.",
                    IsSatisfied = !String.IsNullOrWhiteSpace(result.WorkflowProfileId),
                    ActionLabel = "Open profiles",
                    ActionRoute = workflowProfileRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "deployment_environments",
                    Severity = ReadinessSeverityEnum.Info,
                    Title = "Deployment environments",
                    Message = "Add named workflow environments if this vessel ships to multiple targets.",
                    IsSatisfied = profile != null && result.DeploymentEnvironments.Count > 0,
                    ActionLabel = "Open profiles",
                    ActionRoute = workflowProfileRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "workflow_profile_valid",
                    Severity = ReadinessSeverityEnum.Warning,
                    Title = "Workflow validation",
                    Message = "Resolve workflow-profile validation errors so Armada can trust the configured commands and inputs.",
                    IsSatisfied = profile != null && workflowValidationSatisfied,
                    ActionLabel = "Open profiles",
                    ActionRoute = workflowProfileRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "required_inputs",
                    Severity = ReadinessSeverityEnum.Warning,
                    Title = "Workflow inputs and secret providers",
                    Message = "Resolve all required environment variables, filesystem inputs, and provider-backed secret references before build or deploy workflows run.",
                    IsSatisfied = profile != null && requiredInputsSatisfied,
                    ActionLabel = "Open profiles",
                    ActionRoute = workflowProfileRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "branch_policy",
                    Severity = ReadinessSeverityEnum.Info,
                    Title = "Branch and landing policy",
                    Message = "Set protected-branch, release, and hotfix policy so Armada can predict landing behavior cleanly.",
                    IsSatisfied = branchPolicySatisfied,
                    ActionLabel = "Edit vessel",
                    ActionRoute = vesselEditRoute
                },
                new VesselSetupChecklistItem
                {
                    Code = "deploy_workflow",
                    Severity = ReadinessSeverityEnum.Info,
                    Title = "Deploy and verify workflow",
                    Message = "Define deploy plus smoke, health, or deployment-verification coverage so Armada can guide end-to-end shipping checks.",
                    IsSatisfied = profile != null && deploymentWorkflowSatisfied,
                    ActionLabel = "Open profiles",
                    ActionRoute = workflowProfileRoute
                }
            };
        }

        private static void AddIssue(
            VesselReadinessResult result,
            string code,
            ReadinessSeverityEnum severity,
            string title,
            string message,
            string? relatedValue)
        {
            result.Issues.Add(new VesselReadinessIssue
            {
                Code = code,
                Severity = severity,
                Title = title,
                Message = message,
                RelatedValue = relatedValue
            });
        }

        private static bool LooksLikeGitWorkingTree(string workingDirectory)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                return false;

            return Directory.Exists(Path.Combine(workingDirectory, ".git"))
                || File.Exists(Path.Combine(workingDirectory, ".git"));
        }

        private static List<WorkflowInputReference> FilterRelevantInputs(
            List<WorkflowInputReference>? inputs,
            CheckRunTypeEnum? requestedCheckType,
            string? requestedEnvironmentName)
        {
            if (inputs == null || inputs.Count == 0)
                return new List<WorkflowInputReference>();

            if (!requestedCheckType.HasValue)
                return inputs;

            if (RequiresEnvironment(requestedCheckType.Value))
            {
                return inputs
                    .Where(input => String.IsNullOrWhiteSpace(input.EnvironmentName)
                        || String.Equals(input.EnvironmentName, requestedEnvironmentName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return inputs
                .Where(input => String.IsNullOrWhiteSpace(input.EnvironmentName))
                .ToList();
        }

        private static InputReferenceResolutionResult ResolveInputReference(WorkflowInputReference input)
        {
            return input.Provider switch
            {
                WorkflowInputReferenceProviderEnum.EnvironmentVariable => ResolveEnvironmentVariableInput(input),
                WorkflowInputReferenceProviderEnum.FilePath => ResolveFileInput(input),
                WorkflowInputReferenceProviderEnum.DirectoryPath => ResolveDirectoryInput(input),
                WorkflowInputReferenceProviderEnum.AwsSecretsManager => ResolveAwsSecretInput(input),
                WorkflowInputReferenceProviderEnum.AzureKeyVaultSecret => ResolveAzureKeyVaultInput(input),
                WorkflowInputReferenceProviderEnum.HashiCorpVault => ResolveHashiCorpVaultInput(input),
                WorkflowInputReferenceProviderEnum.OnePassword => ResolveOnePasswordInput(input),
                _ => new InputReferenceResolutionResult
                {
                    Exists = false,
                    Message = "Required input '" + input.Key + "' uses an unknown provider."
                }
            };
        }

        private static bool EnvironmentExists(WorkflowProfile profile, string environmentName)
        {
            return profile.Environments.Any(environment =>
                String.Equals(environment.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildInputReferenceMessage(WorkflowInputReference input, string? providerMessage)
        {
            string scopedPrefix = !String.IsNullOrWhiteSpace(input.EnvironmentName)
                ? "For environment '" + input.EnvironmentName + "', "
                : String.Empty;

            if (!String.IsNullOrWhiteSpace(providerMessage))
                return scopedPrefix + providerMessage;

            return scopedPrefix + (input.Provider switch
            {
                WorkflowInputReferenceProviderEnum.EnvironmentVariable => "Environment variable '" + input.Key + "' is not set for the Armada process.",
                WorkflowInputReferenceProviderEnum.FilePath => "File path '" + input.Key + "' does not exist.",
                WorkflowInputReferenceProviderEnum.DirectoryPath => "Directory path '" + input.Key + "' does not exist.",
                _ => "Required input '" + input.Key + "' could not be resolved."
            });
        }

        private static string FormatInputReferenceDisplay(WorkflowInputReference input)
        {
            string providerPrefix = input.Provider switch
            {
                WorkflowInputReferenceProviderEnum.EnvironmentVariable => "env",
                WorkflowInputReferenceProviderEnum.FilePath => "file",
                WorkflowInputReferenceProviderEnum.DirectoryPath => "dir",
                WorkflowInputReferenceProviderEnum.AwsSecretsManager => "aws-sm",
                WorkflowInputReferenceProviderEnum.AzureKeyVaultSecret => "azure-kv",
                WorkflowInputReferenceProviderEnum.HashiCorpVault => "vault",
                WorkflowInputReferenceProviderEnum.OnePassword => "1password",
                _ => "input"
            };

            string display = providerPrefix + ":" + input.Key;
            if (!String.IsNullOrWhiteSpace(input.EnvironmentName))
                display += " [" + input.EnvironmentName + "]";
            return display;
        }

        private static List<string> EnumerateConfiguredCommands(WorkflowProfile profile)
        {
            List<string> commands = new List<string>();

            AddIfPresent(commands, profile.LintCommand);
            AddIfPresent(commands, profile.BuildCommand);
            AddIfPresent(commands, profile.UnitTestCommand);
            AddIfPresent(commands, profile.IntegrationTestCommand);
            AddIfPresent(commands, profile.E2ETestCommand);
            AddIfPresent(commands, profile.MigrationCommand);
            AddIfPresent(commands, profile.SecurityScanCommand);
            AddIfPresent(commands, profile.PerformanceCommand);
            AddIfPresent(commands, profile.PackageCommand);
            AddIfPresent(commands, profile.DeploymentVerificationCommand);
            AddIfPresent(commands, profile.RollbackVerificationCommand);
            AddIfPresent(commands, profile.PublishArtifactCommand);
            AddIfPresent(commands, profile.ReleaseVersioningCommand);
            AddIfPresent(commands, profile.ChangelogGenerationCommand);

            foreach (WorkflowEnvironmentProfile environment in profile.Environments ?? new List<WorkflowEnvironmentProfile>())
            {
                AddIfPresent(commands, environment.DeployCommand);
                AddIfPresent(commands, environment.RollbackCommand);
                AddIfPresent(commands, environment.SmokeTestCommand);
                AddIfPresent(commands, environment.HealthCheckCommand);
                AddIfPresent(commands, environment.DeploymentVerificationCommand);
                AddIfPresent(commands, environment.RollbackVerificationCommand);
            }

            return commands.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddIfPresent(List<string> commands, string? command)
        {
            if (!String.IsNullOrWhiteSpace(command))
                commands.Add(command.Trim());
        }

        private static async Task<List<string>> ProbeMissingCommandDependenciesAsync(
            string command,
            string? workingDirectory,
            VesselHost? harborHost,
            IDictionary<string, bool> commandProbeCache,
            CancellationToken token)
        {
            List<string> missing = new List<string>();
            foreach (string dependency in ExtractPrimaryDependencies(command))
            {
                string cacheKey = dependency;
                if (!commandProbeCache.TryGetValue(cacheKey, out bool available))
                {
                    available = harborHost != null
                        ? await IsDependencyAvailableOnHostAsync(harborHost, dependency, token).ConfigureAwait(false)
                        : IsDependencyAvailable(dependency, workingDirectory);
                    commandProbeCache[cacheKey] = available;
                }

                if (!available)
                    missing.Add(dependency);
            }

            return missing;
        }

        /// <summary>
        /// Whether a command dependency exists on a Harbor: a relative path is looked up in the checkout, an absolute one
        /// on the Harbor host, and a command name on the PATH of the Harbor user's login shell.
        /// </summary>
        private static async Task<bool> IsDependencyAvailableOnHostAsync(VesselHost host, string dependency, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(dependency)) return true;

            try
            {
                bool looksLikePath = dependency.Contains('/') || dependency.Contains('\\') || dependency.StartsWith(".");
                if (looksLikePath && !Path.IsPathRooted(dependency) && !dependency.StartsWith("/", StringComparison.Ordinal))
                {
                    VesselCheckoutFileInfo info = await host.Files.StatAsync(dependency, token).ConfigureAwait(false);
                    return info.Exists;
                }

                HostCommandResult result = await host.Commands.RunAsync(BuildHostProbe(host, looksLikePath ? "test -e \"$1\"" : "command -v \"$1\" >/dev/null 2>&1", dependency, looksLikePath), token).ConfigureAwait(false);
                return result.Success;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// A probe run through the host's login shell, with the probed name passed as an argument (never interpreted by the
        /// shell): <c>/bin/sh -lc script sh name</c>, or <c>where name</c> / <c>if exist</c> on a Windows Harbor.
        /// </summary>
        private static HostCommandRequest BuildHostProbe(VesselHost host, string script, string argument, bool isPath)
        {
            if (host.IsWindows())
            {
                return new HostCommandRequest
                {
                    Executable = isPath ? "cmd.exe" : "where",
                    WorkingDirectory = host.WorkingDirectory,
                    Arguments = isPath ? new List<string> { "/c", "if", "exist", argument, "(exit 0)", "else", "(exit 1)" } : new List<string> { argument },
                    TimeoutMs = 15000
                };
            }

            return new HostCommandRequest
            {
                Executable = "/bin/sh",
                WorkingDirectory = host.WorkingDirectory,
                Arguments = new List<string> { "-lc", script, "sh", argument },
                TimeoutMs = 15000
            };
        }

        /// <summary>
        /// Repository state, toolchains, and their versions for a checkout on a Harbor, probed on that Harbor.
        /// </summary>
        private async Task PopulateHarborCheckoutAsync(VesselReadinessResult result, VesselHost host, string? defaultBranch, CancellationToken token)
        {
            await PopulateRepositoryStateAsync(result, host.Commands, host.WorkingDirectory, defaultBranch).ConfigureAwait(false);

            HashSet<string> rootFiles = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                WorkspaceTreeResult tree = await host.Files.GetTreeAsync(null, token).ConfigureAwait(false);
                foreach (WorkspaceTreeEntry entry in tree.Entries)
                {
                    if (!entry.IsDirectory) rootFiles.Add(entry.Name);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is IOException)
            {
                _Logging.Debug("[VesselReadinessService] listing the checkout on " + host.HostLabel + " failed: " + ex.Message);
            }

            string? dotnetEvidence = null;
            if (rootFiles.Contains("global.json")) dotnetEvidence = "global.json";
            else dotnetEvidence = rootFiles.Where(name => name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)).OrderBy(name => name, StringComparer.Ordinal).FirstOrDefault();
            if (dotnetEvidence == null)
            {
                try
                {
                    IReadOnlyList<string> projects = await host.Git.ListTrackedFilesAsync(host.WorkingDirectory, token).ConfigureAwait(false);
                    dotnetEvidence = projects.FirstOrDefault(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is TimeoutException)
                {
                    _Logging.Debug("[VesselReadinessService] listing tracked files on " + host.HostLabel + " failed: " + ex.Message);
                }
            }

            List<VesselToolchainProbe> probes = new List<VesselToolchainProbe>();
            foreach (KeyValuePair<string, string> expected in ExpectedToolchains(name => rootFiles.Contains(name), dotnetEvidence))
            {
                VesselToolchainProbe probe = new VesselToolchainProbe { Name = expected.Key, Expected = true, Evidence = expected.Value };
                await ProbeToolchainOnHostAsync(host, probe, token).ConfigureAwait(false);
                probes.Add(probe);
            }

            result.ToolchainProbes = probes.OrderBy(probe => probe.Name, StringComparer.OrdinalIgnoreCase).ToList();
            result.DetectedToolchains = result.ToolchainProbes
                .Where(probe => probe.Available)
                .Select(probe => probe.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (VesselToolchainProbe probe in result.ToolchainProbes.Where(item => item.Expected && !item.Available))
            {
                AddIssue(
                    result,
                    "expected_toolchain_missing",
                    ReadinessSeverityEnum.Warning,
                    "Expected toolchain missing",
                    "The repository appears to expect the '" + probe.Name + "' toolchain, but Armada could not find it on Harbor " + host.HarborName + ".",
                    probe.Evidence ?? probe.Name);
            }
        }

        private static async Task ProbeToolchainOnHostAsync(VesselHost host, VesselToolchainProbe probe, CancellationToken token)
        {
            try
            {
                HostCommandResult found = await host.Commands.RunAsync(BuildHostProbe(host, "command -v \"$1\" >/dev/null 2>&1", probe.Name, false), token).ConfigureAwait(false);
                probe.Available = found.Success;
                if (!probe.Available || !_VersionProbeArgs.TryGetValue(probe.Name, out string[]? args)) return;

                HostCommandRequest version = host.IsWindows()
                    ? new HostCommandRequest { Executable = probe.Name, WorkingDirectory = host.WorkingDirectory, Arguments = new List<string>(args), TimeoutMs = 15000 }
                    : new HostCommandRequest { Executable = "/bin/sh", WorkingDirectory = host.WorkingDirectory, Arguments = new List<string> { "-lc", "\"$0\" \"$@\" 2>&1", probe.Name }, TimeoutMs = 15000 };
                if (!host.IsWindows()) version.Arguments.AddRange(args);
                HostCommandResult output = await host.Commands.RunAsync(version, token).ConfigureAwait(false);
                string text = String.IsNullOrWhiteSpace(output.StandardOutput) ? output.StandardError : output.StandardOutput;
                string line = text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .FirstOrDefault(item => !String.IsNullOrWhiteSpace(item))
                    ?? String.Empty;
                probe.Version = String.IsNullOrWhiteSpace(line) ? null : line;
            }
            catch (InvalidOperationException)
            {
                probe.Available = false;
            }
        }

        private static IEnumerable<string> ExtractPrimaryDependencies(string command)
        {
            if (String.IsNullOrWhiteSpace(command))
                yield break;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string segment in _CommandSegmentSplit.Split(command))
            {
                string? dependency = ExtractDependencyFromSegment(segment);
                if (String.IsNullOrWhiteSpace(dependency)) continue;
                if (seen.Add(dependency))
                    yield return dependency;
            }
        }

        private static string? ExtractDependencyFromSegment(string segment)
        {
            string remaining = segment?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(remaining))
                return null;

            int safetyCounter = 0;
            while (!String.IsNullOrWhiteSpace(remaining) && safetyCounter < 8)
            {
                safetyCounter++;
                Match tokenMatch = _TokenRegex.Match(remaining);
                if (!tokenMatch.Success) return null;

                string token = tokenMatch.Groups[1].Success
                    ? tokenMatch.Groups[1].Value
                    : tokenMatch.Groups[2].Success
                        ? tokenMatch.Groups[2].Value
                        : tokenMatch.Groups[3].Value;

                if (String.IsNullOrWhiteSpace(token))
                    return null;

                if (LooksLikeEnvironmentAssignment(token))
                {
                    remaining = remaining.Substring(tokenMatch.Length).TrimStart();
                    continue;
                }

                if (_ShellBuiltins.Contains(token))
                {
                    if (_TerminalShellBuiltins.Contains(token))
                        return null;

                    remaining = remaining.Substring(tokenMatch.Length).TrimStart();
                    continue;
                }

                return token;
            }

            return null;
        }

        private static bool LooksLikeEnvironmentAssignment(string token)
        {
            int separatorIndex = token.IndexOf('=');
            if (separatorIndex <= 0) return false;
            if (token.Contains(Path.DirectorySeparatorChar) || token.Contains(Path.AltDirectorySeparatorChar)) return false;

            string variableName = token.Substring(0, separatorIndex);
            return variableName.All(ch => Char.IsLetterOrDigit(ch) || ch == '_');
        }

        private static bool IsDependencyAvailable(string dependency, string? workingDirectory)
        {
            if (String.IsNullOrWhiteSpace(dependency)) return true;

            if (dependency.Contains(Path.DirectorySeparatorChar) || dependency.Contains(Path.AltDirectorySeparatorChar) || dependency.StartsWith("."))
            {
                if (String.IsNullOrWhiteSpace(workingDirectory))
                    return false;

                try
                {
                    string candidate = Path.IsPathRooted(dependency)
                        ? dependency
                        : Path.GetFullPath(Path.Combine(workingDirectory, dependency));
                    return File.Exists(candidate) || Directory.Exists(candidate);
                }
                catch
                {
                    return false;
                }
            }

            return RuntimeDetectionService.IsCommandAvailable(dependency);
        }

        private async Task PopulateRepositoryStateAsync(VesselReadinessResult result, IHostCommandExecutor commands, string workingDirectory, string? defaultBranch)
        {
            try
            {
                string currentBranch = (await RunGitCommandAsync(commands, workingDirectory, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                result.CurrentBranch = String.Equals(currentBranch, "HEAD", StringComparison.OrdinalIgnoreCase) ? null : currentBranch;
                result.IsDetachedHead = String.Equals(currentBranch, "HEAD", StringComparison.OrdinalIgnoreCase);
                if (result.IsDetachedHead == true)
                {
                    AddIssue(
                        result,
                        "detached_head",
                        ReadinessSeverityEnum.Warning,
                        "Repository is in detached HEAD state",
                        "This working directory is not currently checked out to a named branch.",
                        null);
                }
            }
            catch (Exception ex)
            {
                _Logging.Debug("[VesselReadinessService] current branch probe failed: " + ex.Message);
            }

            try
            {
                string statusOutput = await RunGitCommandAsync(commands, workingDirectory, "status", "--porcelain").ConfigureAwait(false);
                result.HasUncommittedChanges = !String.IsNullOrWhiteSpace(statusOutput);
                if (result.HasUncommittedChanges == true)
                {
                    AddIssue(
                        result,
                        "working_tree_dirty",
                        ReadinessSeverityEnum.Warning,
                        "Working tree is dirty",
                        "The working directory has uncommitted changes that may interfere with build, test, or landing workflows.",
                        null);
                }
            }
            catch (Exception ex)
            {
                _Logging.Debug("[VesselReadinessService] working tree probe failed: " + ex.Message);
            }

            if (String.IsNullOrWhiteSpace(defaultBranch))
                return;

            try
            {
                try
                {
                    await RunGitCommandAsync(commands, workingDirectory, "fetch", "origin", "--quiet").ConfigureAwait(false);
                }
                catch
                {
                }

                string aheadOutput = await RunGitCommandAsync(commands, workingDirectory, "rev-list", "--count", "origin/" + defaultBranch + "..HEAD").ConfigureAwait(false);
                string behindOutput = await RunGitCommandAsync(commands, workingDirectory, "rev-list", "--count", "HEAD..origin/" + defaultBranch).ConfigureAwait(false);

                if (Int32.TryParse(aheadOutput.Trim(), out int ahead))
                    result.CommitsAhead = ahead;
                if (Int32.TryParse(behindOutput.Trim(), out int behind))
                    result.CommitsBehind = behind;
            }
            catch (Exception ex)
            {
                _Logging.Debug("[VesselReadinessService] ahead/behind probe failed: " + ex.Message);
            }
        }

        private static List<VesselToolchainProbe> DetectToolchains(string workingDirectory)
        {
            List<VesselToolchainProbe> results = new List<VesselToolchainProbe>();
            string? dotnetEvidence = File.Exists(Path.Combine(workingDirectory, "global.json"))
                ? "global.json"
                : Directory.EnumerateFiles(workingDirectory, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                    ?? Directory.EnumerateFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();

            foreach (KeyValuePair<string, string> expected in ExpectedToolchains(name => File.Exists(Path.Combine(workingDirectory, name)), dotnetEvidence))
                AddToolchainProbeIfExpected(results, expected.Key, expected.Value);

            return results
                .OrderBy(probe => probe.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The toolchains a checkout appears to need, each with the file that says so, from which files are at its root.
        /// </summary>
        private static List<KeyValuePair<string, string>> ExpectedToolchains(Func<string, bool> rootFileExists, string? dotnetEvidence)
        {
            List<KeyValuePair<string, string>> expected = new List<KeyValuePair<string, string>>();
            void Add(string name, string? evidence)
            {
                if (!String.IsNullOrWhiteSpace(evidence)) expected.Add(new KeyValuePair<string, string>(name, evidence!));
            }

            Add("dotnet", dotnetEvidence);
            Add("node", rootFileExists("package.json") ? "package.json" : null);
            Add("npm", rootFileExists("package.json") ? "package.json" : null);
            Add("pnpm", rootFileExists("pnpm-lock.yaml") ? "pnpm-lock.yaml" : null);
            Add("yarn", rootFileExists("yarn.lock") ? "yarn.lock" : null);
            Add("python", rootFileExists("pyproject.toml") ? "pyproject.toml" : rootFileExists("requirements.txt") ? "requirements.txt" : null);
            Add("docker", rootFileExists("Dockerfile") ? "Dockerfile" : null);
            Add("cargo", rootFileExists("Cargo.toml") ? "Cargo.toml" : null);
            Add("go", rootFileExists("go.mod") ? "go.mod" : null);
            string? java = rootFileExists("pom.xml") ? "pom.xml" : rootFileExists("build.gradle") ? "build.gradle" : null;
            Add("java", java);
            Add(rootFileExists("pom.xml") ? "mvn" : "gradle", java);
            return expected;
        }

        private static void AddToolchainProbeIfExpected(List<VesselToolchainProbe> probes, string toolchainName, string? evidence)
        {
            if (String.IsNullOrWhiteSpace(evidence))
                return;

            VesselToolchainProbe probe = new VesselToolchainProbe
            {
                Name = toolchainName,
                Expected = true,
                Evidence = evidence,
                Available = RuntimeDetectionService.IsCommandAvailable(toolchainName),
                Version = null
            };

            if (probe.Available)
            {
                probe.Version = TryReadCommandVersion(toolchainName);
            }

            probes.Add(probe);
        }

        private static string? TryReadCommandVersion(string command)
        {
            if (!_VersionProbeArgs.TryGetValue(command, out string[]? args))
                return null;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(command)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (string arg in args)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start version probe.");
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(3000);

                string output = String.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                string line = output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .FirstOrDefault(item => !String.IsNullOrWhiteSpace(item))
                    ?? String.Empty;
                return String.IsNullOrWhiteSpace(line) ? null : line;
            }
            catch
            {
                return null;
            }
        }

        private static InputReferenceResolutionResult ResolveEnvironmentVariableInput(WorkflowInputReference input)
        {
            bool exists = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(input.Key));
            return new InputReferenceResolutionResult
            {
                Exists = exists,
                Message = exists ? null : "Environment variable '" + input.Key + "' is not set for the Armada process."
            };
        }

        private static InputReferenceResolutionResult ResolveFileInput(WorkflowInputReference input)
        {
            bool exists = File.Exists(input.Key);
            return new InputReferenceResolutionResult
            {
                Exists = exists,
                Message = exists ? null : "File path '" + input.Key + "' does not exist."
            };
        }

        private static InputReferenceResolutionResult ResolveDirectoryInput(WorkflowInputReference input)
        {
            bool exists = Directory.Exists(input.Key);
            return new InputReferenceResolutionResult
            {
                Exists = exists,
                Message = exists ? null : "Directory path '" + input.Key + "' does not exist."
            };
        }

        private static InputReferenceResolutionResult ResolveAwsSecretInput(WorkflowInputReference input)
        {
            bool hasRegion = HasAnyEnvironmentValue("AWS_REGION", "AWS_DEFAULT_REGION");
            bool hasProfile = HasAnyEnvironmentValue("AWS_PROFILE");
            bool hasKeyPair = !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"))
                && (!String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"))
                    || !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN")));

            if (hasRegion && (hasProfile || hasKeyPair))
            {
                return new InputReferenceResolutionResult
                {
                    Exists = true
                };
            }

            return new InputReferenceResolutionResult
            {
                Exists = false,
                Message = "AWS Secrets Manager reference '" + input.Key + "' is configured, but the host is missing AWS region or credentials."
            };
        }

        private static InputReferenceResolutionResult ResolveAzureKeyVaultInput(WorkflowInputReference input)
        {
            bool hasTenant = HasAnyEnvironmentValue("AZURE_TENANT_ID");
            bool hasClient = HasAnyEnvironmentValue("AZURE_CLIENT_ID");
            bool hasCredential = HasAnyEnvironmentValue(
                "AZURE_CLIENT_SECRET",
                "AZURE_FEDERATED_TOKEN_FILE",
                "AZURE_CLIENT_CERTIFICATE_PATH");

            if (hasTenant && hasClient && hasCredential)
            {
                return new InputReferenceResolutionResult
                {
                    Exists = true
                };
            }

            return new InputReferenceResolutionResult
            {
                Exists = false,
                Message = "Azure Key Vault reference '" + input.Key + "' is configured, but the host is missing Azure service-principal or federated credentials."
            };
        }

        private static InputReferenceResolutionResult ResolveHashiCorpVaultInput(WorkflowInputReference input)
        {
            bool hasAddress = HasAnyEnvironmentValue("VAULT_ADDR");
            bool hasToken = HasAnyEnvironmentValue("VAULT_TOKEN");
            bool hasAppRole = HasAnyEnvironmentValue("VAULT_ROLE_ID") && HasAnyEnvironmentValue("VAULT_SECRET_ID");

            if (hasAddress && (hasToken || hasAppRole))
            {
                return new InputReferenceResolutionResult
                {
                    Exists = true
                };
            }

            return new InputReferenceResolutionResult
            {
                Exists = false,
                Message = "HashiCorp Vault reference '" + input.Key + "' is configured, but the host is missing Vault address or authentication settings."
            };
        }

        private static InputReferenceResolutionResult ResolveOnePasswordInput(WorkflowInputReference input)
        {
            bool hasServiceAccount = HasAnyEnvironmentValue("OP_SERVICE_ACCOUNT_TOKEN");
            bool hasConnect = HasAnyEnvironmentValue("OP_CONNECT_HOST") && HasAnyEnvironmentValue("OP_CONNECT_TOKEN");
            bool hasSession = false;
            IDictionary variables = Environment.GetEnvironmentVariables();
            foreach (object key in variables.Keys)
            {
                string keyString = key?.ToString() ?? String.Empty;
                if (keyString.StartsWith("OP_SESSION_", StringComparison.OrdinalIgnoreCase)
                    && !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(keyString)))
                {
                    hasSession = true;
                    break;
                }
            }

            if (hasServiceAccount || hasConnect || hasSession)
            {
                return new InputReferenceResolutionResult
                {
                    Exists = true
                };
            }

            return new InputReferenceResolutionResult
            {
                Exists = false,
                Message = "1Password reference '" + input.Key + "' is configured, but the host is missing an OP service-account, Connect, or CLI session credential."
            };
        }

        private static bool HasAnyEnvironmentValue(params string[] variableNames)
        {
            foreach (string variableName in variableNames)
            {
                if (!String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName)))
                    return true;
            }

            return false;
        }

        private static VesselDeploymentMetadata BuildDeploymentMetadata(WorkflowProfile profile)
        {
            List<WorkflowEnvironmentProfile> environments = profile.Environments ?? new List<WorkflowEnvironmentProfile>();
            return new VesselDeploymentMetadata
            {
                EnvironmentCount = environments.Count(environment => !String.IsNullOrWhiteSpace(environment.EnvironmentName)),
                HasDeployCommand = environments.Any(environment => !String.IsNullOrWhiteSpace(environment.DeployCommand)),
                HasRollbackCommand = environments.Any(environment => !String.IsNullOrWhiteSpace(environment.RollbackCommand)),
                HasSmokeTestCommand = environments.Any(environment => !String.IsNullOrWhiteSpace(environment.SmokeTestCommand)),
                HasHealthCheckCommand = environments.Any(environment => !String.IsNullOrWhiteSpace(environment.HealthCheckCommand)),
                HasDeploymentVerificationCommand =
                    !String.IsNullOrWhiteSpace(profile.DeploymentVerificationCommand)
                    || environments.Any(environment => !String.IsNullOrWhiteSpace(environment.DeploymentVerificationCommand)),
                HasRollbackVerificationCommand =
                    !String.IsNullOrWhiteSpace(profile.RollbackVerificationCommand)
                    || environments.Any(environment => !String.IsNullOrWhiteSpace(environment.RollbackVerificationCommand))
            };
        }

        private static async Task<string> RunGitCommandAsync(IHostCommandExecutor commands, string workingDirectory, params string[] args)
        {
            HostCommandResult result = await commands.RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string>(args),
                TimeoutMs = 60000
            }).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException(String.IsNullOrWhiteSpace(result.StandardError) ? "git failed." : result.StandardError.Trim());
            return result.StandardOutput;
        }

        private sealed class InputReferenceResolutionResult
        {
            public bool Exists { get; set; } = false;
            public string? Message { get; set; } = null;
        }
    }
}
