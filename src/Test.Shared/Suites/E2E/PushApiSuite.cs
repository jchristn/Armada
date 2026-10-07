namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Push;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end push device routes and delivery through the real server (with the recording push transport, never the
    /// Expo Push Service): register (201) and refresh (200) by token, masked tokens, update, delete, the test push,
    /// re-owning a token across tenants, user and tenant scoping (404 across tenants, 403 for listing another user),
    /// the Push settings round trip with the access token redacted, and delivery of a failed mission that never
    /// breaks the status transition even when the transport fails.
    /// </summary>
    public sealed class PushApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.Push";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("device_crud_and_idempotent_register", "Register is 201 then 200 for the same token; tokens are masked; update, list, delete", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-crud", false).ConfigureAwait(false);
                using HttpClient u = user.CreateClient(fx.BaseUrl);
                string token = NewToken();

                HttpResponseMessage created = await u.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Ios", ExpoPushToken = token, DeviceName = "My iPhone", AppVersion = "1.0.0", Locale = "en-US" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created);
                PushDevice device = await JsonHelper.DeserializeAsync<PushDevice>(created).ConfigureAwait(false);
                AssertTrue(device.Id.StartsWith("pdv_", StringComparison.Ordinal), device.Id);
                AssertEqual(user.UserId, device.UserId);
                AssertEqual(user.TenantId, device.TenantId);
                AssertTrue(device.ExpoPushToken != token && device.ExpoPushToken.Contains("****"), "masked: " + device.ExpoPushToken);
                AssertEqual(PushSettings.AllCategories().Count, device.Categories.Count, "default categories");

                HttpResponseMessage refreshed = await u.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Ios", ExpoPushToken = token })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, refreshed);
                AssertEqual(device.Id, (await JsonHelper.DeserializeAsync<PushDevice>(refreshed).ConfigureAwait(false)).Id, "same device");

                HttpResponseMessage updated = await u.PutAsync("/api/v1/push/devices/" + device.Id, JsonHelper.ToJsonContent(new { DeviceName = "Work phone", Categories = new[] { "CliPermission" } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, updated);
                PushDevice after = await JsonHelper.DeserializeAsync<PushDevice>(updated).ConfigureAwait(false);
                AssertEqual("Work phone", after.DeviceName);
                AssertEqual("CliPermission", String.Join(",", after.Categories));

                List<PushDevice> list = await JsonHelper.DeserializeAsync<List<PushDevice>>(await u.GetAsync("/api/v1/push/devices").ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(device.Id, list.Single().Id, "listed");
                AssertFalse(list.Single().ExpoPushToken.Contains(token.Substring(18, 10)), "list masks the token");

                AssertStatusCode(HttpStatusCode.BadRequest, await u.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Ios", ExpoPushToken = "garbage" })).ConfigureAwait(false), "invalid token");
                AssertStatusCode(HttpStatusCode.Unauthorized, await fx.UnauthClient.GetAsync("/api/v1/push/devices").ConfigureAwait(false), "unauthenticated");

                AssertStatusCode(HttpStatusCode.NoContent, await u.DeleteAsync("/api/v1/push/devices/" + device.Id).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.NotFound, await u.DeleteAsync("/api/v1/push/devices/" + device.Id).ConfigureAwait(false), "already deleted");
            }));

            cases.Add(CaseAsync("cross_tenant_and_user_scoping", "Other tenants get 404 on another tenant's device; users cannot list other users; tenant admins can", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-tadmin", true).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-alice", false, admin.TenantId).ConfigureAwait(false);
                E2ETenantUser bob = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-bob", false, admin.TenantId).ConfigureAwait(false);
                E2ETenantUser foreign = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-foreign", true).ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                using HttpClient b = bob.CreateClient(fx.BaseUrl);
                using HttpClient t = admin.CreateClient(fx.BaseUrl);
                using HttpClient f = foreign.CreateClient(fx.BaseUrl);

                PushDevice device = await JsonHelper.DeserializeAsync<PushDevice>(await a.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Android", ExpoPushToken = NewToken() })).ConfigureAwait(false)).ConfigureAwait(false);

                AssertStatusCode(HttpStatusCode.NotFound, await f.PutAsync("/api/v1/push/devices/" + device.Id, JsonHelper.ToJsonContent(new { DeviceName = "x" })).ConfigureAwait(false), "foreign admin cannot update");
                AssertStatusCode(HttpStatusCode.NotFound, await f.DeleteAsync("/api/v1/push/devices/" + device.Id).ConfigureAwait(false), "foreign admin cannot delete");
                AssertStatusCode(HttpStatusCode.NotFound, await f.PostAsync("/api/v1/push/devices/" + device.Id + "/test", null).ConfigureAwait(false), "foreign admin cannot test");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PutAsync("/api/v1/push/devices/" + device.Id, JsonHelper.ToJsonContent(new { DeviceName = "x" })).ConfigureAwait(false), "another user cannot update");
                AssertStatusCode(HttpStatusCode.Forbidden, await b.GetAsync("/api/v1/push/devices?userId=" + alice.UserId).ConfigureAwait(false), "a user cannot list another user");
                AssertEqual(0, (await JsonHelper.DeserializeAsync<List<PushDevice>>(await b.GetAsync("/api/v1/push/devices").ConfigureAwait(false)).ConfigureAwait(false)).Count, "bob's own list is empty");
                AssertEqual(0, (await JsonHelper.DeserializeAsync<List<PushDevice>>(await f.GetAsync("/api/v1/push/devices?userId=" + alice.UserId).ConfigureAwait(false)).ConfigureAwait(false)).Count, "foreign tenant admin sees nothing");
                AssertStatusCode(HttpStatusCode.Forbidden, await f.GetAsync("/api/v1/push/devices?tenantId=" + admin.TenantId).ConfigureAwait(false), "tenant admin cannot name another tenant");
                List<PushDevice> adminView = await JsonHelper.DeserializeAsync<List<PushDevice>>(await t.GetAsync("/api/v1/push/devices?userId=" + alice.UserId).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(device.Id, adminView.Single().Id, "tenant admin lists alice");
                AssertStatusCode(HttpStatusCode.OK, await t.PutAsync("/api/v1/push/devices/" + device.Id, JsonHelper.ToJsonContent(new { Categories = new string[0] })).ConfigureAwait(false), "tenant admin updates");
            }));

            cases.Add(CaseAsync("reown_token_across_tenants", "A token registered in another tenant moves to the new user; the previous owner loses it", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser first = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-first", false).ConfigureAwait(false);
                E2ETenantUser second = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-second", false).ConfigureAwait(false);
                using HttpClient one = first.CreateClient(fx.BaseUrl);
                using HttpClient two = second.CreateClient(fx.BaseUrl);
                string token = NewToken();

                PushDevice original = await JsonHelper.DeserializeAsync<PushDevice>(await one.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Ios", ExpoPushToken = token, DeviceName = "shared phone" })).ConfigureAwait(false)).ConfigureAwait(false);
                HttpResponseMessage moved = await two.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Ios", ExpoPushToken = token })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, moved, "re-owned rows are refreshed, not created");
                PushDevice device = await JsonHelper.DeserializeAsync<PushDevice>(moved).ConfigureAwait(false);
                AssertEqual(original.Id, device.Id);
                AssertEqual(second.TenantId, device.TenantId);
                AssertEqual(second.UserId, device.UserId);
                AssertNull(device.DeviceName, "previous owner's name dropped");
                AssertEqual(0, (await JsonHelper.DeserializeAsync<List<PushDevice>>(await one.GetAsync("/api/v1/push/devices").ConfigureAwait(false)).ConfigureAwait(false)).Count, "first user lost it");
                AssertStatusCode(HttpStatusCode.NotFound, await one.DeleteAsync("/api/v1/push/devices/" + device.Id).ConfigureAwait(false), "first user can no longer delete it");
            }));

            cases.Add(CaseAsync("settings_roundtrip_redaction_and_test_push", "Push settings round-trip with the access token redacted and kept; the test push uses the stored token", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage put = await fx.AuthClient.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                {
                    Push = new { Enabled = true, ExpoAccessToken = "expo-secret-access-token", Categories = new[] { "AskProposal", "MissionFailed" }, MaxPerUserPerMinute = 5000, DedupeWindowSeconds = 60 }
                })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, put);
                string putBody = await put.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertFalse(putBody.Contains("expo-secret-access-token"), "PUT response redacts the token");

                PushSettingsEnvelope settings = JsonHelper.Deserialize<PushSettingsEnvelope>(await (await fx.AuthClient.GetAsync("/api/v1/settings").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertNotNull(settings.Push, "push settings present");
                AssertEqual("********", settings.Push!.ExpoAccessToken, "GET redacts the token");
                AssertEqual("AskProposal,MissionFailed", String.Join(",", settings.Push.Categories));
                AssertEqual(600, settings.Push.MaxPerUserPerMinute, "clamped");
                AssertEqual(60, settings.Push.DedupeWindowSeconds);

                AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Push = settings.Push })).ConfigureAwait(false), "PUT back what GET returned");

                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-test", false).ConfigureAwait(false);
                using HttpClient u = user.CreateClient(fx.BaseUrl);
                string token = NewToken();
                PushDevice device = await JsonHelper.DeserializeAsync<PushDevice>(await u.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Ios", ExpoPushToken = token })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual("AskProposal,MissionFailed", String.Join(",", device.Categories), "new devices use the configured defaults");

                HttpResponseMessage test = await u.PostAsync("/api/v1/push/devices/" + device.Id + "/test", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, test);
                PushTestResult result = await JsonHelper.DeserializeAsync<PushTestResult>(test).ConfigureAwait(false);
                AssertEqual(PushTestStatusEnum.Sent, result.Status, result.Message ?? "");
                int index = fx.PushTransport.Messages.FindIndex(m => m.To == token);
                AssertTrue(index >= 0, "the test message reached the transport");
                AssertEqual(PushNotificationKinds.Test, fx.PushTransport.Messages[index].Data.Kind);
                AssertEqual("expo-secret-access-token", fx.PushTransport.AccessTokens.Last(), "the stored (unredacted) token was kept and used");

                AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Push = new { Enabled = false } })).ConfigureAwait(false));
                PushTestResult disabled = await JsonHelper.DeserializeAsync<PushTestResult>(await u.PostAsync("/api/v1/push/devices/" + device.Id + "/test", null).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(PushTestStatusEnum.Disabled, disabled.Status, "disabled");
                AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Push = new { Enabled = true } })).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("failed_mission_delivers_and_transport_failure_is_isolated", "A mission entering Failed pushes to the owner's device; a failing transport never breaks the transition", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "push-owner", true).ConfigureAwait(false);
                using HttpClient o = owner.CreateClient(fx.BaseUrl);
                string token = NewToken();
                AssertStatusCode(HttpStatusCode.Created, await o.PostAsync("/api/v1/push/devices", JsonHelper.ToJsonContent(new { Platform = "Ios", ExpoPushToken = token })).ConfigureAwait(false));
                string vesselId = await CreateVesselAsync(o).ConfigureAwait(false);

                string missionId = await CreateMissionAsync(o, vesselId, "Push me").ConfigureAwait(false);
                await TransitionAsync(o, missionId, "Assigned").ConfigureAwait(false);
                await TransitionAsync(o, missionId, "InProgress").ConfigureAwait(false);
                await TransitionAsync(o, missionId, "Failed").ConfigureAwait(false);
                await fx.Server.PushNotifications!.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                PushMessage message = fx.PushTransport.Messages.Single(m => m.To == token);
                AssertEqual(PushNotificationKinds.Failed, message.Data.Kind);
                AssertEqual(missionId, message.Data.EntityId);
                AssertEqual("/missions/" + missionId, message.Data.Url);
                AssertTrue(message.Body.Contains("Push me"), message.Body);

                fx.PushTransport.AlwaysThrow = true;
                try
                {
                    string second = await CreateMissionAsync(o, vesselId, "Still lands").ConfigureAwait(false);
                    await TransitionAsync(o, second, "Assigned").ConfigureAwait(false);
                    await TransitionAsync(o, second, "InProgress").ConfigureAwait(false);
                    await TransitionAsync(o, second, "Failed").ConfigureAwait(false);
                    await fx.Server.PushNotifications!.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    Mission read = await JsonHelper.DeserializeAsync<Mission>(await o.GetAsync("/api/v1/missions/" + second).ConfigureAwait(false)).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, read.Status, "the transition completed despite the push failure");
                    AssertEqual(1, fx.PushTransport.Messages.Count(m => m.To == token), "nothing more was delivered");
                }
                finally
                {
                    fx.PushTransport.AlwaysThrow = false;
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "E2E: push devices and delivery",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string NewToken()
        {
            return "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";
        }

        private static async Task<string> CreateVesselAsync(HttpClient client)
        {
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(await client.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = "PushFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false)).ConfigureAwait(false);
            HttpResponseMessage resp = await client.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = "PushVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleet.Id })).ConfigureAwait(false);
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp).ConfigureAwait(false);
            if (String.IsNullOrEmpty(vessel.Id)) throw new AssertionException("vessel create failed: " + (int)resp.StatusCode);
            return vessel.Id;
        }

        private static async Task<string> CreateMissionAsync(HttpClient client, string vesselId, string title)
        {
            HttpResponseMessage resp = await client.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new { Title = title, VesselId = vesselId, Description = "" })).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission = wrapper.Mission ?? JsonHelper.Deserialize<Mission>(body);
            if (String.IsNullOrEmpty(mission.Id)) throw new AssertionException("mission create failed (" + (int)resp.StatusCode + "): " + body);
            return mission.Id;
        }

        private static async Task TransitionAsync(HttpClient client, string missionId, string status)
        {
            HttpResponseMessage resp = await client.PutAsync("/api/v1/missions/" + missionId + "/status", JsonHelper.ToJsonContent(new { Status = status })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, resp, "transition to " + status);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.EndToEnd });
        }

        #endregion
    }
}
