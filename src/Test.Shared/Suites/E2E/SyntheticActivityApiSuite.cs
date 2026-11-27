namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using Armada.Client;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Real activity-import HTTP authorization, validation and reply coverage.</summary>
    public sealed class SyntheticActivityApiSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor("E2E.SyntheticActivity", "Synthetic activity API", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor("E2E.SyntheticActivity", "admin_access", "Only global admins can generate synthetic activity; malformed profiles return 400 and fixed profiles replay", async token =>
                {
                    E2EServerFixture server = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    SyntheticActivityRequest profile = new SyntheticActivityRequest { BatchId = "activity-" + Guid.NewGuid().ToString("N"), Days = 3, Density = 0.1, Entropy = 1, Seed = 82, ToUtc = DateTime.UtcNow.AddMinutes(-1) };
                    using (StringContent body = JsonHelper.ToJsonContent(profile))
                    using (HttpResponseMessage response = await server.UnauthClient.PostAsync("/api/v1/activity/synthetic", body, token).ConfigureAwait(false)) AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
                    E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(server.AuthClient, "activity-admin", true).ConfigureAwait(false);
                    using (HttpClient scoped = tenantAdmin.CreateClient(server.BaseUrl))
                    using (StringContent body = JsonHelper.ToJsonContent(profile))
                    using (HttpResponseMessage response = await scoped.PostAsync("/api/v1/activity/synthetic", body, token).ConfigureAwait(false)) AssertEqual(HttpStatusCode.Forbidden, response.StatusCode);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        using (StringContent body = JsonHelper.ToJsonContent(profile))
                        using (HttpResponseMessage response = await server.AuthClient.PostAsync("/api/v1/activity/synthetic", body, token).ConfigureAwait(false))
                        {
                            AssertEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                            SyntheticActivityResult result = JsonSerializer.Deserialize<SyntheticActivityResult>(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                            if (attempt == 0) AssertTrue(result.Created.GetValueOrDefault("missions") >= 12 && result.Created.GetValueOrDefault("requests") >= 12, "recent activity generated");
                            else AssertEqual(0, result.Created.Count, "replay inserts no records");
                            if (attempt == 0)
                            {
                                string window = "?fromUtc=" + Uri.EscapeDataString(profile.ToUtc.AddDays(-3).ToString("o")) + "&toUtc=" + Uri.EscapeDataString(profile.ToUtc.ToString("o")) + "&bucketMinutes=1440";
                                RequestHistorySummaryResult requests = JsonSerializer.Deserialize<RequestHistorySummaryResult>(await server.AuthClient.GetStringAsync("/api/v1/request-history/summary" + window + "&principal=" + Uri.EscapeDataString("synthetic:" + profile.BatchId), token).ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                                AssertEqual(result.Created["requests"], requests.TotalCount, "encoded API summary window includes historical rows");
                                AssertEqual(profile.ToUtc.AddDays(-3), requests.FromUtc, "exact decoded summary start");
                                MissionHistorySummaryResult missions = JsonSerializer.Deserialize<MissionHistorySummaryResult>(await server.AuthClient.GetStringAsync("/api/v1/missions/history" + window + "&vesselId=" + result.VesselId, token).ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                                AssertEqual(result.Created["missions"], missions.TotalCount, "mission history includes all generated rows");
                                AssertEqual(profile.ToUtc.AddDays(-3), missions.FromUtc, "exact decoded mission start");
                            }

                        }
                    }
                    using (ArmadaClient sdk = new ArmadaClient(server.BaseUrl))
                    {
                        sdk.Options.ApiKey = server.ApiKey;
                        SyntheticActivityResult replay = (await sdk.Server.GenerateSyntheticActivityAsync(profile, token).ConfigureAwait(false))!;
                        AssertEqual(0, replay.Created.Count, "typed SDK replay");
                    }
                    profile.Density = -1;
                    using (StringContent body = JsonHelper.ToJsonContent(profile))
                    using (HttpResponseMessage response = await server.AuthClient.PostAsync("/api/v1/activity/synthetic", body, token).ConfigureAwait(false)) AssertEqual(HttpStatusCode.BadRequest, response.StatusCode);
                })
            });
        }
    }
}
