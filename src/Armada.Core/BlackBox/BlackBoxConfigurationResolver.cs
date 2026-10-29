namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Resolves the effective configuration that shaped a failure, each value with the source it came from (Mission,
    /// Voyage, Vessel, WorkflowProfile, Captain, Persona, Global, Default). Precedence matches the code that applies each
    /// setting: landing mode voyage, then vessel, then global, then MergeAndPush (MissionLandingHandler); pull-request
    /// merge voyage then global; branch cleanup vessel then global; stage check commands from the workflow profile;
    /// thresholds and attempt limits from the global settings. At most <see cref="BlackBoxLimits.MaxConfigValues"/> (64)
    /// values. <see cref="Resolve"/> is pure; <see cref="LoadAsync"/> reads the persona and workflow profile. Thread-safe.
    /// </summary>
    public static class BlackBoxConfigurationResolver
    {
        #region Public-Methods

        /// <summary>
        /// Resolve the effective configuration.
        /// </summary>
        /// <param name="inputs">Inputs.</param>
        /// <returns>The configuration (at most 64 values).</returns>
        /// <exception cref="ArgumentNullException">Thrown when inputs is null.</exception>
        public static BlackBoxConfiguration Resolve(BlackBoxConfigurationInputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            ArmadaSettings g = inputs.Settings;
            Mission? mission = inputs.Mission;
            Voyage? voyage = inputs.Voyage;
            Vessel? vessel = inputs.Vessel;
            Captain? captain = inputs.Captain;
            Persona? persona = inputs.Persona;
            WorkflowProfile? profile = inputs.WorkflowProfile;
            BlackBoxConfiguration config = new BlackBoxConfiguration();

            // Landing (MissionLandingHandler.HandleMissionCompleteAsync: voyage > vessel > global > MergeAndPush).
            LandingModeEnum landing;
            if (voyage?.LandingMode != null) { landing = voyage.LandingMode.Value; Add(config, "LandingMode", landing.ToString(), BlackBoxConfigSourceEnum.Voyage); }
            else if (vessel?.LandingMode != null) { landing = vessel.LandingMode.Value; Add(config, "LandingMode", landing.ToString(), BlackBoxConfigSourceEnum.Vessel); }
            else if (g.LandingMode != null) { landing = g.LandingMode.Value; Add(config, "LandingMode", landing.ToString(), BlackBoxConfigSourceEnum.Global); }
            else { landing = LandingModeEnum.MergeAndPush; Add(config, "LandingMode", landing.ToString(), BlackBoxConfigSourceEnum.Default); }
            if (voyage?.AutoPush != null) Add(config, "AutoPush", Bool(voyage.AutoPush.Value), BlackBoxConfigSourceEnum.Voyage);
            else Add(config, "AutoPush", Bool(landing == LandingModeEnum.MergeAndPush), BlackBoxConfigSourceEnum.Default);
            if (voyage?.AutoCreatePullRequests != null) Add(config, "AutoCreatePullRequests", Bool(voyage.AutoCreatePullRequests.Value), BlackBoxConfigSourceEnum.Voyage);
            if (voyage?.AutoMergePullRequests != null) Add(config, "AutoMergePullRequests", Bool(voyage.AutoMergePullRequests.Value), BlackBoxConfigSourceEnum.Voyage);
            else Add(config, "AutoMergePullRequests", Bool(g.AutoMergePullRequests), BlackBoxConfigSourceEnum.Global);
            if (vessel?.BranchCleanupPolicy != null) Add(config, "BranchCleanupPolicy", vessel.BranchCleanupPolicy.Value.ToString(), BlackBoxConfigSourceEnum.Vessel);
            else Add(config, "BranchCleanupPolicy", g.BranchCleanupPolicy.ToString(), BlackBoxConfigSourceEnum.Global);

            // Mission.
            if (mission != null)
            {
                Add(config, "RequiresReview", Bool(mission.RequiresReview), BlackBoxConfigSourceEnum.Mission);
                Add(config, "WaitForVoyageWorkers", Bool(mission.WaitForVoyageWorkers), BlackBoxConfigSourceEnum.Mission);
                if (!String.IsNullOrEmpty(mission.DependsOnMissionId)) Add(config, "DependsOnMissionId", mission.DependsOnMissionId, BlackBoxConfigSourceEnum.Mission);
                if (!String.IsNullOrEmpty(mission.Persona)) Add(config, "Persona", mission.Persona, BlackBoxConfigSourceEnum.Mission);
                else if (!String.IsNullOrEmpty(captain?.PreferredPersona)) Add(config, "Persona", captain!.PreferredPersona, BlackBoxConfigSourceEnum.Captain);
                Add(config, "MissionMode", mission.Mode.ToString(), BlackBoxConfigSourceEnum.Mission);
                if (mission.Tier.HasValue) Add(config, "Tier", mission.Tier.Value.ToString(), BlackBoxConfigSourceEnum.Mission);
                else if (captain?.Tier != null) Add(config, "Tier", captain.Tier.Value.ToString(), BlackBoxConfigSourceEnum.Captain);
            }

            // Vessel.
            if (vessel != null)
            {
                Add(config, "AllowConcurrentMissions", Bool(vessel.AllowConcurrentMissions), BlackBoxConfigSourceEnum.Vessel);
                if (vessel.AutoApprove.HasValue) Add(config, "AutoApprove", Bool(vessel.AutoApprove.Value), BlackBoxConfigSourceEnum.Vessel);
                Add(config, "RequirePassingChecksToLand", Bool(vessel.RequirePassingChecksToLand), BlackBoxConfigSourceEnum.Vessel);
                Add(config, "DefinitionOfDoneEnabled", Bool(vessel.DefinitionOfDoneEnabled), BlackBoxConfigSourceEnum.Vessel);
                if (!String.IsNullOrWhiteSpace(vessel.DefinitionOfDoneBuildCommand)) Add(config, "DefinitionOfDoneBuildCommand", vessel.DefinitionOfDoneBuildCommand, BlackBoxConfigSourceEnum.Vessel);
                if (!String.IsNullOrWhiteSpace(vessel.DefinitionOfDoneTestCommand)) Add(config, "DefinitionOfDoneTestCommand", vessel.DefinitionOfDoneTestCommand, BlackBoxConfigSourceEnum.Vessel);
                Add(config, "AutoLandEnabled", Bool(vessel.AutoLandEnabled), BlackBoxConfigSourceEnum.Vessel);
                if (vessel.ProtectedPathPatterns.Count > 0) Add(config, "ProtectedPathPatterns", String.Join(", ", vessel.ProtectedPathPatterns.Take(20)), BlackBoxConfigSourceEnum.Vessel);
                if (!String.IsNullOrWhiteSpace(vessel.DefaultPipelineId)) Add(config, "DefaultPipelineId", vessel.DefaultPipelineId, BlackBoxConfigSourceEnum.Vessel);
            }

            // Commands: the workflow profile drives stage checks and landing verification; the vessel's intent overrides
            // (TestCommand, LintCommand) beat a profile for the Test and Lint intents.
            if (profile != null)
            {
                Add(config, "WorkflowProfile", profile.Name, BlackBoxConfigSourceEnum.WorkflowProfile);
                if (!String.IsNullOrWhiteSpace(profile.BuildCommand)) Add(config, "BuildCommand", profile.BuildCommand, BlackBoxConfigSourceEnum.WorkflowProfile);
                if (!String.IsNullOrWhiteSpace(profile.UnitTestCommand)) Add(config, "UnitTestCommand", profile.UnitTestCommand, BlackBoxConfigSourceEnum.WorkflowProfile);
            }
            else if (!String.IsNullOrWhiteSpace(vessel?.DefinitionOfDoneBuildCommand))
            {
                Add(config, "BuildCommand", vessel!.DefinitionOfDoneBuildCommand, BlackBoxConfigSourceEnum.Vessel);
            }
            if (!String.IsNullOrWhiteSpace(vessel?.TestCommand)) Add(config, "TestCommand", vessel!.TestCommand, BlackBoxConfigSourceEnum.Vessel);
            else if (!String.IsNullOrWhiteSpace(profile?.UnitTestCommand)) Add(config, "TestCommand", profile!.UnitTestCommand, BlackBoxConfigSourceEnum.WorkflowProfile);
            if (!String.IsNullOrWhiteSpace(vessel?.LintCommand)) Add(config, "LintCommand", vessel!.LintCommand, BlackBoxConfigSourceEnum.Vessel);
            else if (!String.IsNullOrWhiteSpace(profile?.LintCommand)) Add(config, "LintCommand", profile!.LintCommand, BlackBoxConfigSourceEnum.WorkflowProfile);
            if (!String.IsNullOrWhiteSpace(g.MergeQueueTestCommand)) Add(config, "MergeQueueTestCommand", g.MergeQueueTestCommand, BlackBoxConfigSourceEnum.Global);

            // Captain and persona.
            if (captain != null)
            {
                Add(config, "CaptainRuntime", captain.Runtime.ToString(), BlackBoxConfigSourceEnum.Captain);
                if (!String.IsNullOrWhiteSpace(captain.Model)) Add(config, "CaptainModel", captain.Model, BlackBoxConfigSourceEnum.Captain);
                if (!String.IsNullOrWhiteSpace(captain.ModelEndpointId)) Add(config, "ModelEndpointId", captain.ModelEndpointId, BlackBoxConfigSourceEnum.Captain);
                if (captain.ReasoningEffort.HasValue) Add(config, "ReasoningEffort", captain.ReasoningEffort.Value.ToString(), BlackBoxConfigSourceEnum.Captain);
                if (captain.CliPermissionPolicy.HasValue) Add(config, "CliPermissionPolicy", captain.CliPermissionPolicy.Value.ToString(), BlackBoxConfigSourceEnum.Captain);
            }
            if (persona != null)
            {
                Add(config, "CaptainPriorities", persona.CaptainPriorities.Count == 0 ? "none (default captain)" : String.Join("; ", persona.CaptainPriorities.Take(5).Select(Describe)) + (persona.CaptainPriorities.Count > 5 ? "; and " + (persona.CaptainPriorities.Count - 5).ToString(CultureInfo.InvariantCulture) + " more" : String.Empty), BlackBoxConfigSourceEnum.Persona);
                Add(config, "PriorityAvailability", persona.PriorityAvailability.ToString(), BlackBoxConfigSourceEnum.Persona);
                Add(config, "PriorityExhaustion", persona.PriorityExhaustion.ToString(), BlackBoxConfigSourceEnum.Persona);
                if (!String.IsNullOrWhiteSpace(persona.DefaultCaptainId)) Add(config, "DefaultCaptainId", persona.DefaultCaptainId, BlackBoxConfigSourceEnum.Persona);
            }

            // Global thresholds and limits.
            Add(config, "StallThresholdMinutes", Int(g.StallThresholdMinutes), BlackBoxConfigSourceEnum.Global);
            Add(config, "ToolWaitHardLimitMinutes", Int(g.ToolWaitHardLimitMinutes), BlackBoxConfigSourceEnum.Global);
            Add(config, "StageCheckTimeoutMinutes", Int(g.StageCheckTimeoutMinutes), BlackBoxConfigSourceEnum.Global);
            Add(config, "MaxMissionRuntimeMinutes", Int(g.MaxMissionRuntimeMinutes), BlackBoxConfigSourceEnum.Global);
            Add(config, "MaxRecoveryAttempts", Int(g.MaxRecoveryAttempts), BlackBoxConfigSourceEnum.Global);
            Add(config, "MaxMissionRecoveryAttempts", Int(g.MaxMissionRecoveryAttempts), BlackBoxConfigSourceEnum.Global);
            Add(config, "MaxLandingRetries", Int(g.MaxLandingRetries), BlackBoxConfigSourceEnum.Global);
            Add(config, "MaxNoOpRedispatchAttempts", Int(g.MaxNoOpRedispatchAttempts), BlackBoxConfigSourceEnum.Global);
            Add(config, "MaxOutputRepairAttempts", Int(g.MaxOutputRepairAttempts), BlackBoxConfigSourceEnum.Global);
            Add(config, "ReviewTimeoutMinutes", Int(g.ReviewTimeoutMinutes), BlackBoxConfigSourceEnum.Global);
            Add(config, "LandingDeferralDeadlineHours", Int(g.LandingDeferralDeadlineHours), BlackBoxConfigSourceEnum.Global);
            Add(config, "MaxConcurrentMissions", Int(g.MaxConcurrentMissions), BlackBoxConfigSourceEnum.Global);
            Add(config, "BlockDependentsOfFailedStages", Bool(g.BlockDependentsOfFailedStages), BlackBoxConfigSourceEnum.Global);
            if (vessel?.BlackBoxRetentionDays != null) Add(config, "BlackBoxRetentionDays", Int(vessel.BlackBoxRetentionDays.Value), BlackBoxConfigSourceEnum.Vessel);
            else Add(config, "BlackBoxRetentionDays", Int(g.BlackBox.Retention.RetentionDays), BlackBoxConfigSourceEnum.Global);
            return config;
        }

        /// <summary>
        /// Load the persona (by the mission's persona name, in its tenant) and the workflow profile resolved for the vessel,
        /// and return the inputs. Read failures are logged and leave that row null.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Global settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="mission">Mission, or null.</param>
        /// <param name="voyage">Voyage, or null.</param>
        /// <param name="vessel">Vessel, or null.</param>
        /// <param name="captain">Captain, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The inputs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when database, settings or logging is null.</exception>
        public static async Task<BlackBoxConfigurationInputs> LoadAsync(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, Mission? mission, Voyage? voyage, Vessel? vessel, Captain? captain, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            BlackBoxConfigurationInputs inputs = new BlackBoxConfigurationInputs { Settings = settings, Mission = mission, Voyage = voyage, Vessel = vessel, Captain = captain };
            string? personaName = !String.IsNullOrWhiteSpace(mission?.Persona) ? mission!.Persona : captain?.PreferredPersona;
            try
            {
                if (!String.IsNullOrWhiteSpace(personaName))
                {
                    string? tenantId = mission?.TenantId ?? vessel?.TenantId;
                    inputs.Persona = !String.IsNullOrEmpty(tenantId)
                        ? await database.Personas.ReadByNameAsync(tenantId!, personaName!, token).ConfigureAwait(false) ?? await database.Personas.ReadByNameAsync(personaName!, token).ConfigureAwait(false)
                        : await database.Personas.ReadByNameAsync(personaName!, token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                logging.Debug("[BlackBoxConfigurationResolver] persona " + personaName + " unreadable: " + e.Message);
            }
            try
            {
                if (vessel != null)
                {
                    AuthContext system = new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = vessel.TenantId };
                    inputs.WorkflowProfile = await new WorkflowProfileService(database, logging).ResolveForVesselAsync(system, vessel, null, token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                logging.Debug("[BlackBoxConfigurationResolver] workflow profile for " + vessel?.Id + " unreadable: " + e.Message);
            }
            return inputs;
        }

        /// <summary>
        /// Add the values of <paramref name="extra"/> whose setting <paramref name="target"/> does not have yet (keeps the
        /// values recorded at the failure site), up to the 64 value limit.
        /// </summary>
        /// <param name="target">Configuration to extend.</param>
        /// <param name="extra">Values to merge in.</param>
        /// <returns>The number of values added.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static int Merge(BlackBoxConfiguration target, BlackBoxConfiguration extra)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (extra == null) throw new ArgumentNullException(nameof(extra));
            HashSet<string> present = new HashSet<string>(target.Values.Select(v => v.Setting), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (BlackBoxConfigValue v in extra.Values)
            {
                if (target.Values.Count >= BlackBoxLimits.MaxConfigValues) break;
                if (!present.Add(v.Setting)) continue;
                target.Values.Add(v);
                added++;
            }
            return added;
        }

        #endregion

        #region Private-Methods

        private static void Add(BlackBoxConfiguration config, string setting, string? value, BlackBoxConfigSourceEnum source)
        {
            if (config.Values.Count >= BlackBoxLimits.MaxConfigValues) return;
            config.Values.Add(new BlackBoxConfigValue { Setting = setting, Value = BlackBoxLimits.Clip(value, 500), Source = source });
        }

        private static string Bool(bool value)
        {
            return value ? "True" : "False";
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Describe(PersonaCaptainPriority p)
        {
            List<string> parts = new List<string>();
            if (p.Runtime.HasValue) parts.Add(p.Runtime.Value.ToString());
            if (!String.IsNullOrWhiteSpace(p.Model)) parts.Add(p.Model!);
            if (!String.IsNullOrWhiteSpace(p.Endpoint)) parts.Add("endpoint " + p.Endpoint);
            if (p.MinimumTier.HasValue) parts.Add("tier >= " + p.MinimumTier.Value);
            return parts.Count == 0 ? "any captain" : String.Join(" ", parts);
        }

        #endregion
    }
}
