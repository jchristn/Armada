namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ArmadaSettings"/>: default values, validation guards, directory
    /// initialization, GitHub token resolution, and save/load round-trips. Positive cases assert
    /// correct defaults and persistence fidelity; negative cases assert rejection of invalid ports,
    /// null/empty paths, and negative timeouts.
    /// </summary>
    public sealed class SettingsSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Settings suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("default_values_are_correct", "ArmadaSettings DefaultValues AreCorrect", TestTags.Positive, () =>
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
                AssertEqual(Constants.DefaultPlanningSessionInactivityTimeoutMinutes, settings.PlanningSessionInactivityTimeoutMinutes);
                AssertEqual(Constants.DefaultPlanningSessionAbandonmentTimeoutMinutes, settings.PlanningSessionAbandonmentTimeoutMinutes);
                AssertEqual(0, settings.PlanningSessionRetentionDays);
                AssertEqual(Armada.Core.Enums.LandingModeEnum.MergeAndPush, settings.LandingMode, "default landing mode");
                AssertNull(settings.ApiKey);
            }));

            cases.Add(Case("default_agents_contains_expected_runtimes", "ArmadaSettings DefaultAgents ContainsExpectedRuntimes", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertTrue(settings.Agents.Count >= 2, "Should have at least 2 default agents");
                AssertEqual(Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode, settings.Agents[0].Runtime);
                AssertEqual(Armada.Core.Enums.AgentRuntimeEnum.Codex, settings.Agents[1].Runtime);
            }));

            cases.Add(Case("set_port_invalid_range_throws", "ArmadaSettings SetPort InvalidRange Throws", TestTags.Negative, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentOutOfRangeException>(() => settings.AdmiralPort = 0);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.AdmiralPort = 70000);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.McpPort = -1);
            }));

            cases.Add(Case("set_data_directory_null_throws", "ArmadaSettings SetDataDirectory Null Throws", TestTags.Negative, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentNullException>(() => settings.DataDirectory = null!);
            }));

            cases.Add(Case("set_database_path_empty_throws", "ArmadaSettings SetDatabasePath Empty Throws", TestTags.Negative, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentNullException>(() => settings.DatabasePath = "");
            }));

            cases.Add(CaseAsync("save_and_load_round_trip", "ArmadaSettings SaveAndLoad RoundTrip", TestTags.Positive, async () =>
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
                    original.GitHubToken = "ghp-settings-test";

                    await original.SaveAsync(tempFile);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertEqual(9000, loaded.AdmiralPort);
                    AssertEqual(9001, loaded.McpPort);
                    AssertEqual(60, loaded.HeartbeatIntervalSeconds);
                    AssertEqual(90, loaded.DataRetentionDays);
                    AssertEqual("test-key-123", loaded.ApiKey);
                    AssertEqual("ghp-settings-test", loaded.GitHubToken);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            }));

            cases.Add(CaseAsync("load_non_existent_file_returns_defaults", "ArmadaSettings LoadAsync NonExistentFile ReturnsDefaults", TestTags.Positive, async () =>
            {
                ArmadaSettings settings = await ArmadaSettings.LoadAsync("/nonexistent/path/settings.json");
                AssertEqual(Constants.DefaultAdmiralPort, settings.AdmiralPort);
            }));

            cases.Add(Case("initialize_directories_normalizes_relative_sqlite_filename", "ArmadaSettings InitializeDirectories NormalizesRelativeSqliteFilename", TestTags.Positive, () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "armada_settings_db_" + Guid.NewGuid().ToString("N"));

                try
                {
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.DataDirectory = tempDir;
                    settings.Database = new DatabaseSettings();
                    settings.Database.Filename = "armada.db";

                    settings.InitializeDirectories();

                    string expectedPath = Path.GetFullPath(Path.Combine(tempDir, "armada.db"));
                    AssertEqual(expectedPath, settings.Database.Filename, "Database.Filename");
                    AssertEqual(expectedPath, settings.DatabasePath, "DatabasePath");
                    AssertTrue(Directory.Exists(tempDir), "Data directory should exist");
                }
                finally
                {
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                }
            }));

            cases.Add(CaseAsync("load_non_existent_file_lands_with_merge_and_push", "ArmadaSettings LoadAsync without a file uses MergeAndPush", TestTags.Positive, async () =>
            {
                ArmadaSettings settings = await ArmadaSettings.LoadAsync("/nonexistent/path/settings.json");
                AssertEqual(Armada.Core.Enums.LandingModeEnum.MergeAndPush, settings.LandingMode);
            }));

            AddLandingModeLoadCase(cases, "load_landing_mode_unset_is_merge_and_push", "A settings file without landingMode or legacy flags lands with MergeAndPush", "", Armada.Core.Enums.LandingModeEnum.MergeAndPush);
            AddLandingModeLoadCase(cases, "load_landing_mode_null_is_merge_and_push", "A settings file with landingMode null lands with MergeAndPush", "\"landingMode\":null,", Armada.Core.Enums.LandingModeEnum.MergeAndPush);
            AddLandingModeLoadCase(cases, "load_legacy_auto_create_pull_requests_is_pull_request", "A pre-1.0.1 file with autoCreatePullRequests true and no landingMode keeps opening pull requests", "\"autoCreatePullRequests\":true,", Armada.Core.Enums.LandingModeEnum.PullRequest);
            AddLandingModeLoadCase(cases, "load_legacy_auto_push_false_is_local_merge", "A pre-1.0.1 file with autoPush false and no landingMode keeps merging without pushing", "\"autoPush\":false,", Armada.Core.Enums.LandingModeEnum.LocalMerge);
            AddLandingModeLoadCase(cases, "load_legacy_auto_push_true_is_merge_and_push", "A pre-1.0.1 file with autoPush true and no landingMode keeps merging and pushing", "\"autoPush\":true,\"autoCreatePullRequests\":false,", Armada.Core.Enums.LandingModeEnum.MergeAndPush);
            AddLandingModeLoadCase(cases, "load_explicit_landing_mode_beats_legacy_flags", "An explicit landingMode wins over legacy flags in the same file", "\"landingMode\":\"LocalMerge\",\"autoCreatePullRequests\":true,", Armada.Core.Enums.LandingModeEnum.LocalMerge);
            AddLandingModeLoadCase(cases, "load_explicit_merge_and_push", "An explicit landingMode MergeAndPush loads as MergeAndPush", "\"landingMode\":\"MergeAndPush\",", Armada.Core.Enums.LandingModeEnum.MergeAndPush);

            cases.Add(CaseAsync("landing_mode_round_trip_save_load", "ArmadaSettings LandingMode survives save and load, and a saved file names it", TestTags.Positive, async () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "armada_settings_landing_" + Guid.NewGuid().ToString("N"));
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_landing_" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    foreach (Armada.Core.Enums.LandingModeEnum mode in Enum.GetValues(typeof(Armada.Core.Enums.LandingModeEnum)))
                    {
                        ArmadaSettings original = new ArmadaSettings();
                        original.DataDirectory = tempDir;
                        original.LandingMode = mode;
                        await original.SaveAsync(tempFile).ConfigureAwait(false);
                        AssertContains("\"landingMode\": \"" + mode + "\"", await File.ReadAllTextAsync(tempFile).ConfigureAwait(false), "saved file names the mode");
                        ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile).ConfigureAwait(false);
                        AssertEqual(mode, loaded.LandingMode, "round trip " + mode);
                    }
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                }
            }));

            cases.Add(CaseAsync("load_legacy_database_path_syncs_sqlite_filename", "ArmadaSettings LoadAsync LegacyDatabasePath SyncsSqliteFilename", TestTags.Positive, async () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "armada_settings_legacy_" + Guid.NewGuid().ToString("N"));
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_legacy_" + Guid.NewGuid().ToString("N") + ".json");
                string legacyDbPath = Path.Combine(tempDir, "legacy.db");

                try
                {
                    string json = "{" +
                        "\"dataDirectory\":\"" + tempDir.Replace("\\", "\\\\") + "\"," +
                        "\"databasePath\":\"" + legacyDbPath.Replace("\\", "\\\\") + "\"," +
                        "\"database\":{\"type\":\"Sqlite\"}" +
                        "}";
                    await File.WriteAllTextAsync(tempFile, json).ConfigureAwait(false);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertEqual(legacyDbPath, loaded.DatabasePath, "DatabasePath");
                    AssertEqual(legacyDbPath, loaded.Database.Filename, "Database.Filename");
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                }
            }));

            cases.Add(Case("new_settings_have_correct_defaults", "ArmadaSettings NewSettings HaveCorrectDefaults", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertNull(settings.DefaultRuntime);
                AssertTrue(settings.Notifications);
                AssertTrue(settings.TerminalBell);
                AssertEqual(Constants.DefaultIdleCaptainTimeoutSeconds, settings.IdleCaptainTimeoutSeconds);
                AssertEqual(Constants.DefaultPlanningSessionInactivityTimeoutMinutes, settings.PlanningSessionInactivityTimeoutMinutes);
                AssertEqual(Constants.DefaultPlanningSessionAbandonmentTimeoutMinutes, settings.PlanningSessionAbandonmentTimeoutMinutes);
                AssertEqual(0, settings.PlanningSessionRetentionDays);
                AssertNotNull(settings.RemoteControl);
                AssertFalse(settings.RemoteControl.Enabled);
                AssertEqual(Constants.DefaultRemoteConnectTimeoutSeconds, settings.RemoteControl.ConnectTimeoutSeconds);
                AssertEqual(Constants.DefaultRemoteHeartbeatIntervalSeconds, settings.RemoteControl.HeartbeatIntervalSeconds);
                AssertEqual(Constants.DefaultRemoteTunnelPassword, settings.RemoteControl.Password);
            }));

            cases.Add(Case("idle_captain_timeout_negative_throws", "ArmadaSettings IdleCaptainTimeoutSeconds NegativeThrows", TestTags.Negative, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertThrows<ArgumentOutOfRangeException>(() => settings.IdleCaptainTimeoutSeconds = -1);
            }));

            cases.Add(Case("idle_captain_timeout_zero_is_valid", "ArmadaSettings IdleCaptainTimeoutSeconds ZeroIsValid", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                settings.IdleCaptainTimeoutSeconds = 0;
                AssertEqual(0, settings.IdleCaptainTimeoutSeconds);
            }));

            cases.Add(Case("idle_captain_timeout_positive_is_valid", "ArmadaSettings IdleCaptainTimeoutSeconds PositiveIsValid", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                settings.IdleCaptainTimeoutSeconds = 300;
                AssertEqual(300, settings.IdleCaptainTimeoutSeconds);
            }));

            cases.Add(Case("message_templates_defaults_are_correct", "ArmadaSettings MessageTemplates DefaultsAreCorrect", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertNotNull(settings.MessageTemplates);
                AssertTrue(settings.MessageTemplates.EnableCommitMetadata);
                AssertTrue(settings.MessageTemplates.EnablePrMetadata);
                AssertContains("Armada-Mission-Id", settings.MessageTemplates.CommitMessageTemplate);
                AssertContains("Armada", settings.MessageTemplates.PrDescriptionTemplate);
                AssertContains("Merge armada mission", settings.MessageTemplates.MergeCommitTemplate);
            }));

            cases.Add(CaseAsync("new_settings_round_trip_save_load", "ArmadaSettings NewSettings RoundTripSaveLoad", TestTags.Positive, async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_new_" + Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    ArmadaSettings original = new ArmadaSettings();
                    original.DefaultRuntime = "ClaudeCode";
                    original.Notifications = false;
                    original.TerminalBell = false;
                    original.IdleCaptainTimeoutSeconds = 120;
                    original.PlanningSessionInactivityTimeoutMinutes = 45;
                    original.PlanningSessionAbandonmentTimeoutMinutes = 180;
                    original.PlanningSessionRetentionDays = 14;

                    await original.SaveAsync(tempFile);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertEqual("ClaudeCode", loaded.DefaultRuntime);
                    AssertFalse(loaded.Notifications);
                    AssertFalse(loaded.TerminalBell);
                    AssertEqual(120, loaded.IdleCaptainTimeoutSeconds);
                    AssertEqual(45, loaded.PlanningSessionInactivityTimeoutMinutes);
                    AssertEqual(180, loaded.PlanningSessionAbandonmentTimeoutMinutes);
                    AssertEqual(14, loaded.PlanningSessionRetentionDays);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            }));

            cases.Add(CaseAsync("message_templates_round_trip_save_load", "ArmadaSettings MessageTemplates RoundTripSaveLoad", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("remote_control_round_trip_save_load", "ArmadaSettings RemoteControl RoundTripSaveLoad", TestTags.Positive, async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_remote_" + Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    ArmadaSettings original = new ArmadaSettings();
                    original.RemoteControl.Enabled = true;
                    original.RemoteControl.TunnelUrl = "https://control.example.com/tunnel";
                    original.RemoteControl.InstanceId = "armada-test-instance";
                    original.RemoteControl.EnrollmentToken = "token-123";
                    original.RemoteControl.Password = "proxy-secret";
                    original.RemoteControl.ConnectTimeoutSeconds = 25;
                    original.RemoteControl.HeartbeatIntervalSeconds = 45;
                    original.RemoteControl.ReconnectBaseDelaySeconds = 8;
                    original.RemoteControl.ReconnectMaxDelaySeconds = 120;
                    original.RemoteControl.AllowInvalidCertificates = true;

                    await original.SaveAsync(tempFile);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertTrue(loaded.RemoteControl.Enabled);
                    AssertEqual("https://control.example.com/tunnel", loaded.RemoteControl.TunnelUrl);
                    AssertEqual("armada-test-instance", loaded.RemoteControl.InstanceId);
                    AssertEqual("token-123", loaded.RemoteControl.EnrollmentToken);
                    AssertEqual("proxy-secret", loaded.RemoteControl.Password);
                    AssertEqual(25, loaded.RemoteControl.ConnectTimeoutSeconds);
                    AssertEqual(45, loaded.RemoteControl.HeartbeatIntervalSeconds);
                    AssertEqual(8, loaded.RemoteControl.ReconnectBaseDelaySeconds);
                    AssertEqual(120, loaded.RemoteControl.ReconnectMaxDelaySeconds);
                    AssertTrue(loaded.RemoteControl.AllowInvalidCertificates);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            }));

            cases.Add(Case("resolve_github_token_prefers_vessel_override", "ArmadaSettings ResolveGitHubToken PrefersVesselOverride", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                settings.GitHubToken = "ghp_global";
                Vessel vessel = new Vessel("Resolve GitHub", "https://github.com/test/repo");
                vessel.GitHubTokenOverride = "  ghp_vessel  ";

                string? resolved = settings.ResolveGitHubToken(vessel);
                AssertEqual("ghp_vessel", resolved);
            }));

            cases.Add(Case("resolve_github_token_falls_back_to_global", "ArmadaSettings ResolveGitHubToken FallsBackToGlobal", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                settings.GitHubToken = "  ghp_global  ";
                Vessel vessel = new Vessel("Resolve GitHub", "https://github.com/test/repo");

                string? resolved = settings.ResolveGitHubToken(vessel);
                AssertEqual("ghp_global", resolved);
            }));

            cases.Add(Case("telemetry_defaults_are_correct", "ArmadaSettings Telemetry DefaultsAreCorrect", TestTags.Positive, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertNotNull(settings.Telemetry);
                AssertFalse(settings.Telemetry.Enabled, "Telemetry should be disabled by default");
                AssertEqual("armada", settings.Telemetry.ServiceName);
                AssertNull(settings.Telemetry.OtlpEndpoint);
                AssertTrue(settings.Telemetry.PrometheusEnabled);
                AssertEqual(9464, settings.Telemetry.PrometheusPort);
                AssertNull(settings.Telemetry.LokiEndpoint);
            }));

            cases.Add(Case("telemetry_service_name_blank_falls_back", "TelemetrySettings ServiceName Blank FallsBack", TestTags.Negative, () =>
            {
                TelemetrySettings telemetry = new TelemetrySettings();
                telemetry.ServiceName = "   ";
                AssertEqual("armada", telemetry.ServiceName);
                telemetry.ServiceName = null!;
                AssertEqual("armada", telemetry.ServiceName);
            }));

            cases.Add(Case("telemetry_prometheus_port_clamps_out_of_range", "TelemetrySettings PrometheusPort ClampsOutOfRange", TestTags.Negative, () =>
            {
                TelemetrySettings telemetry = new TelemetrySettings();
                telemetry.PrometheusPort = -5;
                AssertEqual(1, telemetry.PrometheusPort);
                telemetry.PrometheusPort = 999999;
                AssertEqual(65535, telemetry.PrometheusPort);
                telemetry.PrometheusPort = 9500;
                AssertEqual(9500, telemetry.PrometheusPort);
            }));

            cases.Add(CaseAsync("telemetry_round_trip_save_load", "ArmadaSettings Telemetry RoundTripSaveLoad", TestTags.Positive, async () =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_telemetry_" + Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    ArmadaSettings original = new ArmadaSettings();
                    original.Telemetry.Enabled = true;
                    original.Telemetry.ServiceName = "armada-prod";
                    original.Telemetry.OtlpEndpoint = "http://collector:4317";
                    original.Telemetry.PrometheusEnabled = false;
                    original.Telemetry.PrometheusPort = 9500;
                    original.Telemetry.LokiEndpoint = "http://loki:3100";

                    await original.SaveAsync(tempFile);

                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile);
                    AssertTrue(loaded.Telemetry.Enabled);
                    AssertEqual("armada-prod", loaded.Telemetry.ServiceName);
                    AssertEqual("http://collector:4317", loaded.Telemetry.OtlpEndpoint);
                    AssertFalse(loaded.Telemetry.PrometheusEnabled);
                    AssertEqual(9500, loaded.Telemetry.PrometheusPort);
                    AssertEqual("http://loki:3100", loaded.Telemetry.LokiEndpoint);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            }));

            cases.Add(Case("metrics_meter_name_and_instruments_present", "ArmadaMetrics MeterName AndInstruments Present", TestTags.Positive, () =>
            {
                AssertEqual("Armada", ArmadaMetrics.MeterName);
                AssertNotNull(ArmadaMetrics.CaptainStalls);
                AssertNotNull(ArmadaMetrics.CaptainRecoveries);
                AssertNotNull(ArmadaMetrics.HandoffsRedriven);
                AssertNotNull(ArmadaMetrics.ReviewsOverdue);
                AssertNotNull(ArmadaMetrics.DocksProvisioned);
                AssertNotNull(ArmadaMetrics.DocksReclaimed);
                AssertNotNull(ArmadaMetrics.MergeEntriesProcessed);
                AssertNotNull(ArmadaMetrics.MissionsFailed);
                AssertNotNull(ArmadaMetrics.MissionRuntimeExceeded);
                // Emitting must be safe with no subscriber attached.
                ArmadaMetrics.CaptainStalls.Add(1);
                ArmadaMetrics.HandoffsRedriven.Add(2);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.Settings",
                displayName: "Settings",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void AddLandingModeLoadCase(List<TestCaseDescriptor> cases, string caseId, string displayName, string landingJson, Armada.Core.Enums.LandingModeEnum expected)
        {
            cases.Add(CaseAsync(caseId, displayName, TestTags.Positive, async () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "armada_settings_landing_" + Guid.NewGuid().ToString("N"));
                string tempFile = Path.Combine(Path.GetTempPath(), "armada_test_settings_landing_" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    string json = "{" + landingJson + "\"dataDirectory\":\"" + tempDir.Replace("\\", "\\\\") + "\"}";
                    await File.WriteAllTextAsync(tempFile, json).ConfigureAwait(false);
                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(tempFile).ConfigureAwait(false);
                    AssertEqual(expected, loaded.LandingMode, "landing mode from " + json);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                }
            }));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.Settings",
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
                suiteId: "Services.Settings",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
