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
                }
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

        #endregion
    }
}
