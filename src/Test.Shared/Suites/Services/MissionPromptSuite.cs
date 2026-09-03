namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
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
    /// Descriptors for mission prompt generation: <see cref="MissionService.GenerateClaudeMdAsync"/>
    /// project-context/style-guide/model-context composition, runtime-specific instruction files,
    /// section ordering and de-duplication, stale mission-block sanitization, persona output
    /// contracts, and the shared <see cref="MissionPromptBuilder"/> launch prompt (compaction,
    /// hard cap, and persona-specific signal markers). Each case runs over a fresh SQLite store
    /// with a stub git service and cleans up its temp working directory.
    /// </summary>
    public sealed class MissionPromptSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MissionPrompt";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Mission Prompt suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("generate_claude_md_async_includes_project_context_when_set", "GenerateClaudeMdAsync includes ProjectContext when set", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_writes_runtime_specific_instruction_file", "GenerateClaudeMdAsync writes runtime-specific instruction file", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_includes_style_guide_when_set", "GenerateClaudeMdAsync includes StyleGuide when set", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_includes_both_project_context_and_style_guide", "GenerateClaudeMdAsync includes both ProjectContext and StyleGuide", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_omits_project_context_section_when_null", "GenerateClaudeMdAsync omits ProjectContext section when null", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_omits_sections_when_empty_string", "GenerateClaudeMdAsync omits sections when empty string", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_project_context_appears_before_mission_instructions", "GenerateClaudeMdAsync ProjectContext appears before Mission Instructions", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_includes_model_context_when_enabled_and_set", "GenerateClaudeMdAsync includes ModelContext when enabled and set", TestTags.Positive, async () =>
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
                        AssertContains("update_vessel_context", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("generate_claude_md_async_omits_model_context_when_disabled", "GenerateClaudeMdAsync omits ModelContext when disabled", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_includes_update_instructions_even_when_model_context_is_empty", "GenerateClaudeMdAsync includes update instructions even when ModelContext is empty", TestTags.Positive, async () =>
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
                        AssertContains("update_vessel_context", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("template_resolved_claude_md_contains_mission_rules", "Template-resolved CLAUDE.md contains mission rules", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("template_resolved_claude_md_contains_structured_result_and_verdict_markers", "Template-resolved CLAUDE.md contains structured result and verdict markers", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("template_resolved_claude_md_de_duplicates_shared_context_sections", "Template-resolved CLAUDE.md de-duplicates shared context sections", TestTags.Positive, async () =>
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
                        Vessel vessel = new Vessel("DedupVessel", "https://github.com/test/repo");
                        vessel.ProjectContext = "Service-oriented C# backend.";
                        vessel.StyleGuide = "Prefer explicit types.";
                        vessel.EnableModelContext = true;
                        vessel.ModelContext = "Background jobs are scheduled from ArmadaServer.";

                        Captain captain = new Captain("architect-prompt-captain");
                        captain.Runtime = AgentRuntimeEnum.Codex;
                        captain.SystemInstructions = "Be concise and careful.";

                        Mission mission = new Mission("Plan work", "Break this objective into missions.");
                        mission.Persona = "Architect";

                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel, captain);

                        string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CODEX.md"));
                        AssertEqual(1, Regex.Matches(content, "^## Project Context$", RegexOptions.Multiline).Count);
                        AssertEqual(1, Regex.Matches(content, "^## Code Style$", RegexOptions.Multiline).Count);
                        AssertEqual(1, Regex.Matches(content, "^## Model Context$", RegexOptions.Multiline).Count);
                        AssertEqual(1, Regex.Matches(content, "^## Repository$", RegexOptions.Multiline).Count);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("template_resolved_persona_prompts_require_structured_test_and_judge_analysis", "Template-resolved persona prompts require structured test and judge analysis", TestTags.Positive, async () =>
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
                        AssertContains("## Correctness", judgeContent, "Judge prompt should require a Correctness lens section");
                        AssertContains("## Blast Radius", judgeContent, "Judge prompt should require a Blast Radius lens section");
                        AssertContains("## Source Fidelity", judgeContent, "Judge prompt should require a Source Fidelity lens section");
                        AssertContains("## Affected Case", judgeContent, "Judge prompt should require a concrete affected case to block");

                        Mission testMission = new Mission();
                        testMission.Title = "Test coverage structure test";
                        testMission.Description = "Verify test engineer requirements.";
                        testMission.Persona = "Test Engineer";

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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_appends_judge_output_contract_after_custom_captain_instructions", "GenerateClaudeMdAsync appends judge output contract after custom captain instructions", TestTags.Positive, async () =>
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
                        AssertContains("## Correctness", content, "Judge output contract should require the Correctness lens");
                        AssertContains("## Blast Radius", content, "Judge output contract should require the Blast Radius lens");
                        AssertContains("## Affected Case", content, "Judge output contract should require a concrete affected case to block");
                        AssertContains("[ARMADA:VERDICT] PASS", content, "Judge output contract should preserve the standalone verdict signal");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("template_resolved_claude_md_contains_persona_prompt", "Template-resolved CLAUDE.md contains persona prompt", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("template_resolved_claude_md_contains_model_context_updates_when_enabled", "Template-resolved CLAUDE.md contains model context updates when enabled", TestTags.Positive, async () =>
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
                        AssertContains("update_vessel_context", content);
                        AssertContains("The auth module was recently refactored to use JWT tokens.", content);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("template_resolved_claude_md_substitutes_placeholders", "Template-resolved CLAUDE.md substitutes placeholders", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_strips_stale_armada_mission_blocks_from_existing_instructions", "GenerateClaudeMdAsync strips stale Armada mission blocks from existing instructions", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("generate_claude_md_async_omits_empty_existing_instruction_wrapper_after_sanitization", "GenerateClaudeMdAsync omits empty existing instruction wrapper after sanitization", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("shared_launch_prompt_builder_produces_compact_prompt_and_defers_to_runtime_instruction_file", "Shared launch prompt builder produces compact prompt and defers to runtime instruction file", TestTags.Positive, async () =>
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
                    mission.Persona = "Test Engineer";
                    mission.BranchName = "armada/prompt-captain/msn_test";

                    Dock dock = new Dock(vessel.Id);
                    dock.BranchName = mission.BranchName;

                    string prompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                        mission, vessel, captain, dock, templateService).ConfigureAwait(false);

                    AssertContains("test engineer", prompt.ToLowerInvariant());
                    AssertContains("## Coverage Added", prompt);
                    AssertContains("[ARMADA:RESULT] COMPLETE", prompt);
                    AssertContains("Write tests", prompt);
                    AssertContains("CODEX.md", prompt);
                    AssertFalse(prompt.Contains("CLAUDE.md"), "Non-Claude runtimes should not be pointed at CLAUDE.md");
                    AssertFalse(prompt.Contains("Be concise and careful."), "Launch prompt should defer captain instructions to the runtime instruction file");
                    AssertFalse(prompt.Contains("Service-oriented C# backend."), "Launch prompt should defer project context to the runtime instruction file");
                    AssertFalse(prompt.Contains("Prefer explicit types."), "Launch prompt should defer style guide to the runtime instruction file");
                    AssertFalse(prompt.Contains("Background jobs are scheduled from ArmadaServer."), "Launch prompt should defer model context to the runtime instruction file");
                }
            }));

            cases.Add(CaseAsync("shared_launch_prompt_builder_caps_oversized_prompts", "Shared launch prompt builder caps oversized prompts", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("architect_launch_prompt_explicitly_requires_armada_mission_markers", "Architect launch prompt explicitly requires ARMADA mission markers", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("judge_launch_prompt_repeats_structured_verdict_contract", "Judge launch prompt repeats structured verdict contract", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IPromptTemplateService templateService = new PromptTemplateService(testDb.Driver, logging);
                    await templateService.SeedDefaultsAsync();

                    Vessel vessel = new Vessel("JudgeLaunchVessel", "https://github.com/test/repo");
                    Captain captain = new Captain("judge-launch-captain");
                    captain.Runtime = AgentRuntimeEnum.ClaudeCode;
                    captain.SystemInstructions = "End with exactly one standalone verdict line and give a brief explanation.";

                    Mission mission = new Mission("Review work", "Assess the submitted change.");
                    mission.Persona = "Judge";
                    mission.BranchName = "armada/judge-launch";

                    Dock dock = new Dock(vessel.Id);
                    dock.BranchName = mission.BranchName;

                    string prompt = await MissionPromptBuilder.BuildLaunchPromptAsync(
                        mission, vessel, captain, dock, templateService).ConfigureAwait(false);

                    AssertContains("## Correctness", prompt);
                    AssertContains("## Blast Radius", prompt);
                    AssertContains("[ARMADA:VERDICT] PASS", prompt);
                    AssertContains("follow it exactly", prompt);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mission Prompt (ProjectContext/StyleGuide/ModelContext)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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

        private static MissionService CreateMissionService(LoggingModule logging, DatabaseDriver db, ArmadaSettings settings, StubGitService git)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            return new MissionService(logging, db, settings, dockService, captainService);
        }

        private static MissionService CreateMissionServiceWithTemplates(LoggingModule logging, DatabaseDriver db, ArmadaSettings settings, StubGitService git, out IPromptTemplateService templateService)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            templateService = new PromptTemplateService(db, logging);
            return new MissionService(logging, db, settings, dockService, captainService, templateService);
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
