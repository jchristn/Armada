namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core;
    using Armada.Core.Settings;
    using Armada.Test.Common;

    public class SettingsTests : TestSuite
    {
        public override string Name => "Settings";

        protected override async Task RunTestsAsync()
        {
            await RunTest("ArmadaSettings DefaultValues AreCorrect", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertEqual(Constants.DefaultAdmiralPort, settings.AdmiralPort);
                AssertEqual(Constants.DefaultMcpPort, settings.McpPort);
                AssertEqual(Constants.DefaultHeartbeatIntervalSeconds, settings.HeartbeatIntervalSeconds);
                AssertEqual(Constants.DefaultStallThresholdMinutes, settings.StallThresholdMinutes);
                AssertEqual(Constants.DefaultMaxRecoveryAttempts, settings.MaxRecoveryAttempts);
                AssertEqual(Constants.DefaultMaxLogFileSizeBytes, settings.MaxLogFileSizeBytes);
                AssertEqual(Constants.DefaultMaxLogFileCount, settings.MaxLogFileCount);
                AssertEqual(Constants.DefaultDataRetentionDays, settings.DataRetentionDays);
                AssertFalse(settings.AutoCreatePullRequests);
                AssertNull(settings.ApiKey);
            });

            await RunTest("ArmadaSettings DefaultAgents ContainsExpectedRuntimes", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertTrue(settings.Agents.Count >= 2, "Should have at least 2 default agents");
                AssertEqual(Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode, settings.Agents[0].Runtime);
                AssertEqual(Armada.Core.Enums.AgentRuntimeEnum.Codex, settings.Agents[1].Runtime);
            });

            await RunTest("ArmadaSettings SetPort InvalidRange Throws", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentOutOfRangeException>(() => settings.AdmiralPort = 0);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.AdmiralPort = 70000);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.McpPort = -1);
            });

            await RunTest("ArmadaSettings SetDataDirectory Null Throws", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentNullException>(() => settings.DataDirectory = null!);
            });

            await RunTest("ArmadaSettings SetDatabasePath Empty Throws", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentNullException>(() => settings.DatabasePath = "");
            });

            await RunTest("ArmadaSettings SaveAndLoad RoundTrip", async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_" + Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    ArmadaSettings original = new ArmadaSettings();
                    original.AdmiralPort = 9000;
                    original.McpPort = 9001;
                    original.HeartbeatIntervalSeconds = 60;
                    original.DataRetentionDays = 90;
                    original.ApiKey = "test-key-123";

                    await original.SaveAsync(tempFile);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertEqual(9000, loaded.AdmiralPort);
                    AssertEqual(9001, loaded.McpPort);
                    AssertEqual(60, loaded.HeartbeatIntervalSeconds);
                    AssertEqual(90, loaded.DataRetentionDays);
                    AssertEqual("test-key-123", loaded.ApiKey);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });

            await RunTest("ArmadaSettings LoadAsync NonExistentFile ReturnsDefaults", async () =>
            {
                ArmadaSettings settings = await ArmadaSettings.LoadAsync("/nonexistent/path/settings.json");
                AssertEqual(Constants.DefaultAdmiralPort, settings.AdmiralPort);
            });

            await RunTest("ArmadaSettings NewSettings HaveCorrectDefaults", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertNull(settings.DefaultRuntime);
                AssertTrue(settings.Notifications);
                AssertTrue(settings.TerminalBell);
                AssertEqual(Constants.DefaultIdleCaptainTimeoutSeconds, settings.IdleCaptainTimeoutSeconds);
            });

            await RunTest("ArmadaSettings IdleCaptainTimeoutSeconds NegativeThrows", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentOutOfRangeException>(() => settings.IdleCaptainTimeoutSeconds = -1);
            });

            await RunTest("ArmadaSettings IdleCaptainTimeoutSeconds ZeroIsValid", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                settings.IdleCaptainTimeoutSeconds = 0;
                AssertEqual(0, settings.IdleCaptainTimeoutSeconds);
            });

            await RunTest("ArmadaSettings IdleCaptainTimeoutSeconds PositiveIsValid", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                settings.IdleCaptainTimeoutSeconds = 300;
                AssertEqual(300, settings.IdleCaptainTimeoutSeconds);
            });

            await RunTest("ArmadaSettings MessageTemplates DefaultsAreCorrect", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertNotNull(settings.MessageTemplates);
                AssertTrue(settings.MessageTemplates.EnableCommitMetadata);
                AssertTrue(settings.MessageTemplates.EnablePrMetadata);
                AssertContains("Armada-Mission-Id", settings.MessageTemplates.CommitMessageTemplate);
                AssertContains("Armada", settings.MessageTemplates.PrDescriptionTemplate);
                AssertContains("Merge armada mission", settings.MessageTemplates.MergeCommitTemplate);
            });

            await RunTest("ArmadaSettings NewSettings RoundTripSaveLoad", async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_new_" + Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    ArmadaSettings original = new ArmadaSettings();
                    original.DefaultRuntime = "ClaudeCode";
                    original.Notifications = false;
                    original.TerminalBell = false;
                    original.IdleCaptainTimeoutSeconds = 120;

                    await original.SaveAsync(tempFile);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertEqual("ClaudeCode", loaded.DefaultRuntime);
                    AssertFalse(loaded.Notifications);
                    AssertFalse(loaded.TerminalBell);
                    AssertEqual(120, loaded.IdleCaptainTimeoutSeconds);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });

            await RunTest("ArmadaSettings MessageTemplates RoundTripSaveLoad", async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_templates_" + Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    ArmadaSettings original = new ArmadaSettings();
                    original.MessageTemplates.EnableCommitMetadata = false;
                    original.MessageTemplates.EnablePrMetadata = false;
                    original.MessageTemplates.CommitMessageTemplate = "Custom: {MissionId}";
                    original.MessageTemplates.PrDescriptionTemplate = "PR: {MissionId}";
                    original.MessageTemplates.MergeCommitTemplate = "Merge: {BranchName}";

                    await original.SaveAsync(tempFile);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertNotNull(loaded.MessageTemplates);
                    AssertFalse(loaded.MessageTemplates.EnableCommitMetadata);
                    AssertFalse(loaded.MessageTemplates.EnablePrMetadata);
                    AssertEqual("Custom: {MissionId}", loaded.MessageTemplates.CommitMessageTemplate);
                    AssertEqual("PR: {MissionId}", loaded.MessageTemplates.PrDescriptionTemplate);
                    AssertEqual("Merge: {BranchName}", loaded.MessageTemplates.MergeCommitTemplate);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }
    }
}
