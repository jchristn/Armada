namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Live configuration publication, environment precedence, optimistic edits and recoverable file writes.</summary>
    public sealed class UsageSelectionConfigurationSuite : IArmadaTestSuite
    {
        private const string _SuiteId = "Services.UsageSelectionConfiguration";

        /// <summary>Build self-contained policy configuration and persistence descriptors.</summary>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, Func<CancellationToken, Task> body) => cases.Add(new TestCaseDescriptor(_SuiteId, id, id.Replace('_', ' '), body));
            Add("live_update_is_persisted_audited_and_detached_with_revision_conflicts", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string directory = Temp();
                try
                {
                    ArmadaSettings settings = Settings(directory); await settings.SaveAsync().ConfigureAwait(false);
                    AuthContext actor = await ActorAsync(database, token).ConfigureAwait(false);
                    using UsageSelectionConfigurationService service = Service(settings, database);
                    UsageSelectionConfiguration before = service.Read();
                    UsageSelectionConfiguration after = await service.ApplyAsync(new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostUsedAllowance, ReservePercent = 15 }, actor, before.Revision, token).ConfigureAwait(false);
                    AssertEqual(CaptainSelectionStrategyEnum.MostUsedAllowance, settings.EffectiveUsageSelection.Strategy);
                    AssertTrue(after.Revision > before.Revision); AssertEqual(15.0, settings.UsageSelection.ReservePercent);
                    AssertEqual(after.Revision, (await ArmadaSettings.LoadAsync(settings.EffectiveSettingsFilePath).ConfigureAwait(false)).UsageSelectionRevision);
                    after.Settings.ReservePercent = 0; AssertEqual(15.0, service.Read().Settings.ReservePercent, "read response cannot mutate the live group");
                    await AssertThrowsAsync<UsageSelectionConfigurationException>(() => service.ApplyAsync(new UsageSelectionSettings(), actor, before.Revision, token)).ConfigureAwait(false);
                    AssertEqual(1, (await database.Driver.Events.EnumerateByTypeAsync("settings.usage-selection.requested", token: token).ConfigureAwait(false)).Count);
                    AssertEqual(1, (await database.Driver.Events.EnumerateByTypeAsync("settings.usage-selection.applied", token: token).ConfigureAwait(false)).Count);
                    AssertFalse(UsageSelectionSettingsFileStore.HasPendingRecovery(settings.EffectiveSettingsFilePath));
                }
                finally { Directory.Delete(directory, true); }
            });
            Add("environment_override_is_read_only_and_never_saved_over_file_values", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string directory = Temp();
                try
                {
                    ArmadaSettings settings = Settings(directory); await settings.SaveAsync().ConfigureAwait(false);
                    AuthContext actor = await ActorAsync(database, token).ConfigureAwait(false);
                    using UsageSelectionConfigurationService service = Service(settings, database, "{\"Strategy\":\"MostRemainingAllowance\"}");
                    AssertEqual(CaptainSelectionStrategyEnum.MostRemainingAllowance, service.Read().Settings.Strategy);
                    AssertTrue(service.Read().Capabilities.EnvironmentOverride);
                    await AssertThrowsAsync<UsageSelectionConfigurationException>(() => service.ApplyAsync(new UsageSelectionSettings(), actor, token: token)).ConfigureAwait(false);
                    settings.MaxCaptains = 23; await settings.SaveAsync().ConfigureAwait(false);
                    AssertEqual(CaptainSelectionStrategyEnum.FirstAvailable, (await ArmadaSettings.LoadAsync(settings.EffectiveSettingsFilePath).ConfigureAwait(false)).UsageSelection.Strategy);
                }
                finally { Directory.Delete(directory, true); }
            });
            Add("tenant_admin_and_unavailable_enforcement_cannot_change_policy", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string directory = Temp();
                try
                {
                    ArmadaSettings settings = Settings(directory); await settings.SaveAsync().ConfigureAwait(false);
                    AuthContext actor = await ActorAsync(database, token).ConfigureAwait(false);
                    using UsageSelectionConfigurationService service = Service(settings, database);
                    actor.IsAdmin = false; actor.IsTenantAdmin = true;
                    await AssertThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(new UsageSelectionSettings(), actor, token: token)).ConfigureAwait(false);
                    actor.IsAdmin = true;
                    await AssertThrowsAsync<UsageSelectionConfigurationException>(() => service.ApplyAsync(new UsageSelectionSettings { Mode = UsageSelectionModeEnum.Enforce }, actor, token: token)).ConfigureAwait(false);
                    AssertEqual(0L, settings.UsageSelectionRevision); AssertEqual(CaptainSelectionStrategyEnum.FirstAvailable, settings.UsageSelection.Strategy);
                }
                finally { Directory.Delete(directory, true); }
            });
            Add("failed_persistence_does_not_publish_live_settings", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string directory = Temp();
                try
                {
                    ArmadaSettings settings = Settings(directory);
                    settings.SettingsFilePath = directory;
                    AuthContext actor = await ActorAsync(database, token).ConfigureAwait(false);
                    using UsageSelectionConfigurationService service = Service(settings, database);
                    await AssertThrowsAsync<IOException>(() => service.ApplyAsync(new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostUsedAllowance }, actor, token: token)).ConfigureAwait(false);
                    AssertEqual(CaptainSelectionStrategyEnum.FirstAvailable, settings.UsageSelection.Strategy); AssertEqual(0L, settings.UsageSelectionRevision);
                }
                finally { Directory.Delete(directory, true); }
            });
            Add("post_write_failure_restores_original_bytes_and_permissions", async token =>
            {
                string directory = Temp();
                try
                {
                    ArmadaSettings settings = Settings(directory); await settings.SaveAsync().ConfigureAwait(false);
                    string path = settings.EffectiveSettingsFilePath;
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    string original = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
                    await AssertThrowsAsync<IOException>(() => UsageSelectionSettingsFileStore.SaveForTestingAsync(path,
                        "{\"usageSelection\":{\"strategy\":\"MostUsedAllowance\"}}", () => throw new IOException("Injected post-write failure"), token)).ConfigureAwait(false);
                    AssertEqual(original, await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                    AssertFalse(UsageSelectionSettingsFileStore.HasPendingRecovery(path));
                    if (!OperatingSystem.IsWindows()) AssertEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
                }
                finally { Directory.Delete(directory, true); }
            });
            Add("startup_recovers_interrupted_write_before_parsing_and_is_idempotent", async token =>
            {
                string directory = Temp();
                try
                {
                    ArmadaSettings settings = Settings(directory); await settings.SaveAsync().ConfigureAwait(false);
                    string path = settings.EffectiveSettingsFilePath;
                    string original = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
                    string journal = Path.Combine(directory, ".settings.json.usage-selection-rollback");
                    await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new UsageSelectionSettingsRecovery { OriginalExists = true, OriginalContent = original }), token).ConfigureAwait(false);
                    await File.WriteAllTextAsync(path, "{", token).ConfigureAwait(false);
                    ArmadaSettings recovered = await ArmadaSettings.LoadAsync(path).ConfigureAwait(false);
                    AssertTrue(recovered.UsageSelectionRecoveryPerformed); AssertEqual(CaptainSelectionStrategyEnum.FirstAvailable, recovered.UsageSelection.Strategy);
                    AssertEqual(original, await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                    AssertFalse((await ArmadaSettings.LoadAsync(path).ConfigureAwait(false)).UsageSelectionRecoveryPerformed);
                }
                finally { Directory.Delete(directory, true); }
            });
            Add("diagnostic_labels_redact_secrets_email_and_credential_paths", token =>
            {
                string input = "owner@example.invalid /home/owner/.codex/auth.json sk-abcdefghijklmnopqrstuvwx";
                string safe = UsageSelectionEvidenceSanitizer.Label(input)!;
                AssertFalse(safe.Contains("owner@example.invalid", StringComparison.Ordinal));
                AssertFalse(safe.Contains("/home/owner", StringComparison.Ordinal));
                AssertFalse(safe.Contains("abcdefghijklmnopqrstuvwx", StringComparison.Ordinal));
                AssertEqual("claude-sonnet-4", UsageSelectionEvidenceSanitizer.Label("claude-sonnet-4"));
                return Task.CompletedTask;
            });
            return new TestSuiteDescriptor(_SuiteId, "Usage selection configuration", cases);
        }

        private static string Temp()
        {
            string path = Path.Combine(Path.GetTempPath(), "armada-usage-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path); return path;
        }
        private static ArmadaSettings Settings(string directory) => new ArmadaSettings { DataDirectory = directory, SettingsFilePath = Path.Combine(directory, "settings.json") };
        private static UsageSelectionConfigurationService Service(ArmadaSettings settings, TestDatabase database, string environment = "")
        {
            LoggingModule logging = new LoggingModule(); logging.Settings.EnableConsole = false;
            return new UsageSelectionConfigurationService(settings, database.Driver, logging, environmentPolicy: environment);
        }
        private static async Task<AuthContext> ActorAsync(TestDatabase database, CancellationToken token)
        {
            TenantMetadata tenant = await database.Driver.Tenants.CreateAsync(new TenantMetadata("policy-admin"), token).ConfigureAwait(false);
            UserMaster user = await database.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "admin@example.invalid", "fixture-password"), token).ConfigureAwait(false);
            return new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = tenant.Id, UserId = user.Id };
        }
    }
}
