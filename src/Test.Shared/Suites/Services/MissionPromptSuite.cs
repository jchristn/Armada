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
    using Armada.Runtimes;
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

        private const string CommitInstructionSentence = "Commit all changes to the current branch";

        private const string PushInstructionSentence = "Commit and push your changes";

        /// <summary>
        /// Exact <c>mission.rules</c> content shipped with an unconditional captain push. Seeding must
        /// replace this built-in text. Any other built-in text stays in place.
        /// </summary>
        private const string ShippedMissionRulesWithUnconditionalPush =
            "## Rules\n" +
            "- Work only within this worktree directory\n" +
            "- Stay strictly within the mission scope and listed files\n" +
            "- Do not create, modify, or delete files outside the listed scope unless the mission explicitly requires it\n" +
            "- If you discover a necessary out-of-scope change, report it in your result instead of expanding scope on your own\n" +
            "- Commit all changes to the current branch\n" +
            "- Commit and push your changes -- the Admiral will also push if needed\n" +
            "- If you encounter a blocking issue, commit what you have and exit\n" +
            "- Exit with code 0 on success\n" +
            "- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages\n" +
            "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n" +
            "- You cannot ask the operator questions during a mission and nobody will answer them: when a choice has a sensible default (for example the newest target framework of a multi-targeted .NET project whose SDK is installed; `dotnet --list-sdks` shows them), make it, say what you chose in your result, and continue\n" +
            "- Stop and report instead of choosing only when the choice is risky or hard to undo, or the mission could mean materially different things\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Mission Prompt suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("mission_rules_push_instruction_matches_landing_mode", "Mission rules only instruct the captain to push for pull requests", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.LandingMode = LandingModeEnum.MergeAndPush;
                    StubGitService git = new StubGitService();
                    MissionService fallbackService = CreateMissionService(logging, testDb.Driver, settings, git);
                    MissionService templateService = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, git, out IPromptTemplateService templates);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    Vessel vessel = new Vessel("LandingModeRulesVessel", "https://github.com/example/landing-mode-rules");
                    LandingModeEnum[] modes = new LandingModeEnum[]
                    {
                        LandingModeEnum.LocalMerge,
                        LandingModeEnum.MergeQueue,
                        LandingModeEnum.None,
                        LandingModeEnum.MergeAndPush,
                        LandingModeEnum.PullRequest,
                        (LandingModeEnum)999
                    };

                    foreach (MissionService service in new MissionService[] { fallbackService, templateService })
                    {
                        foreach (LandingModeEnum mode in modes)
                        {
                            vessel.LandingMode = mode;
                            Mission mission = new Mission("Prompt landing mode " + mode, "Check the landing instructions.");
                            string content = await ReadGeneratedInstructionsAsync(service, "landing-mode-rules", mission, vessel).ConfigureAwait(false);
                            bool expectsPush = mode == LandingModeEnum.PullRequest || (int)mode == 999;
                            AssertEqual(expectsPush, content.Contains(PushInstructionSentence, StringComparison.Ordinal), mode + " captain push instruction");
                            AssertTrue(content.Contains(CommitInstructionSentence, StringComparison.Ordinal), mode + " captain commit instruction");
                        }
                    }

                    vessel.LandingMode = LandingModeEnum.LocalMerge;
                    Mission audit = new Mission("Read only audit prompt", "Inspect the work.") { Mode = MissionModeEnum.Audit };
                    string auditContent = await ReadGeneratedInstructionsAsync(templateService, "audit-mode-rules", audit, vessel).ConfigureAwait(false);
                    AssertContains("READ-ONLY", auditContent);
                    AssertFalse(auditContent.Contains(CommitInstructionSentence, StringComparison.Ordinal), "Audit must not receive commit instructions");
                    AssertFalse(auditContent.Contains(PushInstructionSentence, StringComparison.Ordinal), "Audit must not receive push instructions");

                    Mission research = new Mission("Read only research prompt", "Investigate the implementation.") { Mode = MissionModeEnum.Research };
                    string researchContent = await ReadGeneratedInstructionsAsync(templateService, "research-mode-rules", research, vessel).ConfigureAwait(false);
                    AssertContains("READ-ONLY", researchContent);
                    AssertFalse(researchContent.Contains(CommitInstructionSentence, StringComparison.Ordinal), "Research must not receive commit instructions");
                    AssertFalse(researchContent.Contains(PushInstructionSentence, StringComparison.Ordinal), "Research must not receive push instructions");
                }
            }));

            cases.Add(CaseAsync("mission_rules_untouched_builtin_follows_landing_mode", "Seeding an untouched built-in mission rules template follows the landing mode", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    await testDb.Driver.PromptTemplates.CreateAsync(new PromptTemplate("mission.rules", ShippedMissionRulesWithUnconditionalPush) { IsBuiltIn = true }).ConfigureAwait(false);
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.LandingMode = LandingModeEnum.PullRequest;
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, settings, new StubGitService(), out IPromptTemplateService templates);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    Vessel vessel = new Vessel("UntouchedMissionRulesVessel", "https://github.com/example/untouched-mission-rules");
                    vessel.LandingMode = LandingModeEnum.LocalMerge;
                    Mission mission = new Mission("Untouched rules landing mode", "Check the stored built-in rules.");
                    string content = await ReadGeneratedInstructionsAsync(service, "untouched-mission-rules", mission, vessel).ConfigureAwait(false);
                    AssertTrue(content.Contains(CommitInstructionSentence, StringComparison.Ordinal), "Untouched built-in rules still ask for a commit");
                    AssertFalse(content.Contains(PushInstructionSentence, StringComparison.Ordinal), "Untouched built-in rules must not ask for a captain push on local merge");
                }
            }));

            cases.Add(CaseAsync("mission_rules_seed_keeps_custom_builtin_content", "Seeding mission rules preserves operator content", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    string customContent = "## Rules\n- CUSTOM_OPERATOR_RULE_LANDING_MODE\n";
                    await testDb.Driver.PromptTemplates.CreateAsync(new PromptTemplate("mission.rules", customContent) { IsBuiltIn = true }).ConfigureAwait(false);
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, CreateLogging());
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    PromptTemplate? resolved = await testDb.Driver.PromptTemplates.ReadByNameAsync("mission.rules").ConfigureAwait(false);
                    AssertNotNull(resolved, "mission.rules should remain present");
                    AssertEqual(customContent, resolved!.Content, "Custom built-in content must not be overwritten");
                }
            }));

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

            cases.Add(CaseAsync("pi_mission_writes_agents_override_carrying_agents_md", "A pi mission writes AGENTS.override.md with the instructions followed by the repository's AGENTS.md; a pi launch sees it and git status stays clean", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    MissionService service = CreateMissionService(logging, testDb.Driver, CreateSettings(), new StubGitService());
                    string root = Path.Combine(Path.GetTempPath(), "armada_pi_override_" + Guid.NewGuid().ToString("N"));
                    string tempDir = Path.Combine(root, "dock");
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        Git(tempDir, "init", "-q");
                        File.WriteAllText(Path.Combine(tempDir, "AGENTS.md"), "# Repo rules\nAlways run the tests.\n");
                        Git(tempDir, "add", "AGENTS.md");
                        Git(tempDir, "-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "-q", "-m", "init");

                        Captain captain = new Captain("PiCaptain") { Runtime = AgentRuntimeEnum.Pi };
                        Mission mission = new Mission { Title = "Implement the pi feature", Description = "Do the work." };
                        await service.GenerateClaudeMdAsync(tempDir, mission, new Vessel("PiVessel", "https://github.com/test/repo"), captain);

                        string overridePath = Path.Combine(tempDir, "AGENTS.override.md");
                        AssertTrue(File.Exists(overridePath), "pi missions write AGENTS.override.md");
                        AssertFalse(File.Exists(Path.Combine(tempDir, "CLAUDE.md")), "pi missions do not write CLAUDE.md");
                        string content = File.ReadAllText(overridePath);
                        int missionAt = content.IndexOf("Implement the pi feature", StringComparison.Ordinal);
                        int repoAt = content.IndexOf("Always run the tests.", StringComparison.Ordinal);
                        AssertTrue(missionAt >= 0, "mission instructions present");
                        AssertTrue(repoAt > missionAt, "the repository AGENTS.md follows the mission instructions");
                        AssertEqual("# Repo rules\nAlways run the tests.\n", File.ReadAllText(Path.Combine(tempDir, "AGENTS.md")), "AGENTS.md is untouched");

                        string stubDirectory = Path.Combine(root, "stub");
                        string stub = StubAgentProcesses.WritePiStub(stubDirectory);
                        File.WriteAllText(Path.Combine(stubDirectory, "pi-replay.jsonl"), String.Join("\n", PiStreamFixtures.PlainReply) + "\n");
                        PiRuntime runtime = new PiRuntime(logging) { ExecutablePath = stub };
                        TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        runtime.OnProcessExited += (pid, code) => exited.TrySetResult(code);
                        await runtime.StartAsync(tempDir, "go", captain: captain);
                        AssertTrue(await Task.WhenAny(exited.Task, Task.Delay(30000)) == exited.Task, "stub pi exited");
                        AssertEqual(0, await exited.Task ?? -1);
                        AssertEqual(content, File.ReadAllText(Path.Combine(stubDirectory, "pi-agents-override.md")), "pi saw the generated file in the dock");

                        AssertEqual(String.Empty, Git(tempDir, "status", "--porcelain").Trim(), "git status is clean");
                    }
                    finally
                    {
                        try { Directory.Delete(root, true); } catch { }
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

            foreach (string persona in new[] { "Worker", "Judge", "Test Engineer" })
            foreach (string captainMode in new[] { "none", "empty", "whitespace" })
            {
                cases.Add(CaseAsync("default_contract_delivered_" + persona.Replace(" ", "_") + "_" + captainMode,
                    "Required " + persona + " contract reaches the mission file with " + captainMode + " captain instructions", TestTags.Positive, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                    IPromptTemplateService templates;
                    MissionService service = CreateMissionServiceWithTemplates(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService(), out templates);
                    await templates.SeedDefaultsAsync();
                    string tempDir = Path.Combine(Path.GetTempPath(), "armada_contract_delivery_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        Vessel vessel = new Vessel("contract-delivery", "https://github.com/test/repo");
                        Mission mission = new Mission("Verify instruction delivery", "Review the requested work.") { Persona = persona };
                        Captain? captain = captainMode == "none" ? null : new Captain("default-instructions")
                        {
                            Runtime = Armada.Core.Enums.AgentRuntimeEnum.ApiEndpoint,
                            SystemInstructions = captainMode == "whitespace" ? "   " : String.Empty
                        };
                        string contract = MissionPromptBuilder.BuildTemplateParams(mission, vessel, captain)["CaptainInstructions"];
                        AssertTrue(!String.IsNullOrWhiteSpace(contract), "the role has a required contract independently of custom instructions");
                        await service.GenerateClaudeMdAsync(tempDir, mission, vessel, captain);
                        string instructions = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                        AssertContains(contract, instructions, "the generated artifact must deliver the complete role contract even without custom captain instructions");
                    }
                    finally { try { Directory.Delete(tempDir, true); } catch { } }
                }));
            }

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
                    AssertContains("respond only with real mission definitions", prompt);
                    AssertContains("armada-plan", prompt);
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

            cases.Add(CaseAsync("generate_claude_md_async_never_dirties_git_in_linked_worktree", "GenerateClaudeMdAsync leaves git status clean in a linked worktree, whether CLAUDE.md is tracked or not", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    MissionService service = CreateMissionService(logging, testDb.Driver, settings, new StubGitService());

                    foreach (bool trackClaudeMd in new bool[] { true, false })
                    {
                        string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                        if (trackClaudeMd)
                        {
                            await File.WriteAllTextAsync(Path.Combine(repo, "CLAUDE.md"), "# Project Instructions\n\nUse explicit types.\n");
                            VesselHealthGitHelper.RunGit(repo, null, "add", "CLAUDE.md");
                            VesselHealthGitHelper.RunGit(repo, null, "commit", "-q", "-m", "Add CLAUDE.md");
                        }

                        string dock = Path.Combine(Path.GetDirectoryName(repo)!, Path.GetFileName(repo) + "_dock");
                        VesselHealthGitHelper.RunGit(repo, null, "worktree", "add", "-q", "-b", "armada/test/msn_prompt", dock);

                        Vessel vessel = new Vessel("PromptVessel", "https://github.com/test/repo");
                        Mission mission = new Mission();
                        mission.Title = "Leak check";
                        mission.Description = "Generated instructions must never be committable.";

                        await service.GenerateClaudeMdAsync(dock, mission, vessel);

                        string content = await File.ReadAllTextAsync(Path.Combine(dock, "CLAUDE.md"));
                        AssertContains("# Mission Instructions", content);
                        if (trackClaudeMd) AssertContains("Use explicit types.", content);

                        string status = VesselHealthGitHelper.RunGit(dock, null, "status", "--porcelain").Trim();
                        AssertEqual("", status, "git status in the dock (tracked CLAUDE.md: " + trackClaudeMd + ")");

                        VesselHealthGitHelper.RunGit(dock, null, "add", "-A");
                        string staged = VesselHealthGitHelper.RunGit(dock, null, "diff", "--cached", "--name-only").Trim();
                        AssertEqual("", staged, "files staged by git add -A (tracked CLAUDE.md: " + trackClaudeMd + ")");
                    }
                }
            }));

            cases.Add(CaseAsync("pipeline_placeholders_filled_from_prior_stage", "Judge, test engineer and linter prompts get {Diff} and {PreviousStageOutput} from the prior stage", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, CreateSettings(), new StubGitService(), out IPromptTemplateService templates);

                    Mission prior = new Mission("Implement feature", "Do the work");
                    prior.Persona = "Worker";
                    prior.DiffSnapshot = "diff --git a/src/a.cs b/src/a.cs\n+PRIOR_DIFF_MARKER";
                    prior.AgentOutput = "PRIOR_OUTPUT_MARKER\n[ARMADA:RESULT] COMPLETE";
                    prior = await testDb.Driver.Missions.CreateAsync(prior);

                    foreach (string persona in new[] { "Judge", "TestEngineer", "Linter" })
                    {
                        string tempDir = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDir);
                        try
                        {
                            Vessel vessel = new Vessel("PromptVessel", "https://github.com/test/repo");
                            Mission mission = new Mission("Review feature", "Review the work");
                            mission.Persona = persona;
                            mission.DependsOnMissionId = prior.Id;

                            await service.GenerateClaudeMdAsync(tempDir, mission, vessel);

                            string content = await File.ReadAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"));
                            AssertContains("PRIOR_DIFF_MARKER", content, persona + " diff");
                            AssertContains("PRIOR_OUTPUT_MARKER", content, persona + " output");
                            AssertFalse(content.Contains("{Diff}", StringComparison.Ordinal), persona + ": {Diff} left unfilled");
                            AssertFalse(content.Contains("{PreviousStageOutput}", StringComparison.Ordinal), persona + ": {PreviousStageOutput} left unfilled");
                        }
                        finally
                        {
                            try { Directory.Delete(tempDir, true); } catch { }
                        }
                    }

                    Dictionary<string, string> standalone = MissionPromptBuilder.BuildTemplateParams(new Mission("Solo", "Solo"), new Vessel("V", "https://github.com/test/repo"), null, null, null);
                    AssertEqual(MissionPromptBuilder.NoPreviousStageDiffText, standalone["Diff"]);
                    AssertEqual(MissionPromptBuilder.NoPreviousStageOutputText, standalone["PreviousStageOutput"]);
                }
            }));

            cases.Add(CaseAsync("builtin_template_placeholders_are_supplied", "Every placeholder in every built-in template is one the mission prompt builder supplies", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, CreateLogging());
                    HashSet<string> supplied = new HashSet<string>(MissionPromptBuilder.TemplateParameterNames, StringComparer.Ordinal);

                    Dictionary<string, string> built = MissionPromptBuilder.BuildTemplateParams(new Mission("T", "D"), new Vessel("V", "https://github.com/test/repo"), new Captain("c"), null, null);
                    HashSet<string> builtKeys = new HashSet<string>(built.Keys, StringComparer.Ordinal) { "PersonaPrompt", "ExistingClaudeMd" };
                    AssertTrue(builtKeys.SetEquals(supplied), "TemplateParameterNames must match what the builder fills: builder=" + String.Join(",", builtKeys) + " list=" + String.Join(",", supplied));

                    foreach (string name in templates.GetEmbeddedDefaultNames())
                    {
                        string content = templates.GetEmbeddedDefault(name) ?? String.Empty;
                        foreach (Match match in Regex.Matches(content, "\\{([A-Z][A-Za-z0-9]*)\\}"))
                        {
                            AssertTrue(supplied.Contains(match.Groups[1].Value), "template " + name + " uses {" + match.Groups[1].Value + "}, which nothing fills");
                        }
                    }
                }
            }));

            cases.Add(CaseAsync("placeholder_panels_match_builder", "The dashboard and TUI placeholder panels list exactly the placeholders the builder supplies", TestTags.Negative, () =>
            {
                HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in MissionPromptBuilder.TemplateParameterNames) expected.Add("{" + name + "}");

                // The panel's groups are defined in lib/configuration.ts (shared with the mobile app) and rendered by
                // pages/PromptTemplateDetail.tsx.
                string ts = File.ReadAllText(Path.Combine(DashboardSource.Src(), "lib", "configuration.ts"));
                int start = ts.IndexOf("export const PROMPT_TEMPLATE_PARAMETER_GROUPS", StringComparison.Ordinal);
                int end = start >= 0 ? ts.IndexOf("\n];", start, StringComparison.Ordinal) : -1;
                AssertTrue(start >= 0 && end > start, "PROMPT_TEMPLATE_PARAMETER_GROUPS block not found in lib/configuration.ts");
                string page = File.ReadAllText(Path.Combine(DashboardSource.Src(), "pages", "PromptTemplateDetail.tsx"));
                AssertTrue(page.Contains("PROMPT_TEMPLATE_PARAMETER_GROUPS", StringComparison.Ordinal), "PromptTemplateDetail.tsx no longer renders PROMPT_TEMPLATE_PARAMETER_GROUPS");
                HashSet<string> dashboard = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in Regex.Matches(ts.Substring(start, end - start), "name:\\s*'(\\{[A-Za-z0-9]+\\})'"))
                {
                    dashboard.Add(match.Groups[1].Value);
                }

                AssertTrue(dashboard.SetEquals(expected), "dashboard panel=" + String.Join(",", dashboard) + " builder=" + String.Join(",", expected));

                HashSet<string> tui = new HashSet<string>(StringComparer.Ordinal);
                foreach (Armada.Tui.Screens.Configuration.PromptParameter parameter in Armada.Tui.Screens.Configuration.PromptParameter.All) tui.Add(parameter.Name);
                AssertTrue(tui.SetEquals(expected), "TUI panel=" + String.Join(",", tui) + " builder=" + String.Join(",", expected));
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("persona_prompt_template_name_is_applied", "A mission uses its persona's PromptTemplateName, falling back to the default template when it does not exist", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    MissionService service = CreateMissionServiceWithTemplates(logging, testDb.Driver, CreateSettings(), new StubGitService(), out IPromptTemplateService templates);

                    await testDb.Driver.PromptTemplates.CreateAsync(new PromptTemplate("custom.security_review", "CUSTOM_PERSONA_TEMPLATE_MARKER for {MissionTitle}") { Category = "persona" });
                    await testDb.Driver.Personas.CreateAsync(new Persona("SecurityAuditor", "custom.security_review"));
                    await testDb.Driver.Personas.CreateAsync(new Persona("Judge", "missing.template_name"));

                    Vessel vessel = new Vessel("PromptVessel", "https://github.com/test/repo");

                    string dirA = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    string dirB = Path.Combine(Path.GetTempPath(), "armada_prompt_test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dirA);
                    Directory.CreateDirectory(dirB);
                    try
                    {
                        Mission custom = new Mission("Audit auth", "Audit the auth code");
                        custom.Persona = "SecurityAuditor";
                        await service.GenerateClaudeMdAsync(dirA, custom, vessel);
                        string customContent = await File.ReadAllTextAsync(Path.Combine(dirA, "CLAUDE.md"));
                        AssertContains("CUSTOM_PERSONA_TEMPLATE_MARKER for Audit auth", customContent);

                        Mission judge = new Mission("Review", "Review the work");
                        judge.Persona = "Judge";
                        await service.GenerateClaudeMdAsync(dirB, judge, vessel);
                        string judgeContent = await File.ReadAllTextAsync(Path.Combine(dirB, "CLAUDE.md"));
                        AssertContains("You are an Armada judge agent", judgeContent, "missing persona template falls back to persona.judge");
                    }
                    finally
                    {
                        try { Directory.Delete(dirA, true); } catch { }
                        try { Directory.Delete(dirB, true); } catch { }
                    }
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

        private static async Task<string> ReadGeneratedInstructionsAsync(MissionService service, string directoryLabel, Mission mission, Vessel vessel)
        {
            string directory = TestTemp.NewDirectory(directoryLabel);
            await service.GenerateClaudeMdAsync(directory, mission, vessel).ConfigureAwait(false);
            return await File.ReadAllTextAsync(Path.Combine(directory, "CLAUDE.md")).ConfigureAwait(false);
        }

        private static string Git(string workingDirectory, params string[] args)
        {
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            AssertEqual(0, process.ExitCode, "git " + String.Join(" ", args) + ": " + error);
            return output;
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
