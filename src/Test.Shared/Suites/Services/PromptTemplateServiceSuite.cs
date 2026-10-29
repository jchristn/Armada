namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the <see cref="PromptTemplateService"/>: seeding built-in defaults, resolving
    /// database and embedded-fallback templates, embedded content guarantees for mission rules and
    /// review personas, placeholder rendering, reset-to-default, and listing (all and by category).
    /// Each case builds a fresh SQLite store.
    /// </summary>
    public sealed class PromptTemplateServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PromptTemplateService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Prompt Template Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("seed_defaults_creates_all_built_in_templates", "Seed defaults creates all built-in templates", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    List<PromptTemplate> templates = await service.ListAsync().ConfigureAwait(false);
                    AssertTrue(templates.Count >= 13, "Expected at least 13 built-in templates, got " + templates.Count);

                    // Verify some known template names exist
                    List<string> names = templates.Select(t => t.Name).ToList();
                    AssertTrue(names.Contains("mission.rules"), "Should contain mission.rules");
                    AssertTrue(names.Contains("agent.launch_prompt"), "Should contain agent.launch_prompt");
                    AssertTrue(names.Contains("persona.worker"), "Should contain persona.worker");
                    AssertTrue(names.Contains("persona.architect"), "Should contain persona.architect");
                    AssertTrue(names.Contains("persona.product_manager"), "Should contain persona.product_manager");
                    AssertTrue(names.Contains("persona.usability_engineer"), "Should contain persona.usability_engineer");
                    AssertTrue(names.Contains("persona.judge"), "Should contain persona.judge");
                    PromptTemplate worker = templates.Find(t => t.Name == "persona.worker")!;
                    worker.Content = PersonaPromptImprovements.Briefs[worker.Name];
                    await testDb.Driver.PromptTemplates.UpdateAsync(worker).ConfigureAwait(false);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    worker = (await testDb.Driver.PromptTemplates.ReadByNameAsync(worker.Name).ConfigureAwait(false))!;
                    AssertContains("Print progress before long commands", worker.Content, "previous shipped defaults are upgraded");
                    worker.Content = "Operator custom guidance";
                    await testDb.Driver.PromptTemplates.UpdateAsync(worker).ConfigureAwait(false);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    AssertEqual(worker.Content, (await testDb.Driver.PromptTemplates.ReadByNameAsync(worker.Name).ConfigureAwait(false))!.Content, "custom persona overrides survive");
                }
            }));

            cases.Add(CaseAsync("upgrade_partial_success_guidance_preserves_custom_edits", "Only untouched context guidance is upgraded to require complete work", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                LoggingModule logging = new LoggingModule(); logging.Settings.EnableConsole = false;
                PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                await service.SeedDefaultsAsync().ConfigureAwait(false);
                PromptTemplate template = (await testDb.Driver.PromptTemplates.ReadByNameAsync("mission.context_conservation").ConfigureAwait(false))!;
                const string newRule = "5. **Complete the assigned scope.** Work incrementally and validate relevant changes. If a real blocker prevents completion, preserve your work and report the blocker explicitly. Never emit a success marker or claim completion for partial work.\n";
                const string oldRule = "5. **If the mission scope feels too large** (more than 8 files, or files with 500+ lines to read), commit what you have, report progress, and exit with code 0. Partial progress is better than crashing.\n";
                AssertContains(newRule, template.Content);
                template.Content = template.Content.Replace(newRule, oldRule);
                await testDb.Driver.PromptTemplates.UpdateAsync(template).ConfigureAwait(false);
                await service.SeedDefaultsAsync().ConfigureAwait(false);
                template = (await testDb.Driver.PromptTemplates.ReadByNameAsync(template.Name).ConfigureAwait(false))!;
                AssertContains(newRule, template.Content);
                template.Content = template.Content.Replace(newRule, oldRule) + "Operator customization";
                await testDb.Driver.PromptTemplates.UpdateAsync(template).ConfigureAwait(false);
                await service.SeedDefaultsAsync().ConfigureAwait(false);
                AssertContains("Operator customization", (await testDb.Driver.PromptTemplates.ReadByNameAsync(template.Name).ConfigureAwait(false))!.Content);
                AssertContains(oldRule, (await testDb.Driver.PromptTemplates.ReadByNameAsync(template.Name).ConfigureAwait(false))!.Content);
            }));

            cases.Add(CaseAsync("upgrade_progress_signals_adds_blocked_line_preserves_custom_edits", "An untouched built-in progress_signals template gains the blocked outcome line; an edited one is kept", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                LoggingModule logging = new LoggingModule(); logging.Settings.EnableConsole = false;
                PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                await service.SeedDefaultsAsync().ConfigureAwait(false);
                PromptTemplate template = (await testDb.Driver.PromptTemplates.ReadByNameAsync("mission.progress_signals").ConfigureAwait(false))!;
                AssertContains(CaptainBlockedContract.SignalLine, template.Content, "seeded with the blocked line");
                template.Content = template.Content.Replace(CaptainBlockedContract.SignalLine, String.Empty);
                await testDb.Driver.PromptTemplates.UpdateAsync(template).ConfigureAwait(false);
                await service.SeedDefaultsAsync().ConfigureAwait(false);
                template = (await testDb.Driver.PromptTemplates.ReadByNameAsync(template.Name).ConfigureAwait(false))!;
                AssertContains(CaptainBlockedContract.SignalLine, template.Content, "the previous shipped default is upgraded");
                template.Content = template.Content.Replace(CaptainBlockedContract.SignalLine, String.Empty) + "Operator customization";
                await testDb.Driver.PromptTemplates.UpdateAsync(template).ConfigureAwait(false);
                await service.SeedDefaultsAsync().ConfigureAwait(false);
                string kept = (await testDb.Driver.PromptTemplates.ReadByNameAsync(template.Name).ConfigureAwait(false))!.Content;
                AssertContains("Operator customization", kept);
                AssertFalse(kept.Contains(CaptainBlockedContract.SignalLine, StringComparison.Ordinal), "an edited template is not changed");
            }));

            cases.Add(CaseAsync("resolve_returns_database_template_when_exists", "Resolve returns database template when exists", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    PromptTemplate? resolved = await service.ResolveAsync("mission.rules").ConfigureAwait(false);
                    AssertNotNull(resolved, "Resolved template should not be null");
                    AssertEqual("mission.rules", resolved!.Name, "Template name");
                    AssertTrue(resolved.Content.Contains("## Rules"), "Content should contain '## Rules'");
                }
            }));

            cases.Add(CaseAsync("resolve_falls_back_to_embedded_default", "Resolve falls back to embedded default", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    // Do NOT seed -- the database is empty, so resolve should fall back to embedded defaults
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);

                    PromptTemplate? resolved = await service.ResolveAsync("mission.rules").ConfigureAwait(false);
                    AssertNotNull(resolved, "Resolved template should not be null even without seeding");
                    AssertEqual("mission.rules", resolved!.Name, "Template name");
                    AssertTrue(resolved.Content.Length > 0, "Content should not be empty");
                }
            }));

            cases.Add(CaseAsync("mission_rules_embedded_default_constrains_file_scope", "Mission rules embedded default constrains file scope", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);

                    PromptTemplate? resolved = await service.ResolveAsync("mission.rules").ConfigureAwait(false);
                    AssertNotNull(resolved, "Resolved template should not be null");
                    AssertContains("Stay strictly within the mission scope and listed files", resolved!.Content, "Mission rules should explicitly constrain scope to the assigned files");
                    AssertContains("report it in your result instead of expanding scope on your own", resolved.Content, "Mission rules should tell agents to report needed out-of-scope changes instead of freelancing");
                }
            }));

            cases.Add(CaseAsync("judge_and_test_engineer_embedded_defaults_require_structured_risk_aware_review", "Judge and test engineer embedded defaults require structured risk-aware review", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);

                    PromptTemplate? judge = await service.ResolveAsync("persona.judge").ConfigureAwait(false);
                    AssertNotNull(judge, "Judge template should resolve");
                    AssertContains("## Correctness", judge!.Content, "Judge template should require a Correctness lens section");
                    AssertContains("## Blast Radius", judge.Content, "Judge template should require a Blast Radius lens section");
                    AssertContains("## Source Fidelity", judge.Content, "Judge template should require a Source Fidelity lens section");
                    AssertContains("## Affected Case", judge.Content, "Judge template should require a concrete affected case to block");

                    PromptTemplate? testEngineer = await service.ResolveAsync("persona.test_engineer").ConfigureAwait(false);
                    AssertNotNull(testEngineer, "Test engineer template should resolve");
                    AssertContains("negative or edge-path test", testEngineer!.Content, "Test engineer template should require negative-path coverage");
                    AssertContains("## Coverage Added", testEngineer.Content, "Test engineer template should request a coverage summary section");
                    AssertContains("residual risk", testEngineer.Content, "Test engineer template should require residual risk notes");

                    PromptTemplate? productManager = await service.ResolveAsync("persona.product_manager").ConfigureAwait(false);
                    AssertNotNull(productManager, "Product manager template should resolve");
                    AssertContains("## Product Vision", productManager!.Content, "Product manager template should require a Product Vision section");
                    AssertContains("## Future Readiness", productManager.Content, "Product manager template should require a Future Readiness section");

                    PromptTemplate? usabilityEngineer = await service.ResolveAsync("persona.usability_engineer").ConfigureAwait(false);
                    AssertNotNull(usabilityEngineer, "Usability engineer template should resolve");
                    AssertContains("## Usability", usabilityEngineer!.Content, "Usability engineer template should require a Usability section");
                    AssertContains("## Consistency", usabilityEngineer.Content, "Usability engineer template should require a Consistency section");
                }
            }));

            cases.Add(CaseAsync("render_substitutes_placeholders", "Render substitutes placeholders", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    Dictionary<string, string> parameters = new Dictionary<string, string>
                    {
                        { "MissionTitle", "Test" },
                        { "MissionDescription", "A test mission description." }
                    };

                    string rendered = await service.RenderAsync("agent.launch_prompt", parameters).ConfigureAwait(false);
                    AssertContains("Test", rendered, "Rendered output should contain substituted MissionTitle");
                    AssertContains("A test mission description.", rendered, "Rendered output should contain substituted MissionDescription");
                }
            }));

            cases.Add(CaseAsync("reset_to_default_restores_original_content", "Reset to default restores original content", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    // Read the original content
                    PromptTemplate? original = await service.ResolveAsync("mission.rules").ConfigureAwait(false);
                    AssertNotNull(original, "Original template should not be null");
                    string originalContent = original!.Content;

                    // Modify the template content in the database
                    original.Content = "MODIFIED CONTENT";
                    await testDb.Driver.PromptTemplates.UpdateAsync(original).ConfigureAwait(false);

                    // Verify modification took effect
                    PromptTemplate? modified = await service.ResolveAsync("mission.rules").ConfigureAwait(false);
                    AssertEqual("MODIFIED CONTENT", modified!.Content, "Content should be modified");

                    // Reset to default
                    PromptTemplate? reset = await service.ResetToDefaultAsync("mission.rules").ConfigureAwait(false);
                    AssertNotNull(reset, "Reset template should not be null");
                    AssertEqual(originalContent, reset!.Content, "Content should be restored to original");
                }
            }));

            cases.Add(CaseAsync("list_returns_all_templates", "List returns all templates", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    List<PromptTemplate> templates = await service.ListAsync().ConfigureAwait(false);
                    AssertTrue(templates.Count >= 13, "Expected at least 13 templates, got " + templates.Count);
                }
            }));

            cases.Add(CaseAsync("list_by_category_filters_correctly", "List by category filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    List<PromptTemplate> personaTemplates = await service.ListAsync("persona").ConfigureAwait(false);
                    AssertTrue(personaTemplates.Count > 0, "Should have at least one persona template");

                    foreach (PromptTemplate template in personaTemplates)
                    {
                        AssertEqual("persona", template.Category, "Category for template " + template.Name);
                    }
                }
            }));

            cases.Add(CaseAsync("ask_system_scope_guidance_and_upgrade", "ask.system carries conversation-scope guidance and an untouched previous built-in is upgraded", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    PromptTemplate? row = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains("## Conversation scope", row!.Content, "new installs get the scope guidance");

                    string previous = (string)typeof(PromptTemplateService).GetField("_AskSystemPreviousDefault", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                    AssertFalse(previous.Contains("## Conversation scope"), "previous default lacks the guidance");
                    row.Content = previous;
                    await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    row = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains("## Conversation scope", row!.Content, "untouched previous built-in upgraded");

                    row.Content = "My own Ask prompt.";
                    await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    row = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertEqual("My own Ask prompt.", row!.Content, "operator edit kept");
                }
            }));

            cases.Add(CaseAsync("ask_system_blackbox_guidance_and_upgrade", "ask.system carries the black box section (plan 10.2); an untouched previous built-in is upgraded, and an operator edit is kept as written", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    PromptTemplate? row = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains(Armada.Core.Services.Ask.AskBlackBoxGuidance.Section, row!.Content, "new installs get the black box section");
                    foreach (string heading in Armada.Core.Services.Ask.AskBlackBoxGuidance.AnswerHeadings) AssertContains(heading, row.Content, "answer heading " + heading);
                    AssertContains("get_blackbox", row.Content);
                    AssertContains("read_blackbox_object", row.Content);
                    AssertContains("CaptureMode=Sweeper", row.Content);
                    AssertContains("FailureReason", row.Content);
                    AssertContains("annotate_blackbox", row.Content);
                    AssertFalse(row.Content.Contains('\u2014'), "no em-dashes in the prompt");

                    string recap = (string)typeof(PromptTemplateService).GetField("_AskSystemRecapDefault", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                    AssertFalse(recap.Contains(Armada.Core.Services.Ask.AskBlackBoxGuidance.Heading), "the previous default lacks the section");
                    row.Content = recap;
                    await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    row = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains(Armada.Core.Services.Ask.AskBlackBoxGuidance.Heading, row!.Content, "untouched previous built-in upgraded");
                    AssertEqual(1, CountOf(row.Content, Armada.Core.Services.Ask.AskBlackBoxGuidance.Heading), "the section appears once");

                    row.Content = "My own Ask prompt.";
                    await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    row = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertEqual("My own Ask prompt.", row!.Content, "operator edit kept (the turn prompt appends the section instead)");
                }
            }));

            cases.Add(CaseAsync("decision_guidance_and_upgrade", "ask.system and mission.rules tell captains to choose sensible defaults instead of asking; untouched earlier built-ins are upgraded and operator edits kept", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);

                    PromptTemplate? ask = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    PromptTemplate? rules = await testDb.Driver.PromptTemplates.ReadByNameAsync("mission.rules").ConfigureAwait(false);
                    AssertContains("## Making decisions", ask!.Content, "new installs: Ask decides low-stakes choices");
                    AssertContains("dotnet --list-sdks", ask.Content, "the multi-target example");
                    AssertContains("You cannot ask the operator questions during a mission", rules!.Content, "new installs: missions decide");

                    System.Reflection.BindingFlags privateStatic = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                    string askScope = (string)typeof(PromptTemplateService).GetField("_AskSystemScopeDefault", privateStatic)!.GetValue(null)!;
                    string rulesLegacy = (string)typeof(PromptTemplateService).GetField("_MissionRulesLegacyDefault", privateStatic)!.GetValue(null)!;
                    AssertFalse(askScope.Contains("## Making decisions"), "the earlier Ask default lacks the guidance");
                    AssertFalse(rulesLegacy.Contains("You cannot ask the operator"), "the earlier rules lack the guidance");

                    ask.Content = askScope;
                    rules.Content = rulesLegacy;
                    await testDb.Driver.PromptTemplates.UpdateAsync(ask).ConfigureAwait(false);
                    await testDb.Driver.PromptTemplates.UpdateAsync(rules).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    ask = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    rules = await testDb.Driver.PromptTemplates.ReadByNameAsync("mission.rules").ConfigureAwait(false);
                    AssertContains("## Making decisions", ask!.Content, "untouched earlier Ask built-in upgraded");
                    AssertEqual(PromptTemplateService.MissionRulesDefault, rules!.Content, "untouched earlier rules built-in upgraded");

                    rules.Content = rulesLegacy + "- Always ask before choosing a framework\n";
                    await testDb.Driver.PromptTemplates.UpdateAsync(rules).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    rules = await testDb.Driver.PromptTemplates.ReadByNameAsync("mission.rules").ConfigureAwait(false);
                    AssertEqual(rulesLegacy + "- Always ask before choosing a framework\n", rules!.Content, "operator edit kept");
                    PromptTemplate? resolved = await templates.ResolveAsync("mission.rules").ConfigureAwait(false);
                    AssertEqual(rules.Content, resolved!.Content, "the stored template is what prompts use");
                }
            }));

            cases.Add(CaseAsync("ask_system_dispatch_guidance_and_upgrade", "ask.system tells the captain to favor dispatching missions over doing work directly; an untouched earlier built-in is upgraded and operator edits kept", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);

                    PromptTemplate? ask = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains("## Prefer dispatching work", ask!.Content, "new installs favor dispatch");
                    AssertFalse(ask.Content.Contains("or dispatch work unless the operator explicitly asks"), "the old dispatch restriction is gone");

                    string decisions = (string)typeof(PromptTemplateService).GetField("_AskSystemDecisionsDefault", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                    AssertFalse(decisions.Contains("## Prefer dispatching work"), "the earlier default lacks the guidance");

                    ask.Content = decisions;
                    await testDb.Driver.PromptTemplates.UpdateAsync(ask).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    ask = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains("## Prefer dispatching work", ask!.Content, "untouched earlier built-in upgraded");

                    ask.Content = decisions + "- Operator note\n";
                    await testDb.Driver.PromptTemplates.UpdateAsync(ask).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    ask = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertEqual(decisions + "- Operator note\n", ask!.Content, "operator edit kept");
                }
            }));

            cases.Add(CaseAsync("ask_system_completion_recap_and_upgrade", "ask.system tells the captain to recap what finished and what remains when work completes; an untouched dispatch-era built-in is upgraded", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);

                    PromptTemplate? ask = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains("- When work completes, reiterate what was done", ask!.Content, "new installs recap completed work");

                    string dispatch = (string)typeof(PromptTemplateService).GetField("_AskSystemDispatchDefault", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                    AssertFalse(dispatch.Contains("When work completes"), "the earlier default lacks the guidance");

                    ask.Content = dispatch;
                    await testDb.Driver.PromptTemplates.UpdateAsync(ask).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    ask = await testDb.Driver.PromptTemplates.ReadByNameAsync("ask.system").ConfigureAwait(false);
                    AssertContains("- When work completes, reiterate what was done", ask!.Content, "untouched earlier built-in upgraded");
                }
            }));

            cases.Add(CaseAsync("persona_execution_upgrade_preserves_customization", "Untouched persona defaults upgrade once while operator edits and non-built-in templates remain unchanged", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    Dictionary<string, string> previous = (Dictionary<string, string>)typeof(PromptTemplateService).GetField("_PreviousPersonaContents", flags)!.GetValue(service)!;
                    foreach (KeyValuePair<string, string> entry in previous)
                    {
                        PromptTemplate row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(entry.Key).ConfigureAwait(false))!;
                        row.Content = entry.Value;
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                    }
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (KeyValuePair<string, string> entry in previous)
                    {
                        PromptTemplate legacy = (await testDb.Driver.PromptTemplates.ReadByNameAsync(entry.Key).ConfigureAwait(false))!;
                        string recall = entry.Key == "persona.recorder" ? String.Empty
                            : (string)typeof(PromptTemplateService).GetField("_MemoryRecallGuidance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;
                        legacy.Content = PersonaPromptImprovements.Apply(entry.Key, entry.Value + recall);
                        await testDb.Driver.PromptTemplates.UpdateAsync(legacy).ConfigureAwait(false);
                    }
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (string name in previous.Keys)
                    {
                        PromptTemplate row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertContains(PersonaPromptImprovements.Marker, row.Content, name + " upgraded");
                        string upgraded = row.Content;
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertEqual(upgraded, row.Content, name + " upgrade is idempotent");
                        row.Content = upgraded + "\nOperator-specific instructions.";
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertEqual(upgraded + "\nOperator-specific instructions.", row.Content, name + " operator customization preserved");
                        PromptTemplate reset = (await service.ResetToDefaultAsync(name).ConfigureAwait(false))!;
                        AssertContains(PersonaPromptImprovements.Marker, reset.Content, name + " reset uses improved default");
                    }
                    PromptTemplate worker = (await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.worker").ConfigureAwait(false))!;
                    worker.Content = previous["persona.worker"];
                    worker.IsBuiltIn = false;
                    await testDb.Driver.PromptTemplates.UpdateAsync(worker).ConfigureAwait(false);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    worker = (await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.worker").ConfigureAwait(false))!;
                    AssertEqual(previous["persona.worker"], worker.Content, "non-built-in content not upgraded");
                }
            }));

            cases.Add(CaseAsync("bounded_slice_upgrade_preserves_operator_edits", "Prior brief defaults upgrade while custom briefs survive reseeding", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (string name in new[] { "persona.architect", "persona.worker", "persona.judge" })
                    {
                        PromptTemplate row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        row.Content = PersonaPromptImprovements.PreviousBriefs[name];
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertEqual(service.GetEmbeddedDefault(name), row.Content, name + " previous brief upgraded");
                        row.Content = PersonaPromptImprovements.PreviousBriefs[name] + "Operator-specific instruction";
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertTrue(row.Content.EndsWith("Operator-specific instruction"), name + " custom brief preserved");
                    }
                }
            }));

            cases.Add(CaseAsync("evidence_contract_upgrade_preserves_operator_edits", "Prior brief defaults upgrade while custom briefs survive reseeding", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (string name in new[] { "persona.architect", "persona.worker", "persona.judge" })
                    {
                        PromptTemplate row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        row.Content = PersonaPromptImprovements.RecentBriefs[name];
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertEqual(service.GetEmbeddedDefault(name), row.Content, name + " previous brief upgraded");
                        row.Content = PersonaPromptImprovements.RecentBriefs[name] + "Operator-specific instruction";
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertTrue(row.Content.EndsWith("Operator-specific instruction"), name + " custom brief preserved");
                    }
                }
            }));

            cases.Add(CaseAsync("scoped_decision_upgrade_preserves_operator_edits", "Prior brief defaults upgrade while custom briefs survive reseeding", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (string name in new[] { "persona.architect", "persona.worker", "persona.judge" })
                    {
                        PromptTemplate row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        row.Content = PersonaPromptImprovements.EvidenceBriefs[name];
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertEqual(service.GetEmbeddedDefault(name), row.Content, name + " previous brief upgraded");
                        row.Content = PersonaPromptImprovements.EvidenceBriefs[name] + "Operator-specific instruction";
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertTrue(row.Content.EndsWith("Operator-specific instruction"), name + " custom brief preserved");
                    }
                }
            }));

            cases.Add(CaseAsync("finding_reconciliation_upgrade_preserves_operator_edits", "Prior brief defaults upgrade while custom briefs survive reseeding", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (string name in new[] { "persona.architect", "persona.worker", "persona.judge" })
                    {
                        PromptTemplate row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        row.Content = PersonaPromptImprovements.DecisionBriefs[name];
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertEqual(service.GetEmbeddedDefault(name), row.Content, name + " previous brief upgraded");
                        row.Content = PersonaPromptImprovements.DecisionBriefs[name] + "Operator-specific instruction";
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertTrue(row.Content.EndsWith("Operator-specific instruction"), name + " custom brief preserved");
                    }
                }
            }));

            cases.Add(CaseAsync("decision_check_upgrade_preserves_operator_edits", "Prior brief defaults upgrade while custom briefs survive reseeding", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (string name in new[] { "persona.architect", "persona.worker", "persona.judge", "persona.test_engineer" })
                    {
                        PromptTemplate row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        row.Content = PersonaPromptImprovements.ScopedBriefs[name];
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertEqual(service.GetEmbeddedDefault(name), row.Content, name + " previous brief upgraded");
                        row.Content = PersonaPromptImprovements.ScopedBriefs[name] + "Operator-specific instruction";
                        await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                        await service.SeedDefaultsAsync().ConfigureAwait(false);
                        row = (await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false))!;
                        AssertTrue(row.Content.EndsWith("Operator-specific instruction"), name + " custom brief preserved");
                    }
                }
            }));

            cases.Add(CaseAsync("persona_guidance_rendered_for_limited_tools", "All persona briefs carry tool honesty; role-specific evidence and implementation contracts render without changing output protocols", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, new LoggingModule());
                    await service.SeedDefaultsAsync().ConfigureAwait(false);
                    foreach (string name in PersonaPromptImprovements.Additions.Keys)
                    {
                        string rendered = await service.RenderAsync(name, new Dictionary<string, string> { ["MissionDescription"] = "A bounded offline task", ["Diff"] = "a verified diff", ["PreviousStageOutput"] = "supplied evidence" }).ConfigureAwait(false);
                        AssertTrue(rendered.Length < 2400, name + " stays succinct");
                        AssertContains("Print progress before long commands; skip the full test suite when Armada runs post-stage checks.", rendered);
                        AssertContains("Report unavailable tools", rendered, name + " unavailable checks are explicit");
                        AssertContains("Never claim an inspection", rendered, name + " evidence honesty");
                        AssertFalse(rendered.Contains("{MissionDescription}"), name + " objective rendered");
                    }
                    string architect = service.GetEmbeddedDefault("persona.architect")!;
                    AssertContains("Each description must stand alone", architect, "self-contained fan-out description");
                    AssertContains("validationCommands", architect, "executable checks are part of the typed plan");
                    AssertContains("exact commands", architect, "executable checks retain concrete commands");
                    AssertContains("armada-plan", architect, "plan protocol preserved");
                    AssertContains("Scope exclusions", architect, "bounded architect scope");
                    AssertContains("Invariants", architect, "shared invariants");
                    AssertContains("final integration slice", architect, "end-to-end acceptance");
                    string worker = service.GetEmbeddedDefault("persona.worker")!;
                    AssertContains("substantial redesign", worker, "worker returns design blockers");
                    AssertContains("each acceptance ID", worker, "criterion evidence handoff");
                    AssertContains("changed files", worker, "worker handoff");
                    AssertContains("precise blocker", worker, "bounded escalation");
                    string product = service.GetEmbeddedDefault("persona.product_manager")!;
                    AssertContains("publication/access date", product, "dated research evidence");
                    AssertContains("stable identifiers", product, "measurable acceptance criteria");
                    string testEngineer = service.GetEmbeddedDefault("persona.test_engineer")!;
                    AssertContains("weaken/delete assertions", testEngineer, "production defect honesty");
                    AssertContains("named tests", testEngineer, "requirement mapping");
                    AssertContains("smallest corrective action", service.GetEmbeddedDefault("persona.judge")!, "actionable judge repair");
                    AssertContains("file/line references", service.GetEmbeddedDefault("persona.linter")!, "linter locations");
                    AssertContains("[ARMADA:RESULT] COMPLETE", worker, "worker completion protocol preserved");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Prompt Template Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static int CountOf(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
            return count;
        }

        #endregion
    }
}
