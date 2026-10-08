namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Push;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the push_devices table added by migration 79: round trip of every field (including empty and
    /// multi-value categories), lookup by token, the unique token, updates that re-own a device, the active flag,
    /// filtered oldest-first listing, and deletes by id, user, and tenant; and the registration rules that rely on them
    /// (a token that changes owner gets a new device id; the per-user active device cap). Runs on every provider through
    /// the parity script.
    /// </summary>
    public sealed class PushDeviceDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.PushDevices";
        private static readonly DateTime _Fixed = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("roundtrip_all_fields", "A device round-trips every persisted field", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                PushDevice device = NewDevice("ten_rt", "usr_rt", _Fixed);
                device.Platform = PushPlatformEnum.Android;
                device.DeviceName = "Pixel";
                device.AppVersion = "1.0.0 (42)";
                device.Locale = "de-DE";
                device.Categories = new List<PushCategoryEnum> { PushCategoryEnum.CliPermission, PushCategoryEnum.VoyageFinished };
                device.Active = false;
                device.LastSeenUtc = _Fixed.AddMinutes(5);
                await db.PushDevices.CreateAsync(device).ConfigureAwait(false);

                PushDevice? read = await db.PushDevices.ReadAsync(device.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertTrue(read!.Id.StartsWith("pdv_", StringComparison.Ordinal), "pdv_ prefix: " + read.Id);
                AssertEqual("ten_rt", read.TenantId);
                AssertEqual("usr_rt", read.UserId);
                AssertEqual(PushPlatformEnum.Android, read.Platform);
                AssertEqual(device.ExpoPushToken, read.ExpoPushToken);
                AssertEqual("Pixel", read.DeviceName);
                AssertEqual("1.0.0 (42)", read.AppVersion);
                AssertEqual("de-DE", read.Locale);
                AssertEqual("CliPermission,VoyageFinished", String.Join(",", read.Categories));
                AssertFalse(read.Active, "inactive persisted");
                AssertSameInstant(_Fixed, read.CreatedUtc, "created");
                AssertSameInstant(_Fixed.AddMinutes(5), read.LastSeenUtc, "last seen");

                PushDevice muted = NewDevice("ten_rt", "usr_rt", _Fixed);
                muted.Categories = new List<PushCategoryEnum>();
                muted.DeviceName = null;
                await db.PushDevices.CreateAsync(muted).ConfigureAwait(false);
                PushDevice? readMuted = await db.PushDevices.ReadAsync(muted.Id).ConfigureAwait(false);
                AssertEqual(0, readMuted!.Categories.Count, "empty categories stay empty");
                AssertNull(readMuted.DeviceName, "null name");
                AssertTrue(readMuted.Active, "active by default");

                AssertNull(await db.PushDevices.ReadAsync("pdv_missing").ConfigureAwait(false), "missing id");
            }));

            cases.Add(CaseAsync("read_by_token_and_unique", "ReadByTokenAsync finds the device; a second row with the same token is rejected", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                PushDevice device = await db.PushDevices.CreateAsync(NewDevice("ten_t", "usr_t", _Fixed)).ConfigureAwait(false);
                PushDevice? byToken = await db.PushDevices.ReadByTokenAsync(device.ExpoPushToken).ConfigureAwait(false);
                AssertEqual(device.Id, byToken!.Id, "found by token");
                AssertNull(await db.PushDevices.ReadByTokenAsync("ExponentPushToken[unknown0000]").ConfigureAwait(false), "unknown token");

                PushDevice duplicate = NewDevice("ten_other", "usr_other", _Fixed);
                duplicate.ExpoPushToken = device.ExpoPushToken;
                bool threw = false;
                try { await db.PushDevices.CreateAsync(duplicate).ConfigureAwait(false); }
                catch (Exception) { threw = true; }
                AssertTrue(threw, "the token is unique");
            }));

            cases.Add(CaseAsync("update_reowns_and_set_active", "UpdateAsync moves a device to another user; SetActiveAsync toggles the flag", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                PushDevice device = await db.PushDevices.CreateAsync(NewDevice("ten_a", "usr_a", _Fixed)).ConfigureAwait(false);
                string token = device.ExpoPushToken;
                device.TenantId = "ten_b";
                device.UserId = "usr_b";
                device.Platform = PushPlatformEnum.Android;
                device.DeviceName = "renamed";
                device.Categories = new List<PushCategoryEnum> { PushCategoryEnum.AskProposal };
                device.LastSeenUtc = _Fixed.AddHours(1);
                device.ExpoPushToken = "ExponentPushToken[ignoredonupdate]";
                await db.PushDevices.UpdateAsync(device).ConfigureAwait(false);

                PushDevice? read = await db.PushDevices.ReadAsync(device.Id).ConfigureAwait(false);
                AssertEqual("ten_b", read!.TenantId);
                AssertEqual("usr_b", read.UserId);
                AssertEqual(PushPlatformEnum.Android, read.Platform);
                AssertEqual("renamed", read.DeviceName);
                AssertEqual("AskProposal", String.Join(",", read.Categories));
                AssertEqual(token, read.ExpoPushToken, "the token is fixed");
                AssertSameInstant(_Fixed.AddHours(1), read.LastSeenUtc, "last seen");
                AssertSameInstant(_Fixed, read.CreatedUtc, "created is fixed");

                AssertTrue(await db.PushDevices.SetActiveAsync(device.Id, false).ConfigureAwait(false), "deactivated");
                AssertFalse((await db.PushDevices.ReadAsync(device.Id).ConfigureAwait(false))!.Active, "inactive");
                AssertTrue(await db.PushDevices.SetActiveAsync(device.Id, true).ConfigureAwait(false), "reactivated");
                AssertTrue((await db.PushDevices.ReadAsync(device.Id).ConfigureAwait(false))!.Active, "active");
                AssertFalse(await db.PushDevices.SetActiveAsync("pdv_missing", false).ConfigureAwait(false), "missing id");
            }));

            cases.Add(CaseAsync("enumerate_filters", "EnumerateAsync filters by tenant, user, and active, oldest first", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                PushDevice a1 = await db.PushDevices.CreateAsync(NewDevice("ten_e1", "usr_e1", _Fixed)).ConfigureAwait(false);
                PushDevice a2 = await db.PushDevices.CreateAsync(NewDevice("ten_e1", "usr_e1", _Fixed.AddMinutes(1))).ConfigureAwait(false);
                PushDevice b1 = await db.PushDevices.CreateAsync(NewDevice("ten_e1", "usr_e2", _Fixed.AddMinutes(2))).ConfigureAwait(false);
                PushDevice c1 = await db.PushDevices.CreateAsync(NewDevice("ten_e2", "usr_e3", _Fixed.AddMinutes(3))).ConfigureAwait(false);
                await db.PushDevices.SetActiveAsync(a2.Id, false).ConfigureAwait(false);

                AssertIds(await db.PushDevices.EnumerateAsync(new PushDeviceQuery { TenantId = "ten_e1" }).ConfigureAwait(false), "tenant", a1.Id, a2.Id, b1.Id);
                AssertIds(await db.PushDevices.EnumerateAsync(new PushDeviceQuery { TenantId = "ten_e1", UserId = "usr_e1" }).ConfigureAwait(false), "user", a1.Id, a2.Id);
                AssertIds(await db.PushDevices.EnumerateAsync(new PushDeviceQuery { TenantId = "ten_e1", ActiveOnly = true }).ConfigureAwait(false), "active", a1.Id, b1.Id);
                AssertIds(await db.PushDevices.EnumerateAsync(new PushDeviceQuery { TenantId = "ten_none" }).ConfigureAwait(false), "unknown tenant");
                List<PushDevice> all = await db.PushDevices.EnumerateAsync(new PushDeviceQuery()).ConfigureAwait(false);
                AssertTrue(all.Any(d => d.Id == c1.Id) && all.Count >= 4, "unfiltered lists every tenant");
            }));

            cases.Add(CaseAsync("deletes", "DeleteAsync, DeleteByUserAsync, and DeleteByTenantAsync remove only their rows", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                PushDevice one = await db.PushDevices.CreateAsync(NewDevice("ten_d1", "usr_d1", _Fixed)).ConfigureAwait(false);
                PushDevice two = await db.PushDevices.CreateAsync(NewDevice("ten_d1", "usr_d1", _Fixed)).ConfigureAwait(false);
                PushDevice other = await db.PushDevices.CreateAsync(NewDevice("ten_d1", "usr_d2", _Fixed)).ConfigureAwait(false);
                PushDevice foreign = await db.PushDevices.CreateAsync(NewDevice("ten_d2", "usr_d1", _Fixed)).ConfigureAwait(false);

                AssertTrue(await db.PushDevices.DeleteAsync(one.Id).ConfigureAwait(false), "deleted by id");
                AssertFalse(await db.PushDevices.DeleteAsync(one.Id).ConfigureAwait(false), "already gone");
                AssertEqual(1, await db.PushDevices.DeleteByUserAsync("ten_d1", "usr_d1").ConfigureAwait(false), "by user");
                AssertNull(await db.PushDevices.ReadAsync(two.Id).ConfigureAwait(false), "user's device deleted");
                AssertNotNull(await db.PushDevices.ReadAsync(foreign.Id).ConfigureAwait(false), "same user id in another tenant kept");
                AssertEqual(1, await db.PushDevices.DeleteByTenantAsync("ten_d1").ConfigureAwait(false), "by tenant");
                AssertNull(await db.PushDevices.ReadAsync(other.Id).ConfigureAwait(false), "tenant's device deleted");
                AssertNotNull(await db.PushDevices.ReadAsync(foreign.Id).ConfigureAwait(false), "other tenant kept");
            }));

            cases.Add(CaseAsync("register_new_owner_gets_new_id", "A token registered by another user or tenant is deleted there and registered under a new id", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushDeviceService service = new PushDeviceService(db, new ArmadaSettings(), Quiet());
                string token = "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";

                PushDeviceRegistration first = await service.RegisterAsync(Auth("ten_o1", "usr_o1"), Register(token)).ConfigureAwait(false);
                PushDeviceRegistration sameUserOtherTenant = await service.RegisterAsync(Auth("ten_o2", "usr_o1"), Register(token)).ConfigureAwait(false);
                AssertTrue(sameUserOtherTenant.Created, "created for the new tenant");
                AssertTrue(sameUserOtherTenant.Device.Id != first.Device.Id, "new id for another tenant");
                AssertNull(await db.PushDevices.ReadAsync(first.Device.Id).ConfigureAwait(false), "first row deleted");

                PushDeviceRegistration otherUser = await service.RegisterAsync(Auth("ten_o2", "usr_o2"), Register(token)).ConfigureAwait(false);
                AssertTrue(otherUser.Device.Id != sameUserOtherTenant.Device.Id && otherUser.Device.Id != first.Device.Id, "new id for another user");
                AssertNull(await db.PushDevices.ReadAsync(sameUserOtherTenant.Device.Id).ConfigureAwait(false), "second row deleted");
                PushDevice? byToken = await db.PushDevices.ReadByTokenAsync(token).ConfigureAwait(false);
                AssertEqual(otherUser.Device.Id, byToken!.Id, "the token names the newest owner's device");
                AssertEqual("usr_o2", byToken.UserId);

                PushDeviceRegistration refreshed = await service.RegisterAsync(Auth("ten_o2", "usr_o2"), Register(token)).ConfigureAwait(false);
                AssertFalse(refreshed.Created, "the same owner refreshes");
                AssertEqual(otherUser.Device.Id, refreshed.Device.Id, "same id for the same owner");
            }));

            cases.Add(CaseAsync("register_caps_active_devices_per_user", "Registering or reactivating beyond MaxDevicesPerUser deactivates the least recently seen devices of that user only", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ArmadaSettings settings = new ArmadaSettings();
                settings.Push.MaxDevicesPerUser = 2;
                PushDeviceService service = new PushDeviceService(db, settings, Quiet());

                PushDevice oldest = NewDevice("ten_c", "usr_c", _Fixed);
                PushDevice middle = NewDevice("ten_c", "usr_c", _Fixed.AddMinutes(1));
                PushDevice otherUser = NewDevice("ten_c", "usr_other", _Fixed);
                PushDevice otherTenant = NewDevice("ten_c2", "usr_c", _Fixed);
                foreach (PushDevice d in new[] { oldest, middle, otherUser, otherTenant }) await db.PushDevices.CreateAsync(d).ConfigureAwait(false);

                PushDeviceRegistration third = await service.RegisterAsync(Auth("ten_c", "usr_c"), Register("ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]")).ConfigureAwait(false);
                AssertFalse((await db.PushDevices.ReadAsync(oldest.Id).ConfigureAwait(false))!.Active, "least recently seen deactivated");
                AssertTrue((await db.PushDevices.ReadAsync(middle.Id).ConfigureAwait(false))!.Active, "next one kept");
                AssertTrue((await db.PushDevices.ReadAsync(third.Device.Id).ConfigureAwait(false))!.Active, "new one active");
                AssertTrue((await db.PushDevices.ReadAsync(otherUser.Id).ConfigureAwait(false))!.Active, "another user is not affected");
                AssertTrue((await db.PushDevices.ReadAsync(otherTenant.Id).ConfigureAwait(false))!.Active, "the same user id in another tenant is not affected");

                PushDeviceRegistration back = await service.RegisterAsync(Auth("ten_c", "usr_c"), Register(oldest.ExpoPushToken)).ConfigureAwait(false);
                AssertEqual(oldest.Id, back.Device.Id, "reactivated in place");
                AssertTrue(back.Device.Active, "reactivated");
                AssertFalse((await db.PushDevices.ReadAsync(middle.Id).ConfigureAwait(false))!.Active, "reactivating evicts the now least recently seen");
                List<PushDevice> active = await db.PushDevices.EnumerateAsync(new PushDeviceQuery { TenantId = "ten_c", UserId = "usr_c", ActiveOnly = true }).ConfigureAwait(false);
                AssertEqual(2, active.Count, "never more than the cap");

                PushDeviceRegistration refresh = await service.RegisterAsync(Auth("ten_c", "usr_c"), Register(oldest.ExpoPushToken)).ConfigureAwait(false);
                AssertTrue((await db.PushDevices.ReadAsync(third.Device.Id).ConfigureAwait(false))!.Active, "refreshing an active device evicts nothing");
                AssertTrue(refresh.Device.Active, "still active");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Database: push devices",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static PushDevice NewDevice(string tenantId, string userId, DateTime created)
        {
            PushDevice device = new PushDevice();
            device.TenantId = tenantId;
            device.UserId = userId;
            device.ExpoPushToken = "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";
            device.Categories = new List<PushCategoryEnum> { PushCategoryEnum.MissionFailed };
            device.CreatedUtc = created;
            device.LastSeenUtc = created;
            return device;
        }

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static AuthContext Auth(string tenantId, string userId)
        {
            return AuthContext.Authenticated(tenantId, userId, false, false, "Test");
        }

        private static PushDeviceRegisterRequest Register(string token)
        {
            PushDeviceRegisterRequest request = new PushDeviceRegisterRequest();
            request.Platform = PushPlatformEnum.Ios;
            request.ExpoPushToken = token;
            return request;
        }

        private static void AssertIds(List<PushDevice> devices, string label, params string[] expected)
        {
            AssertEqual(String.Join(",", expected), String.Join(",", devices.Select(d => d.Id)), label);
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
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
}
