namespace Armada.Test.Unit.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Tests for the PromptTemplateService: seeding, resolving, rendering, resetting, and listing templates.
    /// </summary>
    public class PromptTemplateServiceTests : TestSuite
    {
        /// <summary>
        /// Suite name.
        /// </summary>
        public override string Name => "Prompt Template Service";

        /// <summary>
        /// Run all tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Seed defaults creates all built-in templates", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    List<PromptTemplate> templates = await service.ListAsync().ConfigureAwait(false);
                    AssertTrue(templates.Count >= 11, "Expected at least 11 built-in templates, got " + templates.Count);

                    // Verify some known template names exist
                    List<string> names = templates.Select(t => t.Name).ToList();
                    AssertTrue(names.Contains("mission.rules"), "Should contain mission.rules");
                    AssertTrue(names.Contains("agent.launch_prompt"), "Should contain agent.launch_prompt");
                    AssertTrue(names.Contains("persona.worker"), "Should contain persona.worker");
                    AssertTrue(names.Contains("persona.architect"), "Should contain persona.architect");
                    AssertTrue(names.Contains("persona.judge"), "Should contain persona.judge");
                }
            });

            await RunTest("Resolve returns database template when exists", async () =>
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
            });

            await RunTest("Resolve falls back to embedded default", async () =>
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
            });

            await RunTest("Render substitutes placeholders", async () =>
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
            });

            await RunTest("Reset to default restores original content", async () =>
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
            });

            await RunTest("List returns all templates", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    PromptTemplateService service = new PromptTemplateService(testDb.Driver, logging);
                    await service.SeedDefaultsAsync().ConfigureAwait(false);

                    List<PromptTemplate> templates = await service.ListAsync().ConfigureAwait(false);
                    AssertTrue(templates.Count >= 11, "Expected at least 11 templates, got " + templates.Count);
                }
            });

            await RunTest("List by category filters correctly", async () =>
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
            });
        }
    }
}
