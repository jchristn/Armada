namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Database.Sqlite;
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
        }
    }
}
