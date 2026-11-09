namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the user database methods: create, tenant-scoped read, tenant-less
    /// read-by-id, read-by-email (tenant-scoped and cross-tenant), update, delete, tenant-scoped
    /// enumeration, and existence checks. Each case runs against its own fresh SQLite store.
    /// Negative cases cover cross-tenant reads and lookups of nonexistent identifiers returning null.
    /// Legacy-hash cases cover startup upgrade: a hash that is still the value read is stretched, and a
    /// password stored after that read is left as stored.
    /// </summary>
    public sealed class UserMethodsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.UserMethods";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the User Methods suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_async_returns_user", "CreateAsync returns user", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "alice@example.com", "password123");
                    UserMaster result = await db.Users.CreateAsync(user);

                    AssertNotNull(result);
                    AssertEqual("alice@example.com", result.Email);
                    AssertEqual(tenantId, result.TenantId);
                }
            }));

            cases.Add(CaseAsync("read_async_returns_user_by_tenant_and_id", "ReadAsync returns user by tenant and id", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "bob@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadAsync(tenantId, user.Id);
                    AssertNotNull(result);
                    AssertEqual(user.Id, result!.Id);
                    AssertEqual("bob@example.com", result.Email);
                }
            }));

            cases.Add(CaseAsync("read_async_wrong_tenant_returns_null", "ReadAsync wrong tenant returns null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);
                    string otherTenantId = await CreateTestTenantAsync(db, "Other Tenant");

                    UserMaster user = new UserMaster(tenantId, "carol@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    // Try to read with wrong tenant
                    UserMaster? result = await db.Users.ReadAsync(otherTenantId, user.Id);
                    AssertNull(result);
                }
            }));

            cases.Add(CaseAsync("read_by_id_async_returns_user_without_tenant_filter", "ReadByIdAsync returns user without tenant filter", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "dan@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadByIdAsync(user.Id);
                    AssertNotNull(result);
                    AssertEqual(user.Id, result!.Id);
                }
            }));

            cases.Add(CaseAsync("read_by_email_async_returns_user_within_tenant", "ReadByEmailAsync returns user within tenant", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "eve@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadByEmailAsync(tenantId, "eve@example.com");
                    AssertNotNull(result);
                    AssertEqual(user.Id, result!.Id);
                }
            }));

            cases.Add(CaseAsync("read_by_email_async_wrong_tenant_returns_null", "ReadByEmailAsync wrong tenant returns null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);
                    string otherTenantId = await CreateTestTenantAsync(db, "Other Tenant");

                    UserMaster user = new UserMaster(tenantId, "frank@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadByEmailAsync(otherTenantId, "frank@example.com");
                    AssertNull(result);
                }
            }));

            cases.Add(CaseAsync("read_by_email_any_tenant_async_returns_users_across_tenants", "ReadByEmailAnyTenantAsync returns users across tenants", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenant1 = await CreateTestTenantAsync(db, "Tenant 1");
                    string tenant2 = await CreateTestTenantAsync(db, "Tenant 2");

                    string sharedEmail = "shared@example.com";
                    await db.Users.CreateAsync(new UserMaster(tenant1, sharedEmail, "pass1"));
                    await db.Users.CreateAsync(new UserMaster(tenant2, sharedEmail, "pass2"));

                    List<UserMaster> results = await db.Users.ReadByEmailAnyTenantAsync(sharedEmail);
                    AssertEqual(2, results.Count);
                }
            }));

            cases.Add(CaseAsync("update_async_modifies_user", "UpdateAsync modifies user", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "gina@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    user.FirstName = "Gina";
                    user.LastName = "Smith";
                    user.IsAdmin = true;
                    await db.Users.UpdateAsync(user);

                    UserMaster? result = await db.Users.ReadAsync(tenantId, user.Id);
                    AssertEqual("Gina", result!.FirstName);
                    AssertEqual("Smith", result.LastName);
                    AssertTrue(result.IsAdmin);
                }
            }));

            cases.Add(CaseAsync("delete_async_removes_user", "DeleteAsync removes user", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "henry@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    await db.Users.DeleteAsync(tenantId, user.Id);
                    AssertNull(await db.Users.ReadAsync(tenantId, user.Id));
                }
            }));

            cases.Add(CaseAsync("enumerate_async_returns_tenant_scoped_users", "EnumerateAsync returns tenant-scoped users", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenant1 = await CreateTestTenantAsync(db, "Enum Tenant 1");
                    string tenant2 = await CreateTestTenantAsync(db, "Enum Tenant 2");

                    await db.Users.CreateAsync(new UserMaster(tenant1, "a@t1.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenant1, "b@t1.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenant2, "c@t2.com", "pass"));

                    List<UserMaster> t1Users = await db.Users.EnumerateAsync(tenant1);
                    AssertEqual(2, t1Users.Count);

                    List<UserMaster> t2Users = await db.Users.EnumerateAsync(tenant2);
                    AssertEqual(1, t2Users.Count);
                }
            }));

            cases.Add(CaseAsync("exists_async_correct_for_tenant_scoped_lookup", "ExistsAsync correct for tenant-scoped lookup", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);
                    string otherTenantId = await CreateTestTenantAsync(db, "Other");

                    UserMaster user = new UserMaster(tenantId, "exists@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    AssertTrue(await db.Users.ExistsAsync(tenantId, user.Id));
                    AssertFalse(await db.Users.ExistsAsync(otherTenantId, user.Id));
                    AssertFalse(await db.Users.ExistsAsync(tenantId, "usr_nonexistent"));
                }
            }));

            cases.Add(CaseAsync("read_async_nonexistent_returns_null_audit", "ReadAsync nonexistent returns null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster? result = await db.Users.ReadAsync(tenantId, "usr_nonexistent");
                    AssertNull(result);
                }
            }));

            cases.Add(CaseAsync("read_by_id_async_nonexistent_returns_null_audit", "ReadByIdAsync nonexistent returns null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    UserMaster? result = await db.Users.ReadByIdAsync("usr_nonexistent");
                    AssertNull(result);
                }
            }));

            cases.Add(CaseAsync("legacy_upgrade_keeps_a_password_changed_after_the_read", "Legacy upgrade keeps a password changed after the read", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db, "Hash Upgrade Tenant");
                    UserMaster created = new UserMaster(tenantId, "legacy-upgrade@example.com", "original-secret");
                    created.FirstName = "Original";
                    created.LastName = "Profile";
                    await db.Users.CreateAsync(created);
                    created.FirstName = "Original";
                    created.LastName = "Profile";
                    await db.Users.UpdateAsync(created);

                    string legacy = UserMaster.ComputePasswordHash("original-secret");
                    await SetStoredPasswordAsync(testDb.ConnectionString, tenantId, created.Id, legacy);
                    UserMaster before = (await db.Users.ReadAsync(tenantId, created.Id))!;
                    AssertEqual(legacy, before.PasswordSha256, "seeded legacy hash");
                    AssertEqual("Original", before.FirstName, "profile before the upgrade read");

                    string replacement = PasswordHasher.HashPassword("replacement-secret");
                    IUserMethods users = db.Users;
                    UserMaster? changed = null;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    PasswordHashUpgradeService service = new PasswordHashUpgradeService(db, logging);
                    int upgraded;
                    using (UserMethodsScope scope = new UserMethodsScope(db, LegacyUpgradeReadProxy.Create(users, created.Id, async () =>
                    {
                        UserMaster current = (await users.ReadAsync(tenantId, created.Id))!;
                        current.PasswordSha256 = replacement;
                        current.FirstName = "Replaced";
                        await users.UpdateAsync(current);
                        changed = await users.ReadAsync(tenantId, created.Id);
                    })))
                    {
                        upgraded = await service.UpgradeLegacyHashesAsync();
                    }

                    AssertNotNull(changed, "password change after the upgrade read");
                    UserMaster after = (await db.Users.ReadAsync(tenantId, created.Id))!;
                    AssertEqual(changed!.PasswordSha256, after.PasswordSha256, "password stored after the read");
                    AssertEqual(replacement, after.PasswordSha256, "adaptive replacement is still stored");
                    AssertEqual(changed.FirstName, after.FirstName, "profile stored after the read");
                    AssertEqual("Replaced", after.FirstName, "replacement profile");
                    AssertEqual(changed.Email, after.Email, "email");
                    AssertEqual(changed.LastName, after.LastName, "last name");
                    AssertEqual(changed.IsAdmin, after.IsAdmin, "admin");
                    AssertEqual(changed.IsTenantAdmin, after.IsTenantAdmin, "tenant admin");
                    AssertEqual(changed.IsProtected, after.IsProtected, "protected");
                    AssertEqual(changed.Active, after.Active, "active");
                    AssertEqual(changed.TenantId, after.TenantId, "tenant");
                    AssertEqual(changed.LastUpdateUtc, after.LastUpdateUtc, "last update");
                    AssertEqual(0, upgraded, "a changed hash is not counted as upgraded");
                    AssertTrue(after.VerifyPassword("replacement-secret"), "replacement password verifies");
                    AssertFalse(after.VerifyPassword("original-secret"), "original password no longer verifies");
                    AssertEqual("Original", before.FirstName, "stale read still had the earlier profile");
                }
            }));

            cases.Add(CaseAsync("legacy_upgrade_replaces_an_unchanged_legacy_hash", "Legacy upgrade replaces an unchanged legacy hash", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db, "Hash Upgrade Tenant");
                    UserMaster created = new UserMaster(tenantId, "legacy-keep@example.com", "legacy-secret");
                    created.FirstName = "Kept";
                    created.LastName = "Profile";
                    await db.Users.CreateAsync(created);
                    created.FirstName = "Kept";
                    created.LastName = "Profile";
                    await db.Users.UpdateAsync(created);

                    string legacy = UserMaster.ComputePasswordHash("legacy-secret");
                    await SetStoredPasswordAsync(testDb.ConnectionString, tenantId, created.Id, legacy);
                    UserMaster before = (await db.Users.ReadAsync(tenantId, created.Id))!;
                    AssertEqual(1, await CountLegacyHashesAsync(db), "only the seeded user is legacy");

                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    PasswordHashUpgradeService service = new PasswordHashUpgradeService(db, logging);
                    int upgraded = await service.UpgradeLegacyHashesAsync();

                    UserMaster after = (await db.Users.ReadAsync(tenantId, created.Id))!;
                    AssertEqual(1, upgraded, "one legacy hash upgraded");
                    AssertTrue(PasswordHasher.IsAdaptiveHash(after.PasswordSha256), "stored hash is adaptive");
                    AssertNotEqual(legacy, after.PasswordSha256, "legacy value was replaced");
                    AssertTrue(after.VerifyPassword("legacy-secret"), "original password still verifies");
                    AssertEqual(before.Email, after.Email, "email");
                    AssertEqual(before.FirstName, after.FirstName, "first name");
                    AssertEqual(before.LastName, after.LastName, "last name");
                    AssertEqual(before.IsAdmin, after.IsAdmin, "admin");
                    AssertEqual(before.IsTenantAdmin, after.IsTenantAdmin, "tenant admin");
                    AssertEqual(before.IsProtected, after.IsProtected, "protected");
                    AssertEqual(before.Active, after.Active, "active");
                    AssertEqual(before.TenantId, after.TenantId, "tenant");
                    AssertEqual(before.LastUpdateUtc, after.LastUpdateUtc, "last update");
                    AssertEqual(0, await service.UpgradeLegacyHashesAsync(), "a second pass finds nothing to upgrade");
                    AssertEqual(after.PasswordSha256, (await db.Users.ReadAsync(tenantId, created.Id))!.PasswordSha256, "second pass leaves the hash");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "User Database Methods",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> CreateTestTenantAsync(DatabaseDriver db, string name = "Test Tenant")
        {
            TenantMetadata tenant = new TenantMetadata(name);
            await db.Tenants.CreateAsync(tenant);
            return tenant.Id;
        }

        private static async Task<int> CountLegacyHashesAsync(DatabaseDriver db)
        {
            EnumerationQuery query = new EnumerationQuery();
            query.PageSize = 1000;
            EnumerationResult<UserMaster> page = await db.Users.EnumerateAsync(query);
            int count = 0;
            foreach (UserMaster user in page.Objects)
            {
                if (!PasswordHasher.IsAdaptiveHash(user.PasswordSha256)) count = count + 1;
            }

            return count;
        }

        private static async Task SetStoredPasswordAsync(string connectionString, string tenantId, string userId, string passwordHash)
        {
            using (DbConnection connection = OpenConnection(connectionString))
            {
                await connection.OpenAsync();
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE users SET password_sha256 = @password WHERE tenant_id = @tenantId AND id = @id";
                    AddParameter(command, "@password", passwordHash);
                    AddParameter(command, "@tenantId", tenantId);
                    AddParameter(command, "@id", userId);
                    int rows = await command.ExecuteNonQueryAsync();
                    AssertEqual(1, rows, "stored password update");
                }
            }
        }

        private static DbConnection OpenConnection(string connectionString)
        {
            if (TestDatabaseConfig.Type == DatabaseTypeEnum.Postgresql) return new NpgsqlConnection(connectionString);
            if (TestDatabaseConfig.Type == DatabaseTypeEnum.Mysql) return new MySqlConnection(connectionString);
            if (TestDatabaseConfig.Type == DatabaseTypeEnum.SqlServer) return new SqlConnection(connectionString);
            return new SqliteConnection(connectionString);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
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

    /// <summary>
    /// Writes a replacement password after an unscoped user page has been read and before that page is returned.
    /// The returned user objects keep the values from the read. Later writes from the upgrade see a row that has
    /// already changed.
    /// </summary>
    public class LegacyUpgradeReadProxy : DispatchProxy
    {
        private IUserMethods _Inner = null!;
        private string _UserId = "";
        private Func<Task> _AfterRead = null!;
        private bool _Applied;

        /// <summary>
        /// Wrap user methods so the first page that contains <paramref name="userId"/> applies <paramref name="afterRead"/>.
        /// </summary>
        /// <param name="inner">User methods the upgrade already holds.</param>
        /// <param name="userId">User whose row changes after it has been read.</param>
        /// <param name="afterRead">Write that stores the new password.</param>
        /// <returns>Wrapped user methods.</returns>
        public static IUserMethods Create(IUserMethods inner, string userId, Func<Task> afterRead)
        {
            LegacyUpgradeReadProxy proxy = (LegacyUpgradeReadProxy)(object)DispatchProxy.Create<IUserMethods, LegacyUpgradeReadProxy>();
            proxy._Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            proxy._UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            proxy._AfterRead = afterRead ?? throw new ArgumentNullException(nameof(afterRead));
            return (IUserMethods)(object)proxy;
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));
            object? result = targetMethod.Invoke(_Inner, args);
            if (!IsUnscopedEnumerate(targetMethod, args) || _Applied) return result;

            Task<EnumerationResult<UserMaster>> pageTask = (Task<EnumerationResult<UserMaster>>)result!;
            return ReturnPageAfterWriteAsync(pageTask);
        }

        private static bool IsUnscopedEnumerate(MethodInfo targetMethod, object?[]? args)
        {
            if (!String.Equals(targetMethod.Name, "EnumerateAsync", StringComparison.Ordinal)) return false;
            if (args == null || args.Length != 2) return false;
            return args[0] is EnumerationQuery;
        }

        private async Task<EnumerationResult<UserMaster>> ReturnPageAfterWriteAsync(Task<EnumerationResult<UserMaster>> pageTask)
        {
            EnumerationResult<UserMaster> page = await pageTask.ConfigureAwait(false);
            bool containsUser = false;
            foreach (UserMaster user in page.Objects)
            {
                if (String.Equals(user.Id, _UserId, StringComparison.Ordinal)) containsUser = true;
            }

            if (containsUser && !_Applied)
            {
                _Applied = true;
                await _AfterRead().ConfigureAwait(false);
            }

            return page;
        }
    }

    /// <summary>
    /// Puts a user-method implementation in place for one upgrade call, then restores the previous one.
    /// </summary>
    internal sealed class UserMethodsScope : IDisposable
    {
        private readonly FieldInfo _Field;
        private readonly DatabaseDriver _Driver;
        private readonly object? _Previous;
        private bool _Disposed;

        /// <summary>
        /// Replace the driver's user methods until this scope is disposed.
        /// </summary>
        /// <param name="driver">Driver the upgrade service reads.</param>
        /// <param name="replacement">User methods to install.</param>
        public UserMethodsScope(DatabaseDriver driver, IUserMethods replacement)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            FieldInfo? field = typeof(DatabaseDriver).GetField("_Users", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new AssertionException("DatabaseDriver user methods are not available to the test");
            _Field = field;
            _Driver = driver;
            _Previous = _Field.GetValue(driver);
            _Field.SetValue(driver, replacement);
        }

        /// <summary>
        /// Restore the user methods that were installed before this scope.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Field.SetValue(_Driver, _Previous);
        }
    }
}
