namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MessageTemplateService"/>: placeholder substitution,
    /// context building from domain objects, and rendering of commit/PR/merge metadata.
    /// Positive cases assert correct rendering; negative cases assert null/empty/unknown-input
    /// handling, disabled-setting short-circuits, and null-argument rejection (audit additions
    /// for the constructor and Render* settings guards).
    /// </summary>
    public sealed class MessageTemplateServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the MessageTemplateService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // RenderTemplate

            cases.Add(Case("render_template_replaces_all_placeholders", "RenderTemplate ReplacesAllPlaceholders", TestTags.Positive, () =>
            {
                MessageTemplateService service = CreateService();
                Dictionary<string, string> parameters = new Dictionary<string, string>
                {
                    ["MissionId"] = "msn_abc123",
                    ["CaptainId"] = "cpt_def456"
                };

                string result = service.RenderTemplate("Mission: {MissionId}, Captain: {CaptainId}", parameters);
                AssertEqual("Mission: msn_abc123, Captain: cpt_def456", result);
            }));

            cases.Add(Case("render_template_empty_template_returns_empty", "RenderTemplate EmptyTemplate ReturnsEmpty", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                Dictionary<string, string> parameters = new Dictionary<string, string> { ["Key"] = "value" };
                string result = service.RenderTemplate("", parameters);
                AssertEqual("", result);
            }));

            cases.Add(Case("render_template_null_template_returns_empty", "RenderTemplate NullTemplate ReturnsEmpty", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                string result = service.RenderTemplate(null!, new Dictionary<string, string>());
                AssertEqual("", result);
            }));

            cases.Add(Case("render_template_unknown_placeholder_leaves_as_is", "RenderTemplate UnknownPlaceholder LeavesAsIs", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                Dictionary<string, string> parameters = new Dictionary<string, string> { ["Known"] = "value" };
                string result = service.RenderTemplate("{Known} and {Unknown}", parameters);
                AssertEqual("value and {Unknown}", result);
            }));

            cases.Add(Case("render_template_null_parameter_value_replaces_with_empty", "RenderTemplate NullParameterValue ReplacesWithEmpty", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                Dictionary<string, string> parameters = new Dictionary<string, string> { ["Key"] = null! };
                string result = service.RenderTemplate("Value: {Key}", parameters);
                AssertEqual("Value: ", result);
            }));

            cases.Add(Case("render_template_empty_parameters_returns_template_unchanged", "RenderTemplate EmptyParameters ReturnsTemplateUnchanged", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                string result = service.RenderTemplate("Hello {World}", new Dictionary<string, string>());
                AssertEqual("Hello {World}", result);
            }));

            // BuildContext

            cases.Add(Case("build_context_populates_all_fields", "BuildContext PopulatesAllFields", TestTags.Positive, () =>
            {
                MessageTemplateService service = CreateService();
                Mission mission = new Mission("Test Mission", "Test description");
                Captain captain = new Captain("Captain-1");
                Vessel vessel = new Vessel("My Repo", "https://github.com/test/repo");
                vessel.FleetId = "flt_abc123";
                Voyage voyage = new Voyage("Test Voyage");
                Dock dock = new Dock(vessel.Id);
                dock.BranchName = "armada/test-branch";

                Dictionary<string, string> context = service.BuildContext(mission, captain, vessel, voyage, dock);

                AssertEqual(mission.Id, context["MissionId"]);
                AssertEqual("Test Mission", context["MissionTitle"]);
                AssertEqual(captain.Id, context["CaptainId"]);
                AssertEqual("Captain-1", context["CaptainName"]);
                AssertEqual(vessel.Id, context["VesselId"]);
                AssertEqual("My Repo", context["VesselName"]);
                AssertEqual("flt_abc123", context["FleetId"]);
                AssertEqual(voyage.Id, context["VoyageId"]);
                AssertEqual("Test Voyage", context["VoyageTitle"]);
                AssertEqual(dock.Id, context["DockId"]);
                AssertEqual("armada/test-branch", context["BranchName"]);
                AssertFalse(String.IsNullOrEmpty(context["Timestamp"]));
            }));

            cases.Add(Case("build_context_null_optional_objects_handles_gracefully", "BuildContext NullOptionalObjects HandlesGracefully", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                Mission mission = new Mission("Solo Mission");

                Dictionary<string, string> context = service.BuildContext(mission);

                AssertEqual(mission.Id, context["MissionId"]);
                AssertEqual("Solo Mission", context["MissionTitle"]);
                AssertEqual("", context["CaptainName"]);
                AssertEqual("", context["VesselName"]);
                AssertEqual("", context["FleetId"]);
                AssertEqual("", context["VoyageTitle"]);
                AssertEqual("", context["DockId"]);
            }));

            cases.Add(Case("build_context_null_mission_throws", "BuildContext NullMission Throws", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                AssertThrows<ArgumentNullException>(() => service.BuildContext(null!));
            }));

            // RenderCommitInstructions

            cases.Add(CaseAsync("render_commit_instructions_enabled_setting_returns_instructions", "RenderCommitInstructions EnabledSetting ReturnsGuidanceAndTrailers", TestTags.Positive, async () =>
            {
                MessageTemplateService service = CreateService();
                MessageTemplateSettings settings = new MessageTemplateSettings();
                Dictionary<string, string> context = new Dictionary<string, string>
                {
                    ["MissionId"] = "msn_abc",
                    ["VoyageId"] = "vyg_def",
                    ["CaptainId"] = "cpt_ghi",
                    ["VesselId"] = "vsl_jkl"
                };

                string result = await service.RenderCommitInstructionsAsync(settings, context).ConfigureAwait(false);

                AssertContains("msn_abc", result);
                AssertContains("vyg_def", result);
                AssertContains("cpt_ghi", result);
                AssertContains("vsl_jkl", result);
                AssertContains("IMPORTANT", result);
                AssertContains("list every file added, modified, or deleted", result);
                AssertContains("append the following trailers", result);
            }));

            cases.Add(CaseAsync("render_commit_instructions_disabled_metadata_keeps_guidance", "RenderCommitInstructions MetadataDisabled KeepsDescriptiveGuidanceWithoutTrailers", TestTags.Positive, async () =>
            {
                MessageTemplateService service = CreateService();
                MessageTemplateSettings settings = new MessageTemplateSettings();
                settings.EnableCommitMetadata = false;

                string result = await service.RenderCommitInstructionsAsync(settings, new Dictionary<string, string> { ["MissionId"] = "msn_abc" }).ConfigureAwait(false);
                AssertContains("list every file added, modified, or deleted", result, "descriptive guidance still applies");
                AssertFalse(result.Contains("trailers"), "no trailer instruction when metadata is off");
                AssertFalse(result.Contains("msn_abc"), "no trailers rendered");
            }));

            cases.Add(CaseAsync("render_commit_instructions_honors_template_overrides", "RenderCommitInstructions Honors Edited Templates", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    PromptTemplate? preamble = await testDb.Driver.PromptTemplates.ReadByNameAsync("commit.instructions_preamble").ConfigureAwait(false);
                    AssertNotNull(preamble, "seeded");
                    preamble!.Content = "Write commit messages as a bulleted changelog of every file.";
                    await testDb.Driver.PromptTemplates.UpdateAsync(preamble).ConfigureAwait(false);

                    MessageTemplateService service = new MessageTemplateService(logging, templates);
                    string result = await service.RenderCommitInstructionsAsync(new MessageTemplateSettings(), new Dictionary<string, string> { ["MissionId"] = "msn_abc" }).ConfigureAwait(false);
                    AssertContains("bulleted changelog of every file", result, "operator edit is used");
                    AssertFalse(result.Contains("IMPORTANT: Every git commit"), "built-in text is not used once edited");
                    AssertContains("msn_abc", result, "trailers still appended");
                }
            }));

            cases.Add(CaseAsync("commit_preamble_legacy_builtin_is_upgraded_but_edits_are_kept", "Seeding upgrades the legacy built-in commit preamble and keeps operator edits", TestTags.Positive, async () =>
            {
                string legacy = "IMPORTANT: Every git commit you create MUST have a clear, descriptive commit message. The message MUST include: (1) a concise summary line stating what the commit does, and (2) a full manifest and description of what was changed -- list every file added, modified, or deleted and, for each, explain what changed and why. After that description, append the following trailers at the end of the commit message (after a blank line):";
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    PromptTemplate? row = await testDb.Driver.PromptTemplates.ReadByNameAsync("commit.instructions_preamble").ConfigureAwait(false);
                    row!.Content = legacy;
                    await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);

                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    row = await testDb.Driver.PromptTemplates.ReadByNameAsync("commit.instructions_preamble").ConfigureAwait(false);
                    AssertEqual(PromptTemplateService.CommitInstructionsPreambleDefault, row!.Content, "legacy built-in upgraded");
                    AssertNotNull(await testDb.Driver.PromptTemplates.ReadByNameAsync("commit.trailers_preamble").ConfigureAwait(false), "trailers template seeded");

                    row.Content = "My own commit rules.";
                    await testDb.Driver.PromptTemplates.UpdateAsync(row).ConfigureAwait(false);
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    row = await testDb.Driver.PromptTemplates.ReadByNameAsync("commit.instructions_preamble").ConfigureAwait(false);
                    AssertEqual("My own commit rules.", row!.Content, "operator edit kept");
                }
            }));

            // RenderPrDescription

            cases.Add(Case("render_pr_description_appends_to_base_body", "RenderPrDescription AppendsToBaseBody", TestTags.Positive, () =>
            {
                MessageTemplateService service = CreateService();
                MessageTemplateSettings settings = new MessageTemplateSettings();
                Dictionary<string, string> context = new Dictionary<string, string>
                {
                    ["MissionId"] = "msn_abc",
                    ["VoyageId"] = "vyg_def",
                    ["CaptainId"] = "cpt_ghi",
                    ["VesselId"] = "vsl_jkl"
                };

                string result = service.RenderPrDescription(settings, "## My PR Body", context);

                AssertStartsWith("## My PR Body", result);
                AssertContains("msn_abc", result);
                AssertContains("Armada", result);
            }));

            cases.Add(Case("render_pr_description_disabled_setting_returns_base_body", "RenderPrDescription DisabledSetting ReturnsBaseBody", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                MessageTemplateSettings settings = new MessageTemplateSettings();
                settings.EnablePrMetadata = false;

                string result = service.RenderPrDescription(settings, "Base body", new Dictionary<string, string>());
                AssertEqual("Base body", result);
            }));

            // RenderMergeCommitMessage

            cases.Add(Case("render_merge_commit_message_enabled_setting_returns_message", "RenderMergeCommitMessage EnabledSetting ReturnsMessage", TestTags.Positive, () =>
            {
                MessageTemplateService service = CreateService();
                MessageTemplateSettings settings = new MessageTemplateSettings();
                Dictionary<string, string> context = new Dictionary<string, string>
                {
                    ["BranchName"] = "armada/test-branch",
                    ["MissionId"] = "msn_abc",
                    ["VoyageId"] = "vyg_def"
                };

                string? result = service.RenderMergeCommitMessage(settings, context);

                AssertNotNull(result);
                AssertContains("armada/test-branch", result!);
                AssertContains("msn_abc", result);
            }));

            cases.Add(Case("render_merge_commit_message_disabled_setting_returns_null", "RenderMergeCommitMessage DisabledSetting ReturnsNull", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                MessageTemplateSettings settings = new MessageTemplateSettings();
                settings.EnableCommitMetadata = false;

                string? result = service.RenderMergeCommitMessage(settings, new Dictionary<string, string>());
                AssertNull(result);
            }));

            // MessageTemplateSettings

            cases.Add(Case("message_template_settings_default_values_are_correct", "MessageTemplateSettings DefaultValues AreCorrect", TestTags.Positive, () =>
            {
                MessageTemplateSettings settings = new MessageTemplateSettings();
                AssertTrue(settings.EnableCommitMetadata);
                AssertTrue(settings.EnablePrMetadata);
                AssertContains("Armada-Mission-Id", settings.CommitMessageTemplate);
                AssertContains("Armada", settings.PrDescriptionTemplate);
                AssertContains("Merge armada mission", settings.MergeCommitTemplate);
            }));

            cases.Add(Case("message_template_settings_set_null_defaults_to_empty", "MessageTemplateSettings SetNull DefaultsToEmpty", TestTags.Negative, () =>
            {
                MessageTemplateSettings settings = new MessageTemplateSettings();
                settings.CommitMessageTemplate = null!;
                settings.PrDescriptionTemplate = null!;
                settings.MergeCommitTemplate = null!;
                AssertEqual("", settings.CommitMessageTemplate);
                AssertEqual("", settings.PrDescriptionTemplate);
                AssertEqual("", settings.MergeCommitTemplate);
            }));

            cases.Add(Case("message_template_settings_custom_template_is_preserved", "MessageTemplateSettings CustomTemplate IsPreserved", TestTags.Positive, () =>
            {
                MessageTemplateSettings settings = new MessageTemplateSettings();
                settings.CommitMessageTemplate = "Custom: {MissionId}";
                AssertEqual("Custom: {MissionId}", settings.CommitMessageTemplate);
            }));

            // Audit additions: null-argument rejection paths (confirmed against source guards)

            cases.Add(Case("constructor_null_logging_throws", "Constructor NullLogging Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => new MessageTemplateService(null!));
            }));

            cases.Add(CaseAsync("render_commit_instructions_null_settings_throws", "RenderCommitInstructions NullSettings Throws", TestTags.Negative, async () =>
            {
                MessageTemplateService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.RenderCommitInstructionsAsync(null!, new Dictionary<string, string>())).ConfigureAwait(false);
            }));

            cases.Add(Case("render_pr_description_null_settings_throws", "RenderPrDescription NullSettings Throws", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                AssertThrows<ArgumentNullException>(() => service.RenderPrDescription(null!, "Base body", new Dictionary<string, string>()));
            }));

            cases.Add(Case("render_merge_commit_message_null_settings_throws", "RenderMergeCommitMessage NullSettings Throws", TestTags.Negative, () =>
            {
                MessageTemplateService service = CreateService();
                AssertThrows<ArgumentNullException>(() => service.RenderMergeCommitMessage(null!, new Dictionary<string, string>()));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MessageTemplateService",
                displayName: "Message Template Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MessageTemplateService CreateService()
        {
            LoggingModule logging = new LoggingModule();
            return new MessageTemplateService(logging);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MessageTemplateService",
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
                suiteId: "Services.MessageTemplateService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
