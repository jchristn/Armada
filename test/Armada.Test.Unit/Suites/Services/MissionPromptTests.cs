namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    public class MissionPromptTests : TestSuite
    {
        public override string Name => "Mission Prompt (ProjectContext/StyleGuide/ModelContext)";

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

        private MissionService CreateMissionService(LoggingModule logging, SqliteDatabaseDriver db, ArmadaSettings settings, StubGitService git)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            return new MissionService(logging, db, settings, dockService, captainService);
        }

        private MissionService CreateMissionServiceWithTemplates(LoggingModule logging, SqliteDatabaseDriver db, ArmadaSettings settings, StubGitService git, out IPromptTemplateService templateService)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            templateService = new PromptTemplateService(db, logging);
            return new MissionService(logging, db, settings, dockService, captainService, templateService);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("GenerateClaudeMdAsync includes ProjectContext when set", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("PromptVessel", "https://github.com/test/repo");
                        vessel.ProjectContext = "This is a React TypeScript frontend with Redux state management.";

                        Mission mission = new Mission();
                        mission.Title = "Fix login bug";
                        mission.Description = "The login form does not validate email addresses.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Project Context", content);
                        AssertContains("This is a React TypeScript frontend with Redux state management.", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync writes runtime-specific instruction file", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("CodexVessel", "https://github.com/test/repo");
                        Captain captain = new Captain("CodexCaptain");
                        captain.Runtime = AgentRuntimeEnum.Codex;

                        Mission mission = new Mission();
                        mission.Title = "Implement feature";
                        mission.Description = "Use runtime-specific instruction files.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel, captain);

                        AssertTrue(File.Exists(Path.Combine(tempDir, "CODEX.md")), "Codex missions should write CODEX.md");
                        AssertFalse(File.Exists(Path.Combine(tempDir, "CLAUDE.md")), "Codex missions should not write CLAUDE.md by default");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync includes StyleGuide when set", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("StyleVessel", "https://github.com/test/repo");
                        vessel.StyleGuide = "Use camelCase for variables. Prefer const over let.";

                        Mission mission = new Mission();
                        mission.Title = "Add feature";
                        mission.Description = "Add dark mode toggle.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Code Style", content);
                        AssertContains("Use camelCase for variables. Prefer const over let.", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync includes both ProjectContext and StyleGuide", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("BothFieldsVessel", "https://github.com/test/repo");
                        vessel.ProjectContext = "Go microservice with gRPC endpoints.";
                        vessel.StyleGuide = "Follow Effective Go guidelines.";

                        Mission mission = new Mission();
                        mission.Title = "Refactor handler";
                        mission.Description = "Refactor the user handler to use middleware.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Project Context", content);
                        AssertContains("Go microservice with gRPC endpoints.", content);
                        AssertContains("## Code Style", content);
                        AssertContains("Follow Effective Go guidelines.", content);
                        AssertContains("# Mission Instructions", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync omits ProjectContext section when null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("NoContextVessel", "https://github.com/test/repo");

                        Mission mission = new Mission();
                        mission.Title = "Simple task";
                        mission.Description = "Do something.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertFalse(content.Contains("## Project Context"), "Should not contain Project Context section when null");
                        AssertFalse(content.Contains("## Code Style"), "Should not contain Code Style section when null");
                        AssertContains("# Mission Instructions", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync omits sections when empty string", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("EmptyContextVessel", "https://github.com/test/repo");
                        vessel.ProjectContext = "";
                        vessel.StyleGuide = "";

                        Mission mission = new Mission();
                        mission.Title = "Empty context task";
                        mission.Description = "Task with empty context fields.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertFalse(content.Contains("## Project Context"), "Should not contain Project Context section when empty");
                        AssertFalse(content.Contains("## Code Style"), "Should not contain Code Style section when empty");
                        AssertContains("# Mission Instructions", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync ProjectContext appears before Mission Instructions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("OrderVessel", "https://github.com/test/repo");
                        vessel.ProjectContext = "Order test context";
                        vessel.StyleGuide = "Order test style";

                        Mission mission = new Mission();
                        mission.Title = "Order test";
                        mission.Description = "Test ordering.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        int contextIndex = content.IndexOf("## Project Context");
                        int styleIndex = content.IndexOf("## Code Style");
                        int missionIndex = content.IndexOf("# Mission Instructions");

                        AssertTrue(contextIndex >= 0, "Project Context should exist");
                        AssertTrue(styleIndex >= 0, "Code Style should exist");
                        AssertTrue(missionIndex >= 0, "Mission Instructions should exist");
                        AssertTrue(contextIndex < styleIndex, "Project Context should appear before Code Style");
                        AssertTrue(styleIndex < missionIndex, "Code Style should appear before Mission Instructions");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync includes ModelContext when enabled and set", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("ModelContextVessel", "https://github.com/test/repo");
                        vessel.EnableModelContext = true;
                        vessel.ModelContext = "The test suite takes 4 minutes. Auth module was recently refactored.";

                        Mission mission = new Mission();
                        mission.Title = "Fix tests";
                        mission.Description = "Fix broken integration tests.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Model Context", content);
                        AssertContains("The test suite takes 4 minutes.", content);
                        AssertContains("## Model Context Updates", content);
                        AssertContains("armada_update_vessel_context", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync omits ModelContext when disabled", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("DisabledModelContextVessel", "https://github.com/test/repo");
                        vessel.EnableModelContext = false;
                        vessel.ModelContext = "This should not appear.";

                        Mission mission = new Mission();
                        mission.Title = "Task";
                        mission.Description = "Do something.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertFalse(content.Contains("## Model Context"), "Should not contain Model Context when disabled");
                        AssertFalse(content.Contains("## Model Context Updates"), "Should not contain Model Context Updates when disabled");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync includes update instructions even when ModelContext is empty", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("EmptyModelContextVessel", "https://github.com/test/repo");
                        vessel.EnableModelContext = true;
                        vessel.ModelContext = null;

                        Mission mission = new Mission();
                        mission.Title = "First mission";
                        mission.Description = "First mission on this vessel.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertFalse(content.Contains("## Model Context\n"), "Should not contain Model Context section when null");
                        AssertContains("## Model Context Updates", content);
                        AssertContains("armada_update_vessel_context", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("Template-resolved CLAUDE.md contains mission rules", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IPromptTemplateService templateService;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out templateService);
                    await templateService.SeedDefaultsAsync();

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("TemplateRulesVessel", "https://github.com/test/repo");

                        Mission mission = new Mission();
                        mission.Title = "Template rules test";
                        mission.Description = "Verify rules section from templates.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Rules", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("Template-resolved CLAUDE.md contains structured result and verdict markers", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IPromptTemplateService templateService;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out templateService);
                    await templateService.SeedDefaultsAsync();

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("SignalPromptVessel", "https://github.com/test/repo");

                        Mission mission = new Mission();
                        mission.Title = "Judge signal test";
                        mission.Description = "Verify structured output markers are present.";
                        mission.Persona = "Judge";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("[ARMADA:RESULT] COMPLETE", content);
                        AssertContains("[ARMADA:VERDICT] PASS", content);
                        AssertContains("standalone", content.ToLowerInvariant());
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("Template-resolved persona prompts require structured test and judge analysis", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IPromptTemplateService templateService;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out templateService);
                    await templateService.SeedDefaultsAsync();

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("PersonaPromptVessel", "https://github.com/test/repo");

                        Mission judgeMission = new Mission();
                        judgeMission.Title = "Judge structure test";
                        judgeMission.Description = "Verify judge review requirements.";
                        judgeMission.Persona = "Judge";

                        await service.GenerateClaudeMdAsync(tempDir, judgeMission, vessel);

                        string judgeContent = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Completeness", judgeContent, "Judge prompt should require a Completeness section");
                        AssertContains("## Failure Modes", judgeContent, "Judge prompt should require a Failure Modes section");
                        AssertContains("PASS is not allowed", judgeContent, "Judge prompt should constrain PASS approvals");

                        Mission testMission = new Mission();
                        testMission.Title = "Test coverage structure test";
                        testMission.Description = "Verify test engineer requirements.";
                        testMission.Persona = "TestEngineer";

                        await service.GenerateClaudeMdAsync(tempDir, testMission, vessel);

                        string testContent = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("negative or edge-path", testContent, "Test engineer prompt should require negative-path coverage");
                        AssertContains("## Coverage Added", testContent, "Test engineer prompt should request a coverage summary section");
                        AssertContains("## Residual Risks", testContent, "Test engineer prompt should request residual risk reporting");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync appends judge output contract after custom captain instructions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IPromptTemplateService templateService;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out templateService);
                    await templateService.SeedDefaultsAsync();

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("JudgeCaptainPromptVessel", "https://github.com/test/repo");

                        Mission mission = new Mission();
                        mission.Title = "Judge custom instruction contract test";
                        mission.Description = "Verify custom judge instructions still include the required structured output contract.";
                        mission.Persona = "Judge";

                        Captain captain = new Captain("judge-captain");
                        captain.Runtime = Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode;
                        captain.SystemInstructions = "End with exactly one standalone verdict line and give a brief explanation.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel, captain);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Captain Instructions", content, "Custom captain instructions should still be included");
                        AssertContains("brief explanation", content, "Original captain instruction text should be preserved");
                        AssertContains("## Required Output Contract", content, "Generated instructions should append a structured output contract");
                        AssertContains("## Completeness", content, "Judge output contract should require Completeness");
                        AssertContains("## Failure Modes", content, "Judge output contract should require Failure Modes");
                        AssertContains("[ARMADA:VERDICT] PASS", content, "Judge output contract should preserve the standalone verdict signal");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("Template-resolved CLAUDE.md contains persona prompt", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IPromptTemplateService templateService;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out templateService);
                    await templateService.SeedDefaultsAsync();

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("PersonaVessel", "https://github.com/test/repo");

                        Mission mission = new Mission();
                        mission.Title = "Architect persona test";
                        mission.Description = "Verify architect persona prompt.";
                        mission.Persona = "Architect";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertTrue(
                            content.Contains("decompose") || content.Contains("analyze"),
                            "Architect persona should contain 'decompose' or 'analyze'");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("Template-resolved CLAUDE.md contains model context updates when enabled", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IPromptTemplateService templateService;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out templateService);
                    await templateService.SeedDefaultsAsync();

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("TemplateModelContextVessel", "https://github.com/test/repo");
                        vessel.EnableModelContext = true;
                        vessel.ModelContext = "The auth module was recently refactored to use JWT tokens.";

                        Mission mission = new Mission();
                        mission.Title = "Model context test";
                        mission.Description = "Verify model context section from templates.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Model Context Updates", content);
                        AssertContains("armada_update_vessel_context", content);
                        AssertContains("The auth module was recently refactored to use JWT tokens.", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("Template-resolved CLAUDE.md substitutes placeholders", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IPromptTemplateService templateService;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out templateService);
                    await templateService.SeedDefaultsAsync();

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        Vessel vessel = new Vessel("PlaceholderTestVessel", "https://github.com/test/repo");

                        Mission mission = new Mission();
                        mission.Title = "Implement user authentication";
                        mission.Description = "Add OAuth2 login flow.";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("Implement user authentication", content);
                        AssertContains("PlaceholderTestVessel", content);
                        AssertFalse(content.Contains("{MissionTitle}"), "Should not contain literal {MissionTitle} placeholder");
                        AssertFalse(content.Contains("{VesselName}"), "Should not contain literal {VesselName} placeholder");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync strips stale Armada mission blocks from existing instructions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        string existingInstructions =
                            "## Project Context\n" +
                            "Stable project guidance.\n" +
                            "\n" +
                            "## Code Style\n" +
                            "Use explicit types.\n" +
                            "\n" +
                            "# Mission Instructions\n" +
                            "\n" +
                            "## Mission\n" +
                            "- **Title:** Stale mission title\n" +
                            "\n" +
                            "[ARMADA:MISSION] Old task\n";
                        await File.WriteAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"), existingInstructions);

                        Vessel vessel = new Vessel("ExistingInstructionsVessel", "https://github.com/test/repo");
                        Mission mission = new Mission("Fresh mission", "Fresh description.");

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains("## Existing Project Instructions", content);
                        AssertContains("Stable project guidance.", content);
                        AssertFalse(content.Contains("Stale mission title"), "Generated mission blocks from the existing file should be stripped");
                        AssertTrue(
                            content.IndexOf("## Existing Project Instructions", StringComparison.Ordinal) ==
                            content.LastIndexOf("## Existing Project Instructions", StringComparison.Ordinal),
                            "Existing project instructions should be wrapped only once");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("GenerateClaudeMdAsync omits empty existing instruction wrapper after sanitization", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, git);

                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        string existingInstructions =
                            "# Mission Instructions\n" +
                            "\n" +
                            "## Mission\n" +
                            "- **Title:** Generated only\n";
                        await File.WriteAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"), existingInstructions);

                        Vessel vessel = new Vessel("GeneratedOnlyInstructionsVessel", "https://github.com/test/repo");
                        Mission mission = new Mission("Fresh mission", "Fresh description.");

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertFalse(content.Contains("## Existing Project Instructions"), "Empty sanitized instructions should not be wrapped");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            });

            await RunTest("Shared launch prompt builder produces compact prompt and defers to runtime instruction file", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IPromptTemplateService templateService = new PromptTemplateService(testDb.Driver, logging);
                    await templateService.SeedDefaultsAsync();

                    Vessel vessel = new Vessel("LaunchPromptVessel", "https://github.com/test/repo");
                    vessel.ProjectContext = "Service-oriented C# backend.";
                    vessel.StyleGuide = "Prefer explicit types.";
                    vessel.EnableModelContext = true;
                    vessel.ModelContext = "Background jobs are scheduled from ArmadaServer.";

                    Captain captain = new Captain("prompt-captain");
                    captain.Runtime = AgentRuntimeEnum.Codex;
                    captain.SystemInstructions = "Be concise and careful.";

                    Mission mission = new Mission("Write tests", "Add unit tests for the service layer.");
                    mission.Persona = "TestEngineer";
                    mission.BranchName = "armada/prompt-captain/msn_test";

                    Dock dock = new Dock(vessel.Id);
                    dock.BranchName = mission.BranchName;

                    string prompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                        mission, vessel, captain, dock, templateService).ConfigureAwait(false);

                    AssertContains("test engineer", prompt.ToLowerInvariant());
                    AssertContains("Write tests", prompt);
                    AssertContains("CODEX.md", prompt);
                    AssertFalse(prompt.Contains("CLAUDE.md"), "Non-Claude runtimes should not be pointed at CLAUDE.md");
                    AssertFalse(prompt.Contains("Be concise and careful."), "Launch prompt should defer captain instructions to the runtime instruction file");
                    AssertFalse(prompt.Contains("Service-oriented C# backend."), "Launch prompt should defer project context to the runtime instruction file");
                    AssertFalse(prompt.Contains("Prefer explicit types."), "Launch prompt should defer style guide to the runtime instruction file");
                    AssertFalse(prompt.Contains("Background jobs are scheduled from ArmadaServer."), "Launch prompt should defer model context to the runtime instruction file");
                }
            });

            await RunTest("Shared launch prompt builder caps oversized prompts", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IPromptTemplateService templateService = new PromptTemplateService(testDb.Driver, logging);
                    await templateService.SeedDefaultsAsync();

                    Vessel vessel = new Vessel("LargePromptVessel", "https://github.com/test/repo");
                    vessel.ProjectContext = new string('P', 5000);
                    vessel.StyleGuide = new string('S', 5000);
                    vessel.EnableModelContext = true;
                    vessel.ModelContext = new string('M', 5000);

                    Captain captain = new Captain("large-prompt-captain");
                    captain.Runtime = AgentRuntimeEnum.Gemini;
                    captain.SystemInstructions = new string('I', 2000);

                    Mission mission = new Mission("Large mission", new string('D', 20000));
                    mission.Persona = "Architect";
                    mission.BranchName = "armada/large-prompt";

                    Dock dock = new Dock(vessel.Id);
                    dock.BranchName = mission.BranchName;

                    string prompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                        mission, vessel, captain, dock, templateService).ConfigureAwait(false);

                    AssertTrue(prompt.Length <= 6000, "Launch prompt should stay under the hard cap");
                    AssertContains("GEMINI.md", prompt);
                    AssertContains("Large mission", prompt);
                }
            });

            await RunTest("Architect launch prompt explicitly requires ARMADA mission markers", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IPromptTemplateService templateService = new PromptTemplateService(testDb.Driver, logging);
                    await templateService.SeedDefaultsAsync();

                    Vessel vessel = new Vessel("ArchitectVessel", "https://github.com/test/repo");
                    Captain captain = new Captain("architect-captain");
                    captain.Runtime = AgentRuntimeEnum.ClaudeCode;

                    Mission mission = new Mission("Plan work", "Break this objective into missions.");
                    mission.Persona = "Architect";
                    mission.BranchName = "armada/architect";

                    Dock dock = new Dock(vessel.Id);
                    dock.BranchName = mission.BranchName;

                    string prompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                        mission, vessel, captain, dock, templateService).ConfigureAwait(false);

                    AssertContains("[ARMADA:MISSION]", prompt);
                    AssertContains("Do not ask for more input.", prompt);
                    AssertContains("respond only with real [ARMADA:MISSION] blocks", prompt);
                }
            });
        }
    }
}
