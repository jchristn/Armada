namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors that guard cross-surface consistency of the Armada progress/verdict signal
    /// contract: embedded prompt templates, <see cref="MissionService"/> hardcoded fallbacks,
    /// <see cref="MissionPromptBuilder"/> captain-instruction/persona/launch surfaces, and the
    /// structured judge-verdict parser. Positive cases assert that documented RESULT and VERDICT
    /// signals appear consistently across every surface; the negative case asserts the verdict
    /// parser rejects non-documented signal lines.
    /// </summary>
    public sealed class PromptSignalConsistencySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PromptSignalConsistency";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Prompt Signal Consistency suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("mission_service_fallback_progress_signals_match_embedded_result_and_verdict_contract", "MissionService fallback progress signals match embedded result and verdict contract", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    string embeddedSignals = await ResolveTemplateContentAsync(templates, "mission.progress_signals").ConfigureAwait(false);

                    MissionService missionService = CreateMissionService(logging, testDb.Driver, CreateSettings());
                    string fallbackSignals = GetMissionServiceFallback(missionService, "mission.progress_signals");

                    AssertContains("`[ARMADA:RESULT] COMPLETE`", embeddedSignals, "embedded runtime signals should include RESULT COMPLETE");
                    AssertContains("`[ARMADA:VERDICT] PASS`", embeddedSignals, "embedded runtime signals should include VERDICT PASS");
                    AssertContains("`[ARMADA:VERDICT] FAIL`", embeddedSignals, "embedded runtime signals should include VERDICT FAIL");
                    AssertContains("`[ARMADA:VERDICT] NEEDS_REVISION`", embeddedSignals, "embedded runtime signals should include VERDICT NEEDS_REVISION");
                    AssertContains("Architect missions must not emit `[ARMADA:RESULT]` or `[ARMADA:VERDICT]`", embeddedSignals, "embedded runtime signals should restrict architect output");
                    AssertNotContains("`[ARMADA:RESULT] PASS`", embeddedSignals, "embedded runtime signals");
                    AssertNotContains("`[ARMADA:RESULT] FAIL`", embeddedSignals, "embedded runtime signals");
                    AssertNotContains("`[ARMADA:RESULT] NEEDS_REVISION`", embeddedSignals, "embedded runtime signals");
                    AssertNotContains("`[ARMADA:VERDICT] COMPLETE`", embeddedSignals, "embedded runtime signals");

                    AssertContains("`[ARMADA:RESULT] COMPLETE`", fallbackSignals, "fallback runtime signals should include RESULT COMPLETE");
                    AssertContains("`[ARMADA:VERDICT] PASS`", fallbackSignals, "fallback runtime signals should include VERDICT PASS");
                    AssertContains("`[ARMADA:VERDICT] FAIL`", fallbackSignals, "fallback runtime signals should include VERDICT FAIL");
                    AssertContains("`[ARMADA:VERDICT] NEEDS_REVISION`", fallbackSignals, "fallback runtime signals should include VERDICT NEEDS_REVISION");
                    AssertContains("Architect missions must not emit `[ARMADA:RESULT]` or `[ARMADA:VERDICT]`", fallbackSignals, "fallback runtime signals should restrict architect output");
                    AssertNotContains("`[ARMADA:RESULT] PASS`", fallbackSignals, "fallback runtime signals");
                    AssertNotContains("`[ARMADA:RESULT] FAIL`", fallbackSignals, "fallback runtime signals");
                    AssertNotContains("`[ARMADA:RESULT] NEEDS_REVISION`", fallbackSignals, "fallback runtime signals");
                    AssertNotContains("`[ARMADA:VERDICT] COMPLETE`", fallbackSignals, "fallback runtime signals");
                }
            }));

            cases.Add(Case("structured_judge_verdict_parser_accepts_only_documented_verdict_signal_lines", "Structured judge verdict parser accepts only documented verdict signal lines", TestTags.Negative, () =>
            {
                AssertEqual("Pass", ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] PASS"), "PASS verdict should parse");
                AssertEqual("Fail", ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] FAIL"), "FAIL verdict should parse");
                AssertEqual("NeedsRevision", ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] NEEDS_REVISION"), "NEEDS_REVISION verdict should parse");

                AssertNull(ParseStructuredJudgeVerdictSignal("[ARMADA:RESULT] PASS"), "RESULT PASS should not parse as a judge verdict");
                AssertNull(ParseStructuredJudgeVerdictSignal("[ARMADA:RESULT] COMPLETE"), "RESULT COMPLETE should not parse as a judge verdict");
                AssertNull(ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] COMPLETE"), "VERDICT COMPLETE should not parse as a judge verdict");
            }));

            cases.Add(CaseAsync("architect_prompt_surfaces_consistently_forbid_result_and_verdict_lines", "Architect prompt surfaces consistently forbid result and verdict lines", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    string embeddedPrompt = await ResolveTemplateContentAsync(templates, "persona.architect").ConfigureAwait(false);

                    PromptFixtureResult fixture = CreatePromptFixture("Architect");
                    Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(fixture.Mission, fixture.Vessel, fixture.Captain, fixture.Dock);
                    string fallbackPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                        "Architect",
                        templateParams,
                        null).ConfigureAwait(false);
                    string launchPrompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                        fixture.Mission,
                        fixture.Vessel,
                        fixture.Captain,
                        fixture.Dock,
                        null).ConfigureAwait(false);

                    AssertContains("[ARMADA:MISSION]", embeddedPrompt, "architect embedded template should use mission markers");
                    AssertContains("Do not emit `[ARMADA:RESULT]` or `[ARMADA:VERDICT]` lines.", embeddedPrompt, "architect embedded template should forbid result and verdict lines");
                    AssertContains("[ARMADA:MISSION]", templateParams["CaptainInstructions"], "architect captain instructions should use mission markers");
                    AssertContains("Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.", templateParams["CaptainInstructions"], "architect captain instructions should forbid result and verdict lines");
                    AssertContains("[ARMADA:MISSION]", fallbackPrompt, "architect fallback prompt should use mission markers");
                    AssertContains("Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.", fallbackPrompt, "architect fallback prompt should forbid result and verdict lines");
                    AssertContains("[ARMADA:MISSION]", launchPrompt, "architect launch prompt should use mission markers");
                    AssertContains("Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.", launchPrompt, "architect launch prompt should forbid result and verdict lines");
                }
            }));

            cases.Add(CaseAsync("worker_test_engineer_and_judge_prompt_surfaces_keep_documented_signal_values", "Worker, test engineer, and judge prompt surfaces keep documented signal values", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);

                    await AssertResultPersonaSignalsAsync(templates, "Worker", "persona.worker").ConfigureAwait(false);
                    await AssertResultPersonaSignalsAsync(templates, "Product Manager", "persona.product_manager").ConfigureAwait(false);
                    await AssertResultPersonaSignalsAsync(templates, "Usability Engineer", "persona.usability_engineer").ConfigureAwait(false);
                    await AssertResultPersonaSignalsAsync(templates, "Test Engineer", "persona.test_engineer").ConfigureAwait(false);
                    await AssertResultPersonaSignalsAsync(templates, "Linter", "persona.linter").ConfigureAwait(false);
                    await AssertJudgeSignalsAsync(templates).ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Prompt Signal Consistency",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private sealed class PromptFixtureResult
        {
            public Mission Mission { get; set; } = null!;

            public Vessel Vessel { get; set; } = null!;

            public Captain Captain { get; set; } = null!;

            public Dock Dock { get; set; } = null!;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private static MissionService CreateMissionService(LoggingModule logging, DatabaseDriver db, ArmadaSettings settings)
        {
            StubGitService git = new StubGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            return new MissionService(logging, db, settings, dockService, captainService);
        }

        private static PromptFixtureResult CreatePromptFixture(string persona)
        {
            string slug = persona.ToLowerInvariant().Replace(" ", "-");
            Vessel vessel = new Vessel("PromptSignalVessel", "https://github.com/test/repo");
            Captain captain = new Captain(slug + "-captain");
            captain.Runtime = AgentRuntimeEnum.Codex;

            Mission mission = new Mission(persona + " mission", "Validate the signal contract.");
            mission.Persona = persona;
            mission.BranchName = "armada/" + slug + "-signal-contract";

            Dock dock = new Dock(vessel.Id);
            dock.BranchName = mission.BranchName;

            return new PromptFixtureResult
            {
                Mission = mission,
                Vessel = vessel,
                Captain = captain,
                Dock = dock
            };
        }

        private static async Task<string> ResolveTemplateContentAsync(PromptTemplateService templates, string templateName)
        {
            PromptTemplate? template = await templates.ResolveAsync(templateName).ConfigureAwait(false);
            AssertNotNull(template, templateName + " should resolve");
            return template!.Content;
        }

        private static string GetMissionServiceFallback(MissionService service, string templateName)
        {
            MethodInfo? method = typeof(MissionService).GetMethod(
                "GetHardcodedFallback",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("Could not find GetHardcodedFallback.");

            return (string?)method.Invoke(service, new object[] { templateName }) ?? String.Empty;
        }

        private static string? ParseStructuredJudgeVerdictSignal(string line)
        {
            MethodInfo? method = typeof(MissionService).GetMethod(
                "ParseStructuredJudgeVerdictSignal",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("Could not find ParseStructuredJudgeVerdictSignal.");

            return method.Invoke(null, new object[] { line })?.ToString();
        }

        private static void AssertNotContains(string expectedToBeAbsent, string actual, string context)
        {
            AssertFalse(
                actual.Contains(expectedToBeAbsent, StringComparison.Ordinal),
                context + " should not contain " + expectedToBeAbsent);
        }

        private static async Task AssertResultPersonaSignalsAsync(PromptTemplateService templates, string persona, string templateName)
        {
            string embeddedPrompt = await ResolveTemplateContentAsync(templates, templateName).ConfigureAwait(false);
            PromptFixtureResult fixture = CreatePromptFixture(persona);
            Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(fixture.Mission, fixture.Vessel, fixture.Captain, fixture.Dock);

            string fallbackPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                persona,
                templateParams,
                null).ConfigureAwait(false);
            string launchPrompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                fixture.Mission,
                fixture.Vessel,
                fixture.Captain,
                fixture.Dock,
                null).ConfigureAwait(false);

            AssertContains("`[ARMADA:RESULT] COMPLETE`", embeddedPrompt, templateName + " embedded template");
            AssertContains("[ARMADA:RESULT] COMPLETE", templateParams["CaptainInstructions"], persona + " captain instructions");
            AssertContains("[ARMADA:RESULT] COMPLETE", fallbackPrompt, persona + " fallback prompt");
            AssertContains("[ARMADA:RESULT] COMPLETE", launchPrompt, persona + " launch prompt");

            AssertNotContains("[ARMADA:VERDICT]", embeddedPrompt, templateName + " embedded template");
            AssertNotContains("[ARMADA:VERDICT]", templateParams["CaptainInstructions"], persona + " captain instructions");
            AssertNotContains("[ARMADA:VERDICT]", fallbackPrompt, persona + " fallback prompt");
            AssertNotContains("[ARMADA:VERDICT]", launchPrompt, persona + " launch prompt");
        }

        private static async Task AssertJudgeSignalsAsync(PromptTemplateService templates)
        {
            string embeddedPrompt = await ResolveTemplateContentAsync(templates, "persona.judge").ConfigureAwait(false);
            PromptFixtureResult fixture = CreatePromptFixture("Judge");
            Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(fixture.Mission, fixture.Vessel, fixture.Captain, fixture.Dock);

            string fallbackPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                "Judge",
                templateParams,
                null).ConfigureAwait(false);
            string launchPrompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                fixture.Mission,
                fixture.Vessel,
                fixture.Captain,
                fixture.Dock,
                null).ConfigureAwait(false);

            AssertContains("`[ARMADA:VERDICT] PASS`", embeddedPrompt, "judge embedded template PASS");
            AssertContains("`[ARMADA:VERDICT] FAIL`", embeddedPrompt, "judge embedded template FAIL");
            AssertContains("`[ARMADA:VERDICT] NEEDS_REVISION`", embeddedPrompt, "judge embedded template NEEDS_REVISION");

            AssertContains("[ARMADA:VERDICT] PASS", templateParams["CaptainInstructions"], "judge captain instructions PASS");
            AssertContains("[ARMADA:VERDICT] FAIL", templateParams["CaptainInstructions"], "judge captain instructions FAIL");
            AssertContains("[ARMADA:VERDICT] NEEDS_REVISION", templateParams["CaptainInstructions"], "judge captain instructions NEEDS_REVISION");

            AssertContains("[ARMADA:VERDICT] PASS", fallbackPrompt, "judge fallback PASS");
            AssertContains("[ARMADA:VERDICT] FAIL", fallbackPrompt, "judge fallback FAIL");
            AssertContains("[ARMADA:VERDICT] NEEDS_REVISION", fallbackPrompt, "judge fallback NEEDS_REVISION");

            AssertContains("[ARMADA:VERDICT] PASS", launchPrompt, "judge launch prompt PASS");
            AssertContains("[ARMADA:VERDICT] FAIL", launchPrompt, "judge launch prompt FAIL");
            AssertContains("[ARMADA:VERDICT] NEEDS_REVISION", launchPrompt, "judge launch prompt NEEDS_REVISION");

            AssertNotContains("[ARMADA:RESULT]", embeddedPrompt, "judge embedded template");
            AssertNotContains("[ARMADA:RESULT]", templateParams["CaptainInstructions"], "judge captain instructions");
            AssertNotContains("[ARMADA:RESULT]", fallbackPrompt, "judge fallback");
            AssertNotContains("[ARMADA:RESULT]", launchPrompt, "judge launch prompt");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
