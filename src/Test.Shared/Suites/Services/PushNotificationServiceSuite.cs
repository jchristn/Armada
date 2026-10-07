namespace Test.Shared.Suites.Services
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
    /// Push notifications: device registration (idempotent upsert, re-own, validation, scoping), recipient selection per
    /// kind (owner, approvers, tenant admins; other tenants and inactive users excluded), payload content rules
    /// (redaction, single line, truncation, per-recipient deep link and actionable category), batching of 100, ticket and
    /// receipt DeviceNotRegistered deactivation, retries of transient failures, rate limit and deduplication, the
    /// disabled setting, the asynchronous hooks (state transitions, never throwing into the caller), badges, and the
    /// test push. Every case uses <see cref="RecordingPushTransport"/>; the Expo Push Service is never called.
    /// </summary>
    public sealed class PushNotificationServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PushNotifications";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("register_idempotent_upsert", "Registering a token twice refreshes one device; categories default from settings and are kept on refresh", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.Push.Categories = new List<PushCategoryEnum> { PushCategoryEnum.AskProposal, PushCategoryEnum.CliPermission };
                PushDeviceService service = new PushDeviceService(testDb.Driver, settings, Quiet());
                AuthContext user = Auth("ten_r", "usr_r", false, false);
                string token = NewToken();

                PushDeviceRegistration first = await service.RegisterAsync(user, Register(token, "Phone")).ConfigureAwait(false);
                AssertTrue(first.Created, "created");
                AssertEqual("AskProposal,CliPermission", String.Join(",", first.Device.Categories), "defaults from settings");
                AssertTrue(first.Device.ExpoPushToken != token && first.Device.ExpoPushToken.StartsWith("ExponentPushToken[****", StringComparison.Ordinal), "masked: " + first.Device.ExpoPushToken);

                await service.UpdateAsync(user, first.Device.Id, new PushDeviceUpdateRequest { Categories = new List<PushCategoryEnum> { PushCategoryEnum.VoyageFinished } }).ConfigureAwait(false);
                await testDb.Driver.PushDevices.SetActiveAsync(first.Device.Id, false).ConfigureAwait(false);

                PushDeviceRegistration second = await service.RegisterAsync(user, Register(token, null)).ConfigureAwait(false);
                AssertFalse(second.Created, "refreshed, not created");
                AssertEqual(first.Device.Id, second.Device.Id, "same device");
                AssertEqual("VoyageFinished", String.Join(",", second.Device.Categories), "categories kept when omitted");
                AssertEqual("Phone", second.Device.DeviceName, "name kept when omitted");
                AssertTrue(second.Device.Active, "reactivated");
                AssertEqual(1, (await testDb.Driver.PushDevices.EnumerateAsync(new PushDeviceQuery()).ConfigureAwait(false)).Count, "one row");
            }));

            cases.Add(CaseAsync("register_reowns_token", "A token registered by another user moves to the caller with fresh categories and fields", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                PushDeviceService service = new PushDeviceService(testDb.Driver, settings, Quiet());
                string token = NewToken();
                PushDeviceRegisterRequest firstRequest = Register(token, "Old owner phone");
                firstRequest.Categories = new List<PushCategoryEnum> { PushCategoryEnum.MissionFailed };
                PushDeviceRegistration first = await service.RegisterAsync(Auth("ten_a", "usr_a", false, false), firstRequest).ConfigureAwait(false);

                PushDeviceRegistration moved = await service.RegisterAsync(Auth("ten_b", "usr_b", false, false), Register(token, null)).ConfigureAwait(false);
                AssertFalse(moved.Created, "the row is reused");
                AssertEqual(first.Device.Id, moved.Device.Id, "same id");
                AssertEqual("ten_b", moved.Device.TenantId);
                AssertEqual("usr_b", moved.Device.UserId);
                AssertNull(moved.Device.DeviceName, "the previous owner's name is not carried over");
                AssertEqual(PushSettings.AllCategories().Count, moved.Device.Categories.Count, "categories reset to the defaults");
                List<PushDevice> oldOwner = await service.ListAsync(Auth("ten_a", "usr_a", false, false), null, null).ConfigureAwait(false);
                AssertEqual(0, oldOwner.Count, "the previous owner no longer has the device");
            }));

            cases.Add(CaseAsync("register_validation", "Invalid tokens, a missing platform, long fields, and captain sessions are refused", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                PushDeviceService service = new PushDeviceService(testDb.Driver, new ArmadaSettings(), Quiet());
                AuthContext user = Auth("ten_v", "usr_v", false, false);
                await AssertThrowsAsync<ArgumentException>(() => service.RegisterAsync(user, Register("not-a-token", null)), "bad token").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.RegisterAsync(user, Register("ExponentPushToken[abc def]", null)), "token with a space").ConfigureAwait(false);
                PushDeviceRegisterRequest noPlatform = Register(NewToken(), null);
                noPlatform.Platform = null;
                await AssertThrowsAsync<ArgumentException>(() => service.RegisterAsync(user, noPlatform), "platform required").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.RegisterAsync(user, Register(NewToken(), new string('x', 129))), "name too long").ConfigureAwait(false);

                AuthContext captain = Auth("ten_v", "usr_v", false, false);
                captain.MissionId = "msn_x";
                await AssertThrowsAsync<UnauthorizedAccessException>(() => service.RegisterAsync(captain, Register(NewToken(), null)), "captain session").ConfigureAwait(false);
                AssertTrue(PushDeviceService.IsValidToken("ExpoPushToken[abcDEF123_-]"), "ExpoPushToken form accepted");
            }));

            cases.Add(CaseAsync("device_scoping", "Owners see their own devices; tenant admins their tenant; other tenants get not found", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                PushDeviceService service = new PushDeviceService(testDb.Driver, new ArmadaSettings(), Quiet());
                AuthContext alice = Auth("ten_s1", "usr_alice", false, false);
                AuthContext bob = Auth("ten_s1", "usr_bob", false, false);
                AuthContext admin = Auth("ten_s1", "usr_admin", false, true);
                AuthContext foreignAdmin = Auth("ten_s2", "usr_fadmin", false, true);
                AuthContext global = Auth("ten_s2", "usr_global", true, false);

                PushDevice aliceDevice = (await service.RegisterAsync(alice, Register(NewToken(), "alice")).ConfigureAwait(false)).Device;
                await service.RegisterAsync(bob, Register(NewToken(), "bob")).ConfigureAwait(false);

                AssertEqual("alice", String.Join(",", (await service.ListAsync(alice, null, null).ConfigureAwait(false)).Select(d => d.DeviceName)), "own devices only");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(bob, "usr_alice", null), "a user cannot list another user").ConfigureAwait(false);
                AssertEqual("alice", String.Join(",", (await service.ListAsync(admin, "usr_alice", null).ConfigureAwait(false)).Select(d => d.DeviceName)), "tenant admin lists a user");
                AssertEqual(0, (await service.ListAsync(foreignAdmin, "usr_alice", null).ConfigureAwait(false)).Count, "other tenant's admin sees nothing");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(foreignAdmin, "usr_alice", "ten_s1"), "tenant admin cannot name another tenant").ConfigureAwait(false);
                AssertEqual("alice", String.Join(",", (await service.ListAsync(global, "usr_alice", null).ConfigureAwait(false)).Select(d => d.DeviceName)), "global admin lists any user");

                await AssertThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(bob, aliceDevice.Id, new PushDeviceUpdateRequest { DeviceName = "x" }), "other user cannot update").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(foreignAdmin, aliceDevice.Id), "other tenant cannot delete").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => service.ReadManagedAsync(foreignAdmin, aliceDevice.Id), "other tenant cannot read").ConfigureAwait(false);
                PushDevice renamed = await service.UpdateAsync(admin, aliceDevice.Id, new PushDeviceUpdateRequest { DeviceName = "renamed" }).ConfigureAwait(false);
                AssertEqual("renamed", renamed.DeviceName, "tenant admin updates");
                await service.DeleteAsync(alice, aliceDevice.Id).ConfigureAwait(false);
                AssertNull(await testDb.Driver.PushDevices.ReadAsync(aliceDevice.Id).ConfigureAwait(false), "owner deleted it");
            }));

            cases.Add(CaseAsync("recipients_per_kind", "Recipients: proposals to the owner; approvals to owner and tenant admins; failures to the owner (admins without one); other tenants and inactive users never", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                PushNotificationService push = new PushNotificationService(db, settings, Quiet(), new RecordingPushTransport());

                AskActionProposal proposal = new AskActionProposal { TenantId = org.TenantId, UserId = org.Owner, ThreadId = "ath_r", ToolName = "dispatch", SummaryText = "Dispatch a mission" };
                List<PushRecipient> ask = await push.ResolveRecipientsAsync(PushNotificationService.FromAskProposal(proposal)).ConfigureAwait(false);
                AssertEqual(org.Owner, String.Join(",", ask.Select(r => r.UserId)), "proposal: owner only");
                AssertTrue(ask[0].CanAct, "the owner decides proposals");

                CliPermissionRequest request = new CliPermissionRequest { TenantId = org.TenantId, UserId = org.Owner, ToolName = "Bash", SummaryText = "git status", ExpiresUtc = DateTime.UtcNow.AddMinutes(10) };
                List<PushRecipient> cli = await push.ResolveRecipientsAsync(PushNotificationService.FromCliPermission(request)).ConfigureAwait(false);
                AssertSet(new[] { org.Owner, org.TenantAdmin, org.GlobalAdmin }, cli, "cli permission: owner and the tenant's admins");
                AssertFalse(cli.Single(r => r.UserId == org.Owner).CanAct, "the owner cannot decide by default");
                AssertTrue(cli.Single(r => r.UserId == org.TenantAdmin).CanAct, "tenant admin decides");
                settings.Permissions.AllowOwnerApproval = true;
                cli = await push.ResolveRecipientsAsync(PushNotificationService.FromCliPermission(request)).ConfigureAwait(false);
                AssertTrue(cli.Single(r => r.UserId == org.Owner).CanAct, "owner decides with AllowOwnerApproval");

                Mission review = new Mission { TenantId = org.TenantId, UserId = org.Owner, Title = "Review me", Status = MissionStatusEnum.Review };
                AssertSet(new[] { org.Owner, org.TenantAdmin, org.GlobalAdmin }, await push.ResolveRecipientsAsync(PushNotificationService.FromMission(review)!).ConfigureAwait(false), "review: owner and admins");
                Deployment deployment = new Deployment { TenantId = org.TenantId, UserId = org.Owner, Status = DeploymentStatusEnum.PendingApproval };
                AssertSet(new[] { org.Owner, org.TenantAdmin, org.GlobalAdmin }, await push.ResolveRecipientsAsync(PushNotificationService.FromDeployment(deployment)!).ConfigureAwait(false), "deployment approval: owner and admins");

                Mission failed = new Mission { TenantId = org.TenantId, UserId = org.Owner, Title = "Broken", Status = MissionStatusEnum.Failed };
                AssertSet(new[] { org.Owner }, await push.ResolveRecipientsAsync(PushNotificationService.FromMission(failed)!).ConfigureAwait(false), "failure: owner only");
                failed.UserId = null;
                AssertSet(new[] { org.TenantAdmin, org.GlobalAdmin }, await push.ResolveRecipientsAsync(PushNotificationService.FromMission(failed)!).ConfigureAwait(false), "failure without owner: tenant admins");
                failed.UserId = org.Inactive;
                AssertSet(new[] { org.TenantAdmin, org.GlobalAdmin }, await push.ResolveRecipientsAsync(PushNotificationService.FromMission(failed)!).ConfigureAwait(false), "inactive owner: tenant admins");
                failed.UserId = org.ForeignUser;
                AssertSet(new[] { org.TenantAdmin, org.GlobalAdmin }, await push.ResolveRecipientsAsync(PushNotificationService.FromMission(failed)!).ConfigureAwait(false), "an owner id from another tenant is not trusted");

                Captain captain = new Captain { TenantId = org.TenantId, UserId = org.Owner, Name = "cpt", State = CaptainStateEnum.Stalled };
                AssertSet(new[] { org.Owner }, await push.ResolveRecipientsAsync(PushNotificationService.FromCaptain(captain)!).ConfigureAwait(false), "stalled captain: owner");
                Voyage voyage = new Voyage { TenantId = org.TenantId, UserId = org.Owner, Title = "v", Status = VoyageStatusEnum.Complete };
                AssertSet(new[] { org.Owner }, await push.ResolveRecipientsAsync(PushNotificationService.FromVoyage(voyage)!).ConfigureAwait(false), "voyage finished: owner");

                Mission noTenant = new Mission { TenantId = null, UserId = org.Owner, Title = "x", Status = MissionStatusEnum.Failed };
                AssertEqual(0, (await push.ResolveRecipientsAsync(PushNotificationService.FromMission(noTenant)!).ConfigureAwait(false)).Count, "no tenant, nobody");
                AssertNull(PushNotificationService.FromMission(new Mission { Status = MissionStatusEnum.InProgress }), "in-progress missions are not announced");
                AssertNull(PushNotificationService.FromVoyage(new Voyage { Status = VoyageStatusEnum.Cancelled }), "cancelled voyages are not announced");
            }));

            cases.Add(CaseAsync("payload_content_rules", "Payloads carry no secrets, no line breaks, truncated text, the deep link, kind, entity, category, and the actionable category only for deciders", async () =>
            {
                string secretSummary = "curl -H 'Authorization: Bearer abcdefghijklmnop1234' https://example.com/x\nthen api_key=supersecretvalue99 and " + new string('z', 300);
                CliPermissionRequest request = new CliPermissionRequest { TenantId = "ten_p", UserId = "usr_owner", ThreadId = "ath_p", ToolName = "Bash", SummaryText = secretSummary, CaptainName = "Builder" };
                PushOccurrence occurrence = PushNotificationService.FromCliPermission(request);
                PushDevice device = new PushDevice { ExpoPushToken = NewToken(), TenantId = "ten_p", UserId = "usr_owner" };

                PushMessage ownerMessage = PushNotificationService.BuildMessage(occurrence, new PushRecipient { TenantId = "ten_p", UserId = "usr_owner", IsOwner = true, CanAct = false }, device, 3);
                AssertTrue(ownerMessage.Body.Length <= PushContentFormatter.MaxBodyLength, "body bounded: " + ownerMessage.Body.Length);
                AssertTrue(ownerMessage.Title.Length <= PushContentFormatter.MaxTitleLength, "title bounded");
                AssertFalse(ownerMessage.Body.Contains("abcdefghijklmnop1234"), "bearer token redacted: " + ownerMessage.Body);
                AssertFalse(ownerMessage.Body.Contains("supersecretvalue99"), "api key redacted");
                AssertFalse(ownerMessage.Body.Contains("\n") || ownerMessage.Body.Contains("\r"), "single line");
                AssertFalse(ownerMessage.Body.Contains("zzzzzzzzzz"), "summary truncated well before the long tail");
                AssertTrue(ownerMessage.Body.Contains("..."), "truncation marker");
                AssertTrue(ownerMessage.Body.StartsWith("Captain \"Builder\" wants to use Bash", StringComparison.Ordinal), ownerMessage.Body);
                AssertEqual("/ask/ath_p", ownerMessage.Data.Url, "owner opens the thread");
                AssertEqual(InboxItemKinds.CliPermission, ownerMessage.Data.Kind);
                AssertEqual(request.Id, ownerMessage.Data.EntityId);
                AssertEqual("CliPermission", ownerMessage.Data.Category);
                AssertEqual("ath_p", ownerMessage.Data.ThreadId, "owner gets the thread");
                AssertEqual(device.Id, ownerMessage.Data.DeviceId, "the message names the device it was built for");
                AssertNull(ownerMessage.CategoryId, "no actions for a recipient who cannot decide");
                AssertEqual(3, ownerMessage.Badge, "badge");
                AssertEqual("default", ownerMessage.Sound, "sound");
                AssertEqual("high", ownerMessage.Priority, "approvals are high priority");

                PushMessage adminMessage = PushNotificationService.BuildMessage(occurrence, new PushRecipient { TenantId = "ten_p", UserId = "usr_admin", IsTenantAdmin = true, CanAct = true }, device, null);
                AssertEqual("/cli-permissions?request=" + request.Id, adminMessage.Data.Url, "approver opens the permissions page");
                AssertNull(adminMessage.Data.ThreadId, "approver does not get the owner's thread");
                AssertEqual(PushNotificationKinds.ApproveDenyCategoryId, adminMessage.CategoryId, "decider gets approve and deny actions");
                AssertNull(adminMessage.Badge, "no badge when unknown");

                Mission mission = new Mission { TenantId = "ten_p", UserId = "usr_owner", Title = "Fix password=hunter2hunter2 handling\r\nin login" + new string('y', 200), Status = MissionStatusEnum.Failed };
                mission.FailureReason = "stack trace with secret=abcdef123456";
                PushMessage failed = PushNotificationService.BuildMessage(PushNotificationService.FromMission(mission)!, new PushRecipient { UserId = "usr_owner", IsOwner = true, CanAct = true }, device, null);
                AssertFalse(failed.Body.Contains("hunter2hunter2"), "secret in a title redacted: " + failed.Body);
                AssertFalse(failed.Body.Contains("stack trace"), "failure reasons are never sent");
                AssertEqual("/missions/" + mission.Id, failed.Data.Url);
                AssertNull(failed.CategoryId, "failures are not actionable even for someone who could act");
                AssertEqual("default", failed.Priority);

                string json = System.Text.Json.JsonSerializer.Serialize(failed);
                AssertTrue(json.Contains("\"to\":") && json.Contains("\"data\":{\"url\":") && json.Contains("\"entityId\":"), "Expo wire names: " + json);
                AssertFalse(json.Contains("\"badge\"") || json.Contains("\"categoryId\"") || json.Contains("\"threadId\""), "null optional fields are omitted: " + json);
            }));

            cases.Add(CaseAsync("batches_of_100_and_ticket_deactivation", "250 devices go out in batches of 100, 100, 50; a DeviceNotRegistered ticket deactivates its device", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                RecordingPushTransport transport = new RecordingPushTransport();
                ArmadaSettings settings = new ArmadaSettings();
                settings.Push.ExpoAccessToken = "expo-access-token-value";
                PushNotificationService push = new PushNotificationService(db, settings, Quiet(), transport);

                List<PushDevice> devices = new List<PushDevice>();
                for (int i = 0; i < 250; i++) devices.Add(await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false));
                transport.NotRegisteredTokens.Add(devices[7].ExpoPushToken);

                int accepted = await push.DeliverAsync(Failed(org)).ConfigureAwait(false);
                AssertEqual("100,100,50", String.Join(",", transport.Batches.Select(b => b.Count)), "batch sizes");
                AssertEqual(249, accepted, "accepted");
                AssertTrue(transport.AccessTokens.All(t => t == "expo-access-token-value"), "access token passed");
                AssertFalse((await db.PushDevices.ReadAsync(devices[7].Id).ConfigureAwait(false))!.Active, "not registered device deactivated");
                AssertTrue((await db.PushDevices.ReadAsync(devices[8].Id).ConfigureAwait(false))!.Active, "others stay active");
                AssertEqual(249, push.PendingReceiptCount, "tickets await receipts");
            }));

            cases.Add(CaseAsync("receipts_deactivate_not_registered", "Receipts are fetched once due; DeviceNotRegistered receipts deactivate devices and clear the pending tickets", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                RecordingPushTransport transport = new RecordingPushTransport();
                SettableTimeProvider clock = new SettableTimeProvider();
                PushNotificationService push = new PushNotificationService(db, new ArmadaSettings(), Quiet(), transport, clock);
                PushDevice live = await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);
                PushDevice dead = await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);
                transport.NotRegisteredReceiptTokens.Add(dead.ExpoPushToken);

                AssertEqual(2, await push.DeliverAsync(Failed(org)).ConfigureAwait(false), "both accepted");
                AssertEqual(0, await push.CheckReceiptsAsync().ConfigureAwait(false), "not due yet");
                AssertEqual(0, transport.ReceiptRequests.Count, "no receipt request before the delay");

                clock.Advance(push.ReceiptDelay + TimeSpan.FromSeconds(1));
                AssertEqual(1, await push.CheckReceiptsAsync().ConfigureAwait(false), "one device deactivated");
                AssertEqual(2, transport.ReceiptRequests.Single().Count, "both tickets in one request");
                AssertFalse((await db.PushDevices.ReadAsync(dead.Id).ConfigureAwait(false))!.Active, "dead device inactive");
                AssertTrue((await db.PushDevices.ReadAsync(live.Id).ConfigureAwait(false))!.Active, "live device active");
                AssertEqual(0, push.PendingReceiptCount, "pending tickets cleared");

                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_second")).ConfigureAwait(false) - 1, "only the live device is targeted now");
            }));

            cases.Add(CaseAsync("transient_retry_and_permanent_failure", "Transient failures are retried with backoff; a permanent failure is logged and nothing throws", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                RecordingPushTransport transport = new RecordingPushTransport();
                PushNotificationService push = new PushNotificationService(db, new ArmadaSettings(), Quiet(), transport);
                push.RetryBaseDelay = TimeSpan.Zero;
                await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);

                transport.FailNextSends = 2;
                AssertEqual(1, await push.DeliverAsync(Failed(org, "msn_t1")).ConfigureAwait(false), "delivered after retries");
                AssertEqual(3, transport.SendCalls, "two failures then success");

                transport.FailNextSends = 10;
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_t2")).ConfigureAwait(false), "gave up");
                AssertEqual(3 + push.MaxAttempts, transport.SendCalls, "MaxAttempts tries");

                transport.FailNextSends = 1;
                transport.FailTransient = false;
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_t3")).ConfigureAwait(false), "permanent failure not retried");
                AssertEqual(3 + push.MaxAttempts + 1, transport.SendCalls, "one try");
            }));

            cases.Add(CaseAsync("rate_limit_and_dedupe", "A repeat about the same item is suppressed inside the dedupe window; the per-user rate limit drops excess pushes until the minute passes", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                RecordingPushTransport transport = new RecordingPushTransport();
                SettableTimeProvider clock = new SettableTimeProvider();
                ArmadaSettings settings = new ArmadaSettings();
                settings.Push.DedupeWindowSeconds = 300;
                settings.Push.MaxPerUserPerMinute = 2;
                PushNotificationService push = new PushNotificationService(db, settings, Quiet(), transport, clock);
                await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);

                AssertEqual(1, await push.DeliverAsync(Failed(org, "msn_d1")).ConfigureAwait(false), "first");
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_d1")).ConfigureAwait(false), "duplicate suppressed");
                AssertEqual(1, await push.DeliverAsync(Failed(org, "msn_d2")).ConfigureAwait(false), "second item");
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_d3")).ConfigureAwait(false), "rate limited");
                clock.Advance(TimeSpan.FromSeconds(61));
                AssertEqual(1, await push.DeliverAsync(Failed(org, "msn_d3")).ConfigureAwait(false), "allowed after a minute");
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_d1")).ConfigureAwait(false), "still inside the dedupe window");
                clock.Advance(TimeSpan.FromSeconds(300));
                AssertEqual(1, await push.DeliverAsync(Failed(org, "msn_d1")).ConfigureAwait(false), "after the dedupe window");
                AssertEqual(4, transport.Messages.Count, "messages sent");
            }));

            cases.Add(CaseAsync("categories_disabled_and_no_devices", "Muted categories, the disabled setting, and the absence of devices send nothing", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                RecordingPushTransport transport = new RecordingPushTransport();
                ArmadaSettings settings = new ArmadaSettings();
                PushNotificationService push = new PushNotificationService(db, settings, Quiet(), transport);

                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_n0")).ConfigureAwait(false), "no devices");
                PushDevice device = await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);
                device.Categories = new List<PushCategoryEnum> { PushCategoryEnum.AskProposal };
                await db.PushDevices.UpdateAsync(device).ConfigureAwait(false);
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_n1")).ConfigureAwait(false), "category muted");

                await AddDeviceAsync(db, org.TenantId, org.TenantAdmin).ConfigureAwait(false);
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_n2")).ConfigureAwait(false), "a failure with an owner does not reach the admin");

                await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);
                settings.Push.Enabled = false;
                AssertEqual(0, await push.DeliverAsync(Failed(org, "msn_n3")).ConfigureAwait(false), "disabled");
                AssertFalse(push.Enqueue(Failed(org, "msn_n4")), "disabled: nothing queued");
                AssertEqual(0, transport.SendCalls, "transport never called");
                settings.Push.Enabled = true;
                AssertEqual(1, await push.DeliverAsync(Failed(org, "msn_n5")).ConfigureAwait(false), "enabled again");
            }));

            cases.Add(CaseAsync("hooks_are_async_and_announce_transitions_once", "Hooks enqueue without blocking; a status is announced once per entry; a failing transport never throws into the caller", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                RecordingPushTransport transport = new RecordingPushTransport();
                ArmadaSettings settings = new ArmadaSettings();
                settings.Push.DedupeWindowSeconds = 0;
                using PushNotificationService push = new PushNotificationService(db, settings, Quiet(), transport);
                push.RetryBaseDelay = TimeSpan.Zero;
                push.Start();
                await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);

                Mission mission = new Mission { TenantId = org.TenantId, UserId = org.Owner, Title = "Hooked", Status = MissionStatusEnum.Failed };
                push.OnMissionChanged(mission);
                push.OnMissionChanged(mission);
                await push.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(1, transport.Messages.Count, "announced once while it stays failed");

                mission.Status = MissionStatusEnum.InProgress;
                push.OnMissionChanged(mission);
                mission.Status = MissionStatusEnum.Failed;
                push.OnMissionChanged(mission);
                await push.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(2, transport.Messages.Count, "announced again after re-entering the status");

                Voyage voyage = new Voyage { TenantId = org.TenantId, UserId = org.Owner, Title = "trip", Status = VoyageStatusEnum.InProgress };
                push.OnVoyageChanged(voyage, VoyageStatusEnum.Complete);
                await push.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(PushNotificationKinds.VoyageFinished, transport.Messages.Last().Data.Kind, "the status override is honored");

                transport.AlwaysThrow = true;
                mission.Status = MissionStatusEnum.Review;
                push.OnMissionChanged(mission);
                push.OnCaptainChanged(new Captain { TenantId = org.TenantId, UserId = org.Owner, Name = "c", State = CaptainStateEnum.Stalled });
                await push.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(3, transport.Messages.Count, "nothing recorded while the transport fails");
                transport.AlwaysThrow = false;

                push.OnCliPermissionEvent("cli_permission.resolved", new CliPermissionRequest { TenantId = org.TenantId, UserId = org.Owner, Status = CliPermissionRequestStatusEnum.Pending });
                push.OnAskProposalCreated(new AskActionProposal { TenantId = org.TenantId, UserId = org.Owner, ThreadId = "ath_h", ToolName = "x", Status = AskProposalStatusEnum.Approved });
                await push.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(3, transport.Messages.Count, "resolved requests and auto-approved proposals are not announced");

                push.OnAskProposalCreated(new AskActionProposal { TenantId = org.TenantId, UserId = org.Owner, ThreadId = "ath_h", ToolName = "dispatch", SummaryText = "Dispatch", Status = AskProposalStatusEnum.Pending });
                await push.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                PushMessage last = transport.Messages.Last();
                AssertEqual(InboxItemKinds.AskProposal, last.Data.Kind, "pending proposal announced");
                AssertEqual(PushNotificationKinds.ApproveDenyCategoryId, last.CategoryId, "owner can approve or deny from the notification");
            }));

            cases.Add(CaseAsync("badge_counts_pending_approvals", "The badge is the recipient's pending approvals from their inbox", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                PushTestOrg org = await CreateOrgAsync(db).ConfigureAwait(false);
                RecordingPushTransport transport = new RecordingPushTransport();
                PushNotificationService push = new PushNotificationService(db, new ArmadaSettings(), Quiet(), transport);
                await AddDeviceAsync(db, org.TenantId, org.Owner).ConfigureAwait(false);
                AskThread thread = await db.AskThreads.CreateAsync(new AskThread { TenantId = org.TenantId, UserId = org.Owner }).ConfigureAwait(false);
                AskActionProposal proposal = new AskActionProposal { TenantId = org.TenantId, UserId = org.Owner, ThreadId = thread.Id, ToolName = "dispatch", SummaryText = "Dispatch", CreatedUtc = DateTime.UtcNow.AddMinutes(-1) };
                await db.AskActionProposals.CreateAsync(proposal).ConfigureAwait(false);

                await push.DeliverAsync(PushNotificationService.FromAskProposal(proposal)).ConfigureAwait(false);
                AssertEqual(1, transport.Messages.Single().Badge, "one pending approval");
            }));

            cases.Add(CaseAsync("test_push_outcomes", "Test pushes report Sent, Disabled, DeviceInactive, and DeviceNotRegistered (deactivating the device)", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                RecordingPushTransport transport = new RecordingPushTransport();
                ArmadaSettings settings = new ArmadaSettings();
                PushNotificationService push = new PushNotificationService(db, settings, Quiet(), transport);
                PushDevice device = await AddDeviceAsync(db, "ten_x", "usr_x").ConfigureAwait(false);
                device.Categories = new List<PushCategoryEnum>();

                PushTestResult sent = await push.SendTestAsync(device).ConfigureAwait(false);
                AssertEqual(PushTestStatusEnum.Sent, sent.Status, sent.Message ?? "");
                AssertNotNull(sent.TicketId, "ticket id");
                AssertEqual(PushNotificationKinds.Test, transport.Messages.Single().Data.Kind, "test kind (categories ignored)");
                AssertEqual(device.Id, transport.Messages.Single().Data.DeviceId, "test push names the device");

                settings.Push.Enabled = false;
                AssertEqual(PushTestStatusEnum.Disabled, (await push.SendTestAsync(device).ConfigureAwait(false)).Status);
                settings.Push.Enabled = true;

                transport.NotRegisteredTokens.Add(device.ExpoPushToken);
                PushTestResult dead = await push.SendTestAsync(device).ConfigureAwait(false);
                AssertEqual(PushTestStatusEnum.DeviceNotRegistered, dead.Status);
                AssertEqual((PushErrorCodeEnum?)PushErrorCodeEnum.DeviceNotRegistered, dead.Error);
                PushDevice? stored = await db.PushDevices.ReadAsync(device.Id).ConfigureAwait(false);
                AssertFalse(stored!.Active, "deactivated");
                AssertEqual(PushTestStatusEnum.DeviceInactive, (await push.SendTestAsync(stored).ConfigureAwait(false)).Status);

                transport.AlwaysThrow = true;
                PushDevice other = await AddDeviceAsync(db, "ten_x", "usr_x").ConfigureAwait(false);
                AssertEqual(PushTestStatusEnum.Failed, (await push.SendTestAsync(other).ConfigureAwait(false)).Status, "transport failure reported, not thrown");
            }));

            cases.Add(CaseAsync("expo_transport_wire_format", "ExpoPushTransport posts the Expo wire format with the bearer token and maps tickets, receipts, and HTTP failures", async () =>
            {
                StubExpoHandler handler = new StubExpoHandler();
                using System.Net.Http.HttpClient http = new System.Net.Http.HttpClient(handler);
                ExpoPushTransport transport = new ExpoPushTransport(http, "http://expo.invalid/--/api/v2/push");
                PushMessage message = new PushMessage { To = "ExponentPushToken[abcd1234]", Title = "t", Body = "b" };
                message.Data.Url = "/missions/msn_1";

                handler.Next = new StubExpoResponse(200, "{\"data\":[{\"status\":\"ok\",\"id\":\"tk1\"},{\"status\":\"error\",\"message\":\"gone\",\"details\":{\"error\":\"DeviceNotRegistered\"}}]}");
                List<PushTicket> tickets = await transport.SendAsync(new List<PushMessage> { message, message }, "secret-token").ConfigureAwait(false);
                AssertEqual("http://expo.invalid/--/api/v2/push/send", handler.LastUrl);
                AssertEqual("Bearer secret-token", handler.LastAuthorization);
                AssertTrue(handler.LastBody.Contains("\"to\":\"ExponentPushToken[abcd1234]\"") && handler.LastBody.Contains("\"url\":\"/missions/msn_1\""), handler.LastBody);
                AssertTrue(tickets[0].Ok && tickets[0].TicketId == "tk1", "ok ticket");
                AssertEqual((PushErrorCodeEnum?)PushErrorCodeEnum.DeviceNotRegistered, tickets[1].Error, "error ticket");

                handler.Next = new StubExpoResponse(200, "{\"data\":{\"tk1\":{\"status\":\"error\",\"details\":{\"error\":\"MessageRateExceeded\"}},\"tk2\":{\"status\":\"ok\"}}}");
                List<PushReceipt> receipts = await transport.GetReceiptsAsync(new List<string> { "tk1", "tk2" }, null).ConfigureAwait(false);
                AssertEqual("http://expo.invalid/--/api/v2/push/getReceipts", handler.LastUrl);
                AssertNull(handler.LastAuthorization, "no token, no header");
                AssertEqual((PushErrorCodeEnum?)PushErrorCodeEnum.MessageRateExceeded, receipts.Single(r => r.TicketId == "tk1").Error);
                AssertTrue(receipts.Single(r => r.TicketId == "tk2").Ok, "ok receipt");
                AssertEqual(PushErrorCodeEnum.Unknown, ExpoPushTransport.MapError("SomethingNew"), "unknown codes");

                handler.Next = new StubExpoResponse(503, "busy");
                PushTransportException transient = await AssertThrowsAsync<PushTransportException>(() => transport.SendAsync(new List<PushMessage> { message }, null), "503").ConfigureAwait(false);
                AssertTrue(transient.Transient, "503 is transient");
                handler.Next = new StubExpoResponse(429, "slow down");
                AssertTrue((await AssertThrowsAsync<PushTransportException>(() => transport.SendAsync(new List<PushMessage> { message }, null), "429").ConfigureAwait(false)).Transient, "429 is transient");
                handler.Next = new StubExpoResponse(400, "{\"errors\":[{\"code\":\"VALIDATION_ERROR\",\"message\":\"bad\"}]}");
                AssertFalse((await AssertThrowsAsync<PushTransportException>(() => transport.SendAsync(new List<PushMessage> { message }, null), "400").ConfigureAwait(false)).Transient, "400 is permanent");
                handler.ThrowNext = true;
                AssertTrue((await AssertThrowsAsync<PushTransportException>(() => transport.SendAsync(new List<PushMessage> { message }, null), "connection").ConfigureAwait(false)).Transient, "connection failure is transient");

                List<PushMessage> tooMany = Enumerable.Range(0, 101).Select(i => message).ToList();
                await AssertThrowsAsync<ArgumentException>(() => transport.SendAsync(tooMany, null), "more than 100").ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Push notifications",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static AuthContext Auth(string tenantId, string userId, bool isAdmin, bool isTenantAdmin)
        {
            return AuthContext.Authenticated(tenantId, userId, isAdmin, isTenantAdmin, "Test");
        }

        private static string NewToken()
        {
            return "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";
        }

        private static PushDeviceRegisterRequest Register(string token, string? name)
        {
            return new PushDeviceRegisterRequest { Platform = PushPlatformEnum.Ios, ExpoPushToken = token, DeviceName = name, AppVersion = "1.0.0", Locale = "en-US" };
        }

        private static async Task<PushDevice> AddDeviceAsync(DatabaseDriver db, string tenantId, string userId)
        {
            PushDevice device = new PushDevice { TenantId = tenantId, UserId = userId, ExpoPushToken = NewToken(), Categories = PushSettings.AllCategories() };
            return await db.PushDevices.CreateAsync(device).ConfigureAwait(false);
        }

        private static PushOccurrence Failed(PushTestOrg org, string missionId = "msn_failed")
        {
            Mission mission = new Mission { Id = missionId, TenantId = org.TenantId, UserId = org.Owner, Title = "Broken build", Status = MissionStatusEnum.Failed };
            return PushNotificationService.FromMission(mission)!;
        }

        private static async Task<PushTestOrg> CreateOrgAsync(DatabaseDriver db)
        {
            TenantMetadata tenant = new TenantMetadata("Push " + Guid.NewGuid().ToString("N").Substring(0, 8));
            await db.Tenants.CreateAsync(tenant).ConfigureAwait(false);
            TenantMetadata foreign = new TenantMetadata("Push other " + Guid.NewGuid().ToString("N").Substring(0, 8));
            await db.Tenants.CreateAsync(foreign).ConfigureAwait(false);

            PushTestOrg org = new PushTestOrg();
            org.TenantId = tenant.Id;
            org.Owner = await AddUserAsync(db, tenant.Id, "owner", false, false, true).ConfigureAwait(false);
            org.TenantAdmin = await AddUserAsync(db, tenant.Id, "tadmin", false, true, true).ConfigureAwait(false);
            org.GlobalAdmin = await AddUserAsync(db, tenant.Id, "gadmin", true, false, true).ConfigureAwait(false);
            org.Bystander = await AddUserAsync(db, tenant.Id, "bystander", false, false, true).ConfigureAwait(false);
            org.Inactive = await AddUserAsync(db, tenant.Id, "inactive", false, true, false).ConfigureAwait(false);
            org.ForeignUser = await AddUserAsync(db, foreign.Id, "foreign", false, false, true).ConfigureAwait(false);
            await AddUserAsync(db, foreign.Id, "fadmin", true, true, true).ConfigureAwait(false);
            return org;
        }

        private static async Task<string> AddUserAsync(DatabaseDriver db, string tenantId, string label, bool isAdmin, bool isTenantAdmin, bool active)
        {
            UserMaster user = new UserMaster(tenantId, label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@push.test", "password");
            user.IsAdmin = isAdmin;
            user.IsTenantAdmin = isTenantAdmin;
            user.Active = active;
            await db.Users.CreateAsync(user).ConfigureAwait(false);
            return user.Id;
        }

        private static void AssertSet(string[] expected, List<PushRecipient> actual, string label)
        {
            string e = String.Join(",", expected.OrderBy(x => x, StringComparer.Ordinal));
            string a = String.Join(",", actual.Select(r => r.UserId).OrderBy(x => x, StringComparer.Ordinal));
            AssertEqual(e, a, label);
        }

        private static async Task<T> AssertThrowsAsync<T>(Func<Task> action, string label) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new AssertionException(label + ": expected " + typeof(T).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
            }

            throw new AssertionException(label + ": expected " + typeof(T).Name + " but nothing was thrown");
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
