namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Extended database tests for Captain covering persona and instruction properties.
    /// </summary>
    public class CaptainExtendedDatabaseTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Captain Extended Database";

        /// <summary>
        /// Run all extended captain database tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync persists SystemInstructions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("instructions-captain", AgentRuntimeEnum.ClaudeCode);
                    captain.SystemInstructions = "Always review code carefully.";
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual("Always review code carefully.", result!.SystemInstructions);
                }
            });

            await RunTest("CreateAsync persists AllowedPersonas", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("persona-captain");
                    captain.AllowedPersonas = "[\"Worker\", \"Judge\"]";
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual("[\"Worker\", \"Judge\"]", result!.AllowedPersonas);
                }
            });

            await RunTest("CreateAsync persists PreferredPersona", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("preferred-captain");
                    captain.PreferredPersona = "Architect";
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual("Architect", result!.PreferredPersona);
                }
            });

            await RunTest("UpdateAsync modifies SystemInstructions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("update-instructions");
                    captain.SystemInstructions = "Original instructions";
                    await db.Captains.CreateAsync(captain);

                    captain.SystemInstructions = "Updated instructions";
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual("Updated instructions", result!.SystemInstructions);
                }
            });

            await RunTest("UpdateAsync clears SystemInstructions to null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("clear-instructions");
                    captain.SystemInstructions = "Some instructions";
                    await db.Captains.CreateAsync(captain);

                    captain.SystemInstructions = null;
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNull(result!.SystemInstructions);
                }
            });

            await RunTest("UpdateAsync modifies AllowedPersonas", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("update-personas");
                    captain.AllowedPersonas = "[\"Worker\"]";
                    await db.Captains.CreateAsync(captain);

                    captain.AllowedPersonas = "[\"Worker\", \"Architect\", \"Judge\"]";
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual("[\"Worker\", \"Architect\", \"Judge\"]", result!.AllowedPersonas);
                }
            });

            await RunTest("UpdateAsync modifies PreferredPersona", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("update-preferred");
                    captain.PreferredPersona = "Worker";
                    await db.Captains.CreateAsync(captain);

                    captain.PreferredPersona = "Judge";
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual("Judge", result!.PreferredPersona);
                }
            });

            await RunTest("CreateAsync with null optional fields persists correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("null-optional");
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertNull(result!.SystemInstructions);
                    AssertNull(result.AllowedPersonas);
                    AssertNull(result.PreferredPersona);
                    AssertNull(result.TenantId);
                    AssertNull(result.UserId);
                    AssertNull(result.CurrentMissionId);
                    AssertNull(result.CurrentDockId);
                    AssertNull(result.ProcessId);
                    AssertNull(result.LastHeartbeatUtc);
                }
            });

            await RunTest("CreateAsync with TenantId persists correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("Test Tenant");
                    await db.Tenants.CreateAsync(tenant);

                    Captain captain = new Captain("tenant-captain");
                    captain.TenantId = tenant.Id;
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(tenant.Id, result!.TenantId);
                }
            });

            await RunTest("ReadByNameAsync returns null for nonexistent name", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain? result = await db.Captains.ReadByNameAsync("nonexistent-captain");
                    AssertNull(result);
                }
            });

            await RunTest("Multiple captains with different runtimes", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain claudeCaptain = new Captain("claude-captain", AgentRuntimeEnum.ClaudeCode);
                    Captain codexCaptain = new Captain("codex-captain", AgentRuntimeEnum.Codex);

                    await db.Captains.CreateAsync(claudeCaptain);
                    await db.Captains.CreateAsync(codexCaptain);

                    Captain? readClaude = await db.Captains.ReadAsync(claudeCaptain.Id);
                    Captain? readCodex = await db.Captains.ReadAsync(codexCaptain.Id);

                    AssertEqual(AgentRuntimeEnum.ClaudeCode, readClaude!.Runtime);
                    AssertEqual(AgentRuntimeEnum.Codex, readCodex!.Runtime);
                }
            });
        }
    }
}
