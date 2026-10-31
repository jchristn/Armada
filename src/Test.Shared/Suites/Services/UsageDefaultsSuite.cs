namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for usage capture defaults (AC1 Services.UsageDefaults): the fixed cross-runtime captain
    /// contract and the server settings behind it. Minimally deserialized captains ({}) and captains with
    /// explicit nulls stay disabled with the default 300-second interval, Auto mode and no Custom command;
    /// interval bounds clamp consistently in the model setters; Disabled mode always wins; unknown enum
    /// strings are rejected; the UsageCaptureSettings defaults (disabled, 60-second minimum interval,
    /// opt-in PTY, 20-second probe timeout, concurrency and excerpt caps, 7/90-day retention) and their
    /// clamps are stable through ArmadaSettings; and the windows JSON codec tolerates empty, null and
    /// malformed input without crashing.
    /// </summary>
    public sealed class UsageDefaultsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UsageDefaults";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the usage defaults suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("captain_minimal_json_keeps_disabled_defaults", "A minimally deserialized captain stays disabled with 300/Auto", TestTags.Positive, () =>
            {
                Captain empty = JsonSerializer.Deserialize<Captain>("{}")!;
                AssertNotNull(empty, "captain deserializes");
                AssertFalse(empty.UsageCaptureEnabled, "{} keeps usage capture disabled");
                AssertEqual(300, empty.UsageCaptureIntervalSeconds, "{} keeps the default interval");
                AssertEqual(UsageCaptureModeEnum.Auto, empty.UsageCaptureMode, "{} keeps the default Auto mode");
                AssertNull(empty.UsageCaptureCommand, "{} keeps no command");
                AssertFalse(empty.UsageCaptureActive, "{} captain is not effectively active");

                Captain explicitFalse = JsonSerializer.Deserialize<Captain>("{\"UsageCaptureEnabled\":false}")!;
                AssertFalse(explicitFalse.UsageCaptureEnabled, "explicit false stays disabled");
                AssertFalse(explicitFalse.UsageCaptureActive, "explicit false is not active");

                Captain nullCommand = JsonSerializer.Deserialize<Captain>("{\"UsageCaptureCommand\":null}")!;
                AssertNull(nullCommand.UsageCaptureCommand, "explicit null command normalizes to null");
                AssertFalse(nullCommand.UsageCaptureActive, "explicit null command does not enable capture");
            }));

            cases.Add(Case("usage_global_json_omitted_null_and_empty_keep_defaults", "Omitted, null and empty global blocks keep capture disabled", TestTags.Positive, () =>
            {
                foreach (string json in new string[] { "{}", "{\"UsageCapture\":null}", "{\"UsageCapture\":{}}" })
                {
                    ArmadaSettings settings = JsonSerializer.Deserialize<ArmadaSettings>(json)!;
                    AssertNotNull(settings.UsageCapture, "global block is restored");
                    AssertFalse(settings.UsageCapture.Enabled, "capture stays disabled");
                    AssertFalse(settings.UsageCapture.AllowPtyFallback, "PTY stays opt-in");
                    AssertEqual(60, settings.UsageCapture.MinimumIntervalSeconds, "default minimum");
                    AssertEqual(20, settings.UsageCapture.ProbeTimeoutSeconds, "default timeout");
                    AssertEqual(4, settings.UsageCapture.MaximumConcurrentProbes, "default concurrency");
                    AssertEqual(1, settings.UsageCapture.MaximumConcurrentProbesPerHost, "default host concurrency");
                    AssertEqual(7, settings.UsageCapture.RawSnapshotRetentionDays, "default raw retention");
                    AssertEqual(90, settings.UsageCapture.RollupRetentionDays, "default rollup retention");
                }
            }));

            cases.Add(Case("usage_global_json_overrides_clamp_and_round_trip", "Explicit global settings clamp through deserialization and persist", TestTags.Positive, () =>
            {
                string json = "{\"UsageCapture\":{\"Enabled\":true,\"AllowPtyFallback\":true,"
                    + "\"MinimumIntervalSeconds\":1,\"ProbeTimeoutSeconds\":999,"
                    + "\"MaximumConcurrentProbes\":0,\"MaximumConcurrentProbesPerHost\":999,"
                    + "\"MaximumBackoffSeconds\":0,\"MaximumExcerptBytes\":999999,"
                    + "\"RawSnapshotRetentionDays\":0,\"RollupRetentionDays\":999999}}";
                ArmadaSettings settings = JsonSerializer.Deserialize<ArmadaSettings>(json)!;
                ArmadaSettings restored = JsonSerializer.Deserialize<ArmadaSettings>(JsonSerializer.Serialize(settings))!;
                foreach (UsageCaptureSettings usage in new UsageCaptureSettings[] { settings.UsageCapture, restored.UsageCapture })
                {
                    AssertTrue(usage.Enabled, "explicit enable survives");
                    AssertTrue(usage.AllowPtyFallback, "explicit PTY override survives");
                    AssertEqual(60, usage.MinimumIntervalSeconds, "minimum clamps");
                    AssertEqual(300, usage.ProbeTimeoutSeconds, "timeout clamps");
                    AssertEqual(1, usage.MaximumConcurrentProbes, "concurrency clamps");
                    AssertEqual(64, usage.MaximumConcurrentProbesPerHost, "host concurrency clamps");
                    AssertEqual(60, usage.MaximumBackoffSeconds, "backoff clamps");
                    AssertEqual(65536, usage.MaximumExcerptBytes, "excerpt clamps");
                    AssertEqual(1, usage.RawSnapshotRetentionDays, "raw retention clamps");
                    AssertEqual(1825, usage.RollupRetentionDays, "rollup retention clamps");
                }
            }));

            cases.Add(Case("captain_usage_json_rejects_malformed_types", "Malformed captain settings cannot silently enable capture", TestTags.Negative, () =>
            {
                foreach (string json in new string[]
                {
                    "{\"UsageCaptureEnabled\":\"true\"}",
                    "{\"UsageCaptureIntervalSeconds\":\"300\"}",
                    "{\"UsageCaptureIntervalSeconds\":1.5}",
                    "{\"UsageCaptureMode\":{}}",
                    "{\"UsageCaptureCommand\":[]}"
                })
                {
                    AssertThrows<JsonException>(() => JsonSerializer.Deserialize<Captain>(json));
                }

                foreach (string command in new string[] { "", " ", "\t\r\n" })
                {
                    string json = "{\"UsageCaptureCommand\":" + JsonSerializer.Serialize(command) + "}";
                    Captain captain = JsonSerializer.Deserialize<Captain>(json)!;
                    AssertNull(captain.UsageCaptureCommand, "blank command normalizes through JSON");
                    AssertFalse(captain.UsageCaptureActive, "blank command cannot enable default captain");
                }
            }));

            cases.Add(Case("captain_full_settings_round_trip", "Fully populated usage settings survive serialization", TestTags.Positive, () =>
            {
                string json = "{\"UsageCaptureEnabled\":true,\"UsageCaptureIntervalSeconds\":900,"
                    + "\"UsageCaptureMode\":\"Custom\",\"UsageCaptureCommand\":\"{\\\"executable\\\":\\\"/opt/usage/read-quota\\\",\\\"arguments\\\":[\\\"--json\\\"]}\"}";
                Captain captain = JsonSerializer.Deserialize<Captain>(json)!;
                AssertTrue(captain.UsageCaptureEnabled, "enabled flag deserializes");
                AssertEqual(900, captain.UsageCaptureIntervalSeconds, "interval deserializes");
                AssertEqual(UsageCaptureModeEnum.Custom, captain.UsageCaptureMode, "Custom mode deserializes");
                AssertNotNull(captain.UsageCaptureCommand, "command deserializes");
                AssertTrue(captain.UsageCaptureActive, "enabled Custom captain is active");

                string serialized = JsonSerializer.Serialize(captain);
                Captain roundTripped = JsonSerializer.Deserialize<Captain>(serialized)!;
                AssertTrue(roundTripped.UsageCaptureEnabled, "enabled flag round-trips");
                AssertEqual(900, roundTripped.UsageCaptureIntervalSeconds, "interval round-trips");
                AssertEqual(UsageCaptureModeEnum.Custom, roundTripped.UsageCaptureMode, "mode round-trips");
                AssertEqual(captain.UsageCaptureCommand, roundTripped.UsageCaptureCommand, "command round-trips");
            }));

            cases.Add(Case("captain_interval_clamps_in_model_setter", "The interval clamps to 60..86400 in the setter and through JSON", TestTags.Positive, () =>
            {
                Captain captain = new Captain("clamp-captain");
                AssertEqual(300, captain.UsageCaptureIntervalSeconds, "default interval is 300");

                captain.UsageCaptureIntervalSeconds = 0;
                AssertEqual(60, captain.UsageCaptureIntervalSeconds, "zero clamps to the minimum");
                captain.UsageCaptureIntervalSeconds = -30;
                AssertEqual(60, captain.UsageCaptureIntervalSeconds, "negative clamps to the minimum");
                captain.UsageCaptureIntervalSeconds = 59;
                AssertEqual(60, captain.UsageCaptureIntervalSeconds, "below-range clamps to the minimum");
                captain.UsageCaptureIntervalSeconds = 60;
                AssertEqual(60, captain.UsageCaptureIntervalSeconds, "minimum is accepted as-is");
                captain.UsageCaptureIntervalSeconds = 86400;
                AssertEqual(86400, captain.UsageCaptureIntervalSeconds, "maximum is accepted as-is");
                captain.UsageCaptureIntervalSeconds = 86401;
                AssertEqual(86400, captain.UsageCaptureIntervalSeconds, "above-range clamps to the maximum");
                captain.UsageCaptureIntervalSeconds = 100000;
                AssertEqual(86400, captain.UsageCaptureIntervalSeconds, "far above-range clamps to the maximum");
                captain.UsageCaptureIntervalSeconds = 7200;
                AssertEqual(7200, captain.UsageCaptureIntervalSeconds, "in-range value is preserved");

                Captain clampedLow = JsonSerializer.Deserialize<Captain>("{\"UsageCaptureIntervalSeconds\":5}")!;
                AssertEqual(60, clampedLow.UsageCaptureIntervalSeconds, "JSON below-range clamps to the minimum");
                Captain clampedHigh = JsonSerializer.Deserialize<Captain>("{\"UsageCaptureIntervalSeconds\":999999}")!;
                AssertEqual(86400, clampedHigh.UsageCaptureIntervalSeconds, "JSON above-range clamps to the maximum");
            }));

            cases.Add(Case("captain_leaving_custom_mode_clears_command", "Leaving Custom mode clears the stored command", TestTags.Positive, () =>
            {
                Captain captain = new Captain("mode-clear-captain");
                AssertNull(captain.UsageCaptureCommand, "no command by default");

                captain.UsageCaptureMode = UsageCaptureModeEnum.Custom;
                captain.UsageCaptureCommand = "{\"executable\":\"/opt/usage/read-quota\"}";
                AssertEqual(UsageCaptureModeEnum.Custom, captain.UsageCaptureMode, "Custom mode set");
                AssertNotNull(captain.UsageCaptureCommand, "command stored in Custom mode");

                captain.UsageCaptureMode = UsageCaptureModeEnum.Auto;
                AssertNull(captain.UsageCaptureCommand, "switching to Auto clears the command");

                captain.UsageCaptureMode = UsageCaptureModeEnum.Custom;
                captain.UsageCaptureCommand = "{\"executable\":\"/opt/usage/other\"}";
                captain.UsageCaptureMode = UsageCaptureModeEnum.Disabled;
                AssertNull(captain.UsageCaptureCommand, "switching to Disabled clears the command");

                captain.UsageCaptureCommand = "  ";
                AssertNull(captain.UsageCaptureCommand, "whitespace command normalizes to null");
            }));

            cases.Add(Case("captain_disabled_mode_wins_over_enabled", "Disabled mode always wins over the enabled flag", TestTags.Positive, () =>
            {
                Captain captain = new Captain("opt-out-captain");
                captain.UsageCaptureEnabled = true;
                captain.UsageCaptureMode = UsageCaptureModeEnum.Auto;
                AssertTrue(captain.UsageCaptureActive, "enabled Auto captain is active");

                captain.UsageCaptureMode = UsageCaptureModeEnum.Disabled;
                AssertFalse(captain.UsageCaptureActive, "Disabled mode wins over enabled=true");

                captain.UsageCaptureMode = UsageCaptureModeEnum.Custom;
                AssertTrue(captain.UsageCaptureActive, "enabled Custom captain is active again");

                captain.UsageCaptureEnabled = false;
                AssertFalse(captain.UsageCaptureActive, "disabled flag wins for Auto and Custom");

                Captain deserialized = JsonSerializer.Deserialize<Captain>("{\"UsageCaptureEnabled\":true,\"UsageCaptureMode\":\"Disabled\"}")!;
                AssertFalse(deserialized.UsageCaptureActive, "Disabled wins after deserialization too");
            }));

            cases.Add(Case("captain_unknown_usage_capture_mode_rejected", "Unknown usage capture mode strings are rejected", TestTags.Negative, () =>
            {
                AssertThrows<JsonException>(() => JsonSerializer.Deserialize<Captain>("{\"UsageCaptureMode\":\"Bogus\"}"));
            }));

            cases.Add(Case("usage_capture_settings_defaults", "ArmadaSettings.UsageCapture defaults to the disabled contract", TestTags.Positive, () =>
            {
                ArmadaSettings defaults = JsonSerializer.Deserialize<ArmadaSettings>("{}")!;
                AssertNotNull(defaults.UsageCapture, "settings never deserialize a null UsageCapture block");

                UsageCaptureSettings usage = defaults.UsageCapture;
                AssertFalse(usage.Enabled, "global switch defaults off");
                AssertEqual(60, usage.MinimumIntervalSeconds, "default minimum interval is 60");
                AssertFalse(usage.AllowPtyFallback, "PTY fallback defaults off");
                AssertEqual(20, usage.ProbeTimeoutSeconds, "default probe timeout is 20 seconds");
                AssertEqual(4, usage.MaximumConcurrentProbes, "default overall concurrency is 4");
                AssertEqual(1, usage.MaximumConcurrentProbesPerHost, "default per-host concurrency is 1");
                AssertEqual(3600, usage.MaximumBackoffSeconds, "default backoff cap is 3600 seconds");
                AssertEqual(2048, usage.MaximumExcerptBytes, "default excerpt cap is 2048 bytes");
                AssertEqual(7, usage.RawSnapshotRetentionDays, "raw snapshots default to 7 days");
                AssertEqual(90, usage.RollupRetentionDays, "rollups default to 90 days");

                ArmadaSettings constructed = new ArmadaSettings();
                AssertFalse(constructed.UsageCapture.Enabled, "constructed settings default to disabled capture");
                AssertEqual(60, constructed.UsageCapture.MinimumIntervalSeconds, "constructed minimum interval matches");

                ArmadaSettings restored = new ArmadaSettings();
                restored.UsageCapture = null!;
                AssertFalse(restored.UsageCapture.Enabled, "assigning null restores the default settings");
            }));

            cases.Add(Case("usage_capture_settings_clamp_bounds", "Every UsageCaptureSettings bound clamps consistently", TestTags.Positive, () =>
            {
                UsageCaptureSettings usage = new UsageCaptureSettings();

                usage.MinimumIntervalSeconds = 1;
                AssertEqual(60, usage.MinimumIntervalSeconds, "minimum interval clamps up to 60");
                usage.MinimumIntervalSeconds = 100000;
                AssertEqual(86400, usage.MinimumIntervalSeconds, "minimum interval clamps down to 86400");

                usage.ProbeTimeoutSeconds = 0;
                AssertEqual(1, usage.ProbeTimeoutSeconds, "probe timeout clamps up to 1");
                usage.ProbeTimeoutSeconds = 1000;
                AssertEqual(300, usage.ProbeTimeoutSeconds, "probe timeout clamps down to 300");

                usage.MaximumConcurrentProbes = 0;
                AssertEqual(1, usage.MaximumConcurrentProbes, "overall concurrency clamps up to 1");
                usage.MaximumConcurrentProbes = 1000;
                AssertEqual(64, usage.MaximumConcurrentProbes, "overall concurrency clamps down to 64");

                usage.MaximumConcurrentProbesPerHost = 0;
                AssertEqual(1, usage.MaximumConcurrentProbesPerHost, "per-host concurrency clamps up to 1");
                usage.MaximumConcurrentProbesPerHost = 1000;
                AssertEqual(64, usage.MaximumConcurrentProbesPerHost, "per-host concurrency clamps down to 64");

                usage.MaximumBackoffSeconds = 1;
                AssertEqual(60, usage.MaximumBackoffSeconds, "backoff clamps up to 60");
                usage.MaximumBackoffSeconds = 100000;
                AssertEqual(86400, usage.MaximumBackoffSeconds, "backoff clamps down to 86400");

                usage.MaximumExcerptBytes = 0;
                AssertEqual(256, usage.MaximumExcerptBytes, "excerpt cap clamps up to 256");
                usage.MaximumExcerptBytes = 1000000;
                AssertEqual(65536, usage.MaximumExcerptBytes, "excerpt cap clamps down to 65536");

                usage.RawSnapshotRetentionDays = 0;
                AssertEqual(1, usage.RawSnapshotRetentionDays, "raw retention clamps up to 1 day");
                usage.RawSnapshotRetentionDays = 10000;
                AssertEqual(365, usage.RawSnapshotRetentionDays, "raw retention clamps down to 365 days");

                usage.RollupRetentionDays = 0;
                AssertEqual(7, usage.RollupRetentionDays, "rollup retention clamps up to 7 days");
                usage.RollupRetentionDays = 100000;
                AssertEqual(1825, usage.RollupRetentionDays, "rollup retention clamps down to 1825 days");
            }));

            cases.Add(Case("usage_windows_json_round_trip_and_null_safety", "The windows codec round-trips and never crashes on empty input", TestTags.Positive, () =>
            {
                AssertEqual(0, UsageWindowsJson.Deserialize(null).Count, "null json deserializes to an empty list");
                AssertEqual(0, UsageWindowsJson.Deserialize(String.Empty).Count, "empty json deserializes to an empty list");
                AssertEqual(0, UsageWindowsJson.Deserialize("[]").Count, "empty array deserializes to an empty list");
                AssertEqual(0, UsageWindowsJson.Deserialize("{}").Count, "empty object does not crash");
                AssertEqual(0, UsageWindowsJson.Deserialize("not json at all").Count, "malformed json becomes an empty list");

                List<UsageWindow> emptyObjectWindows = UsageWindowsJson.Deserialize("[{}]");
                AssertEqual(1, emptyObjectWindows.Count, "empty window object deserializes");
                AssertEqual(String.Empty, emptyObjectWindows[0].Name, "missing window name defaults to empty");
                AssertNull(emptyObjectWindows[0].UsedPercent, "missing percent stays null");

                AssertNull(UsageWindowsJson.Serialize(null), "null windows serialize to null");
                AssertNull(UsageWindowsJson.Serialize(new List<UsageWindow>()), "empty windows serialize to null");

                List<UsageWindow> windows = new List<UsageWindow>();
                windows.Add(new UsageWindow
                {
                    Name = "five_hour",
                    UsedPercent = 62.0,
                    Used = 625,
                    Limit = 1000,
                    Unit = "tokens",
                    WindowMinutes = 300,
                    ResetsUtc = new DateTime(2026, 10, 27, 14, 5, 0, DateTimeKind.Utc),
                    Model = null,
                    Scope = "org-1",
                    ConfigurationRevision = "rev-9",
                    CoverageNote = "configured daily budget"
                });
                windows.Add(new UsageWindow
                {
                    Name = "per-model",
                    UsedPercent = null,
                    Model = "claude-opnet-4",
                    CoverageNote = null
                });

                string json = UsageWindowsJson.Serialize(windows)!;
                AssertContains("\"usedPercent\":62", json, "windows use the documented camelCase contract names");
                AssertContains("\"resetsUtc\"", json, "reset timestamps use the documented name");
                AssertContains("\"model\":\"claude-opnet-4\"", json, "per-model window keeps its model");

                List<UsageWindow> roundTripped = UsageWindowsJson.Deserialize(json);
                AssertEqual(2, roundTripped.Count, "both windows round-trip");
                AssertEqual("five_hour", roundTripped[0].Name, "window name round-trips");
                AssertEqual(62.0, roundTripped[0].UsedPercent, "window percent round-trips");
                AssertEqual(625, roundTripped[0].Used, "window usage round-trips");
                AssertEqual(1000, roundTripped[0].Limit, "window limit round-trips");
                AssertEqual("tokens", roundTripped[0].Unit, "window unit round-trips");
                AssertEqual(300, roundTripped[0].WindowMinutes, "window minutes round-trip");
                AssertEqual(new DateTime(2026, 10, 27, 14, 5, 0, DateTimeKind.Utc), roundTripped[0].ResetsUtc, "reset time round-trips");
                AssertNull(roundTripped[0].Model, "null model stays null");
                AssertEqual("org-1", roundTripped[0].Scope, "scope round-trips");
                AssertEqual("rev-9", roundTripped[0].ConfigurationRevision, "estimated revision round-trips");
                AssertEqual("configured daily budget", roundTripped[0].CoverageNote, "coverage note round-trips");
                AssertNull(roundTripped[1].UsedPercent, "unknown percent stays unknown, never zero");
                AssertNull(roundTripped[1].CoverageNote, "null coverage note stays null");
            }));

            cases.Add(Case("usage_snapshot_enums_serialize_as_strings", "Usage snapshot statuses and sources serialize as named strings", TestTags.Positive, () =>
            {
                AssertContains("\"Ok\"", JsonSerializer.Serialize(UsageSnapshotStatusEnum.Ok), "Ok serializes by name");
                AssertContains("\"AuthMissing\"", JsonSerializer.Serialize(UsageSnapshotStatusEnum.AuthMissing), "AuthMissing serializes by name");
                AssertContains("\"Estimated\"", JsonSerializer.Serialize(UsageSnapshotStatusEnum.Estimated), "Estimated serializes by name");

                AssertEqual(UsageSnapshotStatusEnum.Failed, JsonSerializer.Deserialize<UsageSnapshotStatusEnum>("\"Failed\""), "Failed deserializes by name");
                AssertEqual(UsageCaptureSourceEnum.OAuthEndpoint, JsonSerializer.Deserialize<UsageCaptureSourceEnum>("\"OAuthEndpoint\""), "OAuthEndpoint deserializes by name");
                AssertEqual(UsageCaptureSourceEnum.SessionLog, JsonSerializer.Deserialize<UsageCaptureSourceEnum>("\"SessionLog\""), "SessionLog deserializes by name");
                AssertEqual(UsageCaptureSourceEnum.Pty, JsonSerializer.Deserialize<UsageCaptureSourceEnum>("\"Pty\""), "Pty deserializes by name");
                AssertEqual(UsageCaptureSourceEnum.Headers, JsonSerializer.Deserialize<UsageCaptureSourceEnum>("\"Headers\""), "Headers deserializes by name");
                AssertEqual(UsageCaptureSourceEnum.Derived, JsonSerializer.Deserialize<UsageCaptureSourceEnum>("\"Derived\""), "Derived deserializes by name");
                AssertEqual(UsageCaptureSourceEnum.Custom, JsonSerializer.Deserialize<UsageCaptureSourceEnum>("\"Custom\""), "Custom deserializes by name");
            }));

            cases.Add(Case("usage_snapshot_enums_reject_unknown_values", "Unknown snapshot status and source strings are rejected", TestTags.Negative, () =>
            {
                AssertThrows<JsonException>(() => JsonSerializer.Deserialize<UsageSnapshotStatusEnum>("\"Unknown\""));
                AssertThrows<JsonException>(() => JsonSerializer.Deserialize<UsageCaptureSourceEnum>("\"Prompt\""));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Defaults",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}