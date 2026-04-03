namespace Armada.Test.Unit.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    public class PromptSignalConsistencyTests : TestSuite
    {
        public override string Name => "Prompt Signal Consistency";

        private LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private MissionService CreateMissionService(LoggingModule logging, SqliteDatabaseDriver db, ArmadaSettings settings)
        {
            StubGitService git = new StubGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            return new MissionService(logging, db, settings, dockService, captainService);
        }

        private static (Mission Mission, Vessel Vessel, Captain Captain, Dock Dock) CreatePromptFixture(string persona)
        {
            Vessel vessel = new Vessel("PromptSignalVessel", "https://github.com/test/repo");
            Captain captain = new Captain(persona.ToLowerInvariant() + "-captain");
            captain.Runtime = AgentRuntimeEnum.Codex;

            Mission mission = new Mission(persona + " mission", "Validate the signal contract.");
            mission.Persona = persona;
            mission.BranchName = "armada/" + persona.ToLowerInvariant() + "-signal-contract";

            Dock dock = new Dock(vessel.Id);
            dock.BranchName = mission.BranchName;

            return (mission, vessel, captain, dock);
        }

        private async Task<string> ResolveTemplateContentAsync(PromptTemplateService templates, string templateName)
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

        private void AssertNotContains(string expectedToBeAbsent, string actual, string context)
        {
            AssertFalse(
                actual.Contains(expectedToBeAbsent, StringComparison.Ordinal),
                context + " should not contain " + expectedToBeAbsent);
        }

        private async Task AssertResultPersonaSignalsAsync(PromptTemplateService templates, string persona, string templateName)
        {
            string embeddedPrompt = await ResolveTemplateContentAsync(templates, templateName).ConfigureAwait(false);
            (Mission mission, Vessel vessel, Captain captain, Dock dock) = CreatePromptFixture(persona);
            Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(mission, vessel, captain, dock);

            string fallbackPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                persona,
                templateParams,
                null).ConfigureAwait(false);
            string launchPrompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                mission,
                vessel,
                captain,
                dock,
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

        private async Task AssertJudgeSignalsAsync(PromptTemplateService templates)
        {
            string embeddedPrompt = await ResolveTemplateContentAsync(templates, "persona.judge").ConfigureAwait(false);
            (Mission mission, Vessel vessel, Captain captain, Dock dock) = CreatePromptFixture("Judge");
            Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(mission, vessel, captain, dock);

            string fallbackPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                "Judge",
                templateParams,
                null).ConfigureAwait(false);
            string launchPrompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                mission,
                vessel,
                captain,
                dock,
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

        protected override async Task RunTestsAsync()
        {
            await RunTest("MissionService fallback progress signals match embedded result and verdict contract", async () =>
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
            });

            await RunTest("Structured judge verdict parser accepts only documented verdict signal lines", () =>
            {
                AssertEqual("Pass", ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] PASS"), "PASS verdict should parse");
                AssertEqual("Fail", ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] FAIL"), "FAIL verdict should parse");
                AssertEqual("NeedsRevision", ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] NEEDS_REVISION"), "NEEDS_REVISION verdict should parse");

                AssertNull(ParseStructuredJudgeVerdictSignal("[ARMADA:RESULT] PASS"), "RESULT PASS should not parse as a judge verdict");
                AssertNull(ParseStructuredJudgeVerdictSignal("[ARMADA:RESULT] COMPLETE"), "RESULT COMPLETE should not parse as a judge verdict");
                AssertNull(ParseStructuredJudgeVerdictSignal("[ARMADA:VERDICT] COMPLETE"), "VERDICT COMPLETE should not parse as a judge verdict");
            });

            await RunTest("Architect prompt surfaces consistently forbid result and verdict lines", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    string embeddedPrompt = await ResolveTemplateContentAsync(templates, "persona.architect").ConfigureAwait(false);

                    (Mission mission, Vessel vessel, Captain captain, Dock dock) = CreatePromptFixture("Architect");
                    Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(mission, vessel, captain, dock);
                    string fallbackPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                        "Architect",
                        templateParams,
                        null).ConfigureAwait(false);
                    string launchPrompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                        mission,
                        vessel,
                        captain,
                        dock,
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
            });

            await RunTest("Worker, test engineer, and judge prompt surfaces keep documented signal values", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);

                    await AssertResultPersonaSignalsAsync(templates, "Worker", "persona.worker").ConfigureAwait(false);
                    await AssertResultPersonaSignalsAsync(templates, "TestEngineer", "persona.test_engineer").ConfigureAwait(false);
                    await AssertJudgeSignalsAsync(templates).ConfigureAwait(false);
                }
            });
        }
    }
}
