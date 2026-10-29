namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// BlackBoxConfigurationResolver: each effective value with its source, in the precedence the landing handler and
    /// settings use (voyage, vessel, global, default), workflow profile commands, captain and persona values, the 64 value
    /// bound, merging, and the enrichment step that completes capture's configuration.
    /// </summary>
    public sealed class BlackBoxConfigurationSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxConfiguration";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("precedence", "Landing mode comes from the voyage, else the vessel, else the global setting, else MergeAndPush; other values name their source", TestTags.Positive, () =>
            {
                ArmadaSettings g = new ArmadaSettings { LandingMode = LandingModeEnum.LocalMerge, AutoMergePullRequests = true };
                Vessel vessel = new Vessel("v", "https://example.invalid/v.git") { LandingMode = LandingModeEnum.PullRequest, AllowConcurrentMissions = true, TestCommand = "make test" };
                Voyage voyage = new Voyage("voy") { LandingMode = LandingModeEnum.MergeQueue, AutoMergePullRequests = false };
                Mission mission = new Mission("m", "d") { RequiresReview = true, DependsOnMissionId = "msn_dep", Persona = "Worker" };
                Captain captain = new Captain("c", AgentRuntimeEnum.Codex) { Model = "gpt-5" };

                AssertValue(Resolve(g, mission, voyage, vessel, captain), "LandingMode", "MergeQueue", BlackBoxConfigSourceEnum.Voyage);
                AssertValue(Resolve(g, mission, null, vessel, captain), "LandingMode", "PullRequest", BlackBoxConfigSourceEnum.Vessel);
                AssertValue(Resolve(g, mission, null, null, captain), "LandingMode", "LocalMerge", BlackBoxConfigSourceEnum.Global);
                AssertValue(Resolve(new ArmadaSettings { LandingMode = null }, mission, null, null, null), "LandingMode", "MergeAndPush", BlackBoxConfigSourceEnum.Default);
                BlackBoxConfiguration all = Resolve(g, mission, voyage, vessel, captain);
                AssertValue(all, "AutoMergePullRequests", "False", BlackBoxConfigSourceEnum.Voyage);
                AssertValue(Resolve(g, mission, null, vessel, captain), "AutoMergePullRequests", "True", BlackBoxConfigSourceEnum.Global);
                AssertValue(all, "RequiresReview", "True", BlackBoxConfigSourceEnum.Mission);
                AssertValue(all, "DependsOnMissionId", "msn_dep", BlackBoxConfigSourceEnum.Mission);
                AssertValue(all, "AllowConcurrentMissions", "True", BlackBoxConfigSourceEnum.Vessel);
                AssertValue(all, "TestCommand", "make test", BlackBoxConfigSourceEnum.Vessel);
                AssertValue(all, "CaptainRuntime", "Codex", BlackBoxConfigSourceEnum.Captain);
                AssertValue(all, "CaptainModel", "gpt-5", BlackBoxConfigSourceEnum.Captain);
                AssertValue(all, "StallThresholdMinutes", g.StallThresholdMinutes.ToString(), BlackBoxConfigSourceEnum.Global);
                AssertValue(all, "BlackBoxRetentionDays", "180", BlackBoxConfigSourceEnum.Global);
                vessel.BlackBoxRetentionDays = 7;
                AssertValue(Resolve(g, mission, voyage, vessel, captain), "BlackBoxRetentionDays", "7", BlackBoxConfigSourceEnum.Vessel);
                AssertTrue(all.Values.Count <= BlackBoxLimits.MaxConfigValues, "bounded");
                AssertEqual(all.Values.Count, all.Values.Select(v => v.Setting).Distinct().Count(), "each setting once");
                return Task.CompletedTask;
            }));

            cases.Add(Case("profile_persona_and_bounds", "Workflow profile commands and persona priorities are recorded with their sources; vessel intent overrides beat the profile for Test and Lint; merge keeps capture's values; at most 64 values", TestTags.Positive, () =>
            {
                ArmadaSettings g = new ArmadaSettings();
                Vessel vessel = new Vessel("v", "https://example.invalid/v.git") { LintCommand = "eslint ." };
                WorkflowProfile profile = new WorkflowProfile { Name = "dotnet", BuildCommand = "dotnet build", UnitTestCommand = "dotnet test", LintCommand = "dotnet format --verify-no-changes" };
                Persona persona = new Persona("Worker", "persona.worker");
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode, Model = "opus" });
                BlackBoxConfiguration c = BlackBoxConfigurationResolver.Resolve(new BlackBoxConfigurationInputs { Settings = g, Vessel = vessel, WorkflowProfile = profile, Persona = persona, Mission = new Mission("m", "d") });
                AssertValue(c, "WorkflowProfile", "dotnet", BlackBoxConfigSourceEnum.WorkflowProfile);
                AssertValue(c, "BuildCommand", "dotnet build", BlackBoxConfigSourceEnum.WorkflowProfile);
                AssertValue(c, "TestCommand", "dotnet test", BlackBoxConfigSourceEnum.WorkflowProfile);
                AssertValue(c, "LintCommand", "eslint .", BlackBoxConfigSourceEnum.Vessel);
                AssertValue(c, "CaptainPriorities", "ClaudeCode opus", BlackBoxConfigSourceEnum.Persona);
                AssertNotNull(c.Values.FirstOrDefault(v => v.Setting == "PriorityExhaustion" && v.Source == BlackBoxConfigSourceEnum.Persona), "persona policy");

                BlackBoxConfiguration captured = new BlackBoxConfiguration();
                captured.Values.Add(new BlackBoxConfigValue { Setting = "LandingMode", Value = "LocalMerge", Source = BlackBoxConfigSourceEnum.Vessel });
                int added = BlackBoxConfigurationResolver.Merge(captured, c);
                AssertTrue(added > 5, "missing settings added");
                AssertEqual("LocalMerge", captured.Values.Single(v => v.Setting == "LandingMode").Value, "the failure-time value is kept");
                BlackBoxConfiguration full = new BlackBoxConfiguration();
                for (int i = 0; i < 70; i++) full.Values.Add(new BlackBoxConfigValue { Setting = "S" + i });
                full.Values.RemoveRange(64, 6);
                AssertEqual(0, BlackBoxConfigurationResolver.Merge(full, c), "nothing past 64");
                AssertThrows<ArgumentNullException>(() => BlackBoxConfigurationResolver.Resolve(null!), "null inputs");
                return Task.CompletedTask;
            }));

            cases.Add(Case("capture_and_enrichment", "Capture records the resolved configuration; enrichment adds the workflow profile and persona without overwriting capture's values", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.LandingMode = LandingModeEnum.LocalMerge;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-config-" + Guid.NewGuid().ToString("N").Substring(0, 6), "https://example.invalid/c.git") { LandingMode = LandingModeEnum.PullRequest }).ConfigureAwait(false);
                Persona persona = new Persona("BbConfigPersona" + Guid.NewGuid().ToString("N").Substring(0, 6), "persona.worker");
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                persona = await t.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.VesselId = vessel.Id;
                mission.Persona = persona.Name;
                mission = await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                BlackBoxRecord captured = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertValue(captured.Configuration!, "LandingMode", "PullRequest", BlackBoxConfigSourceEnum.Vessel);
                AssertNull(captured.Configuration!.Values.FirstOrDefault(v => v.Setting == "CaptainPriorities"), "capture does not read the persona");
                await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);
                BlackBoxRecord enriched = await bb.Pipeline.LoadRecordAsync((await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!).ConfigureAwait(false);
                AssertValue(enriched.Configuration!, "CaptainPriorities", "Codex", BlackBoxConfigSourceEnum.Persona);
                AssertValue(enriched.Configuration!, "LandingMode", "PullRequest", BlackBoxConfigSourceEnum.Vessel);
                AssertEqual(1, enriched.Configuration!.Values.Count(v => v.Setting == "LandingMode"), "no duplicates");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box effective configuration", cases: cases);
        }

        private static BlackBoxConfiguration Resolve(ArmadaSettings g, Mission? m, Voyage? voyage, Vessel? vessel, Captain? captain)
        {
            return BlackBoxConfigurationResolver.Resolve(new BlackBoxConfigurationInputs { Settings = g, Mission = m, Voyage = voyage, Vessel = vessel, Captain = captain });
        }

        private static void AssertValue(BlackBoxConfiguration c, string setting, string value, BlackBoxConfigSourceEnum source)
        {
            BlackBoxConfigValue? v = c.Values.FirstOrDefault(x => x.Setting == setting);
            AssertNotNull(v, setting + " recorded");
            AssertEqual(value, v!.Value, setting + " value");
            AssertEqual(source, v.Source, setting + " source");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
