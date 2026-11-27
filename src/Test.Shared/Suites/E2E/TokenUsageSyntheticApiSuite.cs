namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using Armada.Client;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Live synthetic-import route, SDK, idempotency and admin-only authorization.</summary>
    public sealed class TokenUsageSyntheticApiSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor("E2E.TokenUsageSyntheticApi", "Synthetic usage API", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor("E2E.TokenUsageSyntheticApi", "admin_sdk_and_access", "Admin-only SDK import is replayable and summarized separately from real usage", async token =>
                {
                    E2EServerFixture server = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    TokenUsageSyntheticRequest request = new TokenUsageSyntheticRequest { BatchId = "api-" + Guid.NewGuid().ToString("N"), Records = new List<TokenUsageSyntheticRecord> { new TokenUsageSyntheticRecord { Model = "demo", CreatedUtc = DateTime.UtcNow.AddDays(-35), InputTokens = 110, OutputTokens = 40, CachedTokens = 50 } } };
                    using (StringContent content = JsonHelper.ToJsonContent(request))
                    using (HttpResponseMessage response = await server.UnauthClient.PostAsync("/api/v1/token-usage/synthetic", content, token).ConfigureAwait(false))
                        AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
                    E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(server.AuthClient, "synthetic-tenant", true).ConfigureAwait(false);
                    using HttpClient scoped = tenantAdmin.CreateClient(server.BaseUrl);
                    using (StringContent content = JsonHelper.ToJsonContent(request))
                    using (HttpResponseMessage response = await scoped.PostAsync("/api/v1/token-usage/synthetic", content, token).ConfigureAwait(false))
                        AssertEqual(HttpStatusCode.Forbidden, response.StatusCode, "tenant admin cannot manufacture usage");
                    using ArmadaClient client = new ArmadaClient(server.BaseUrl);
                    client.Options.ApiKey = server.ApiKey;
                    TokenUsageSyntheticResult result = (await client.TokenUsage.ImportSyntheticAsync(request, token).ConfigureAwait(false))!;
                    AssertEqual(1, result.Created, "first import creates one row"); AssertEqual(150L, result.TotalTokens);
                    AssertEqual(1, (await client.TokenUsage.ImportSyntheticAsync(request, token).ConfigureAwait(false))!.Existing, "replay recognizes existing row");
                    TokenUsageSummaryResult summary = (await client.TokenUsage.GetSummaryAsync(new TokenUsageQuery { SourceId = result.SourceId, FromUtc = DateTime.UtcNow.AddDays(-40), ToUtc = DateTime.UtcNow, BucketMinutes = 1440 }, token).ConfigureAwait(false))!;
                    AssertEqual(1, summary.SyntheticCount, "summary includes tagged row using sourceId " + result.SourceId + "; records=" + summary.RecordCount + "; total=" + summary.TotalTokens + "; from=" + summary.FromUtc + "; to=" + summary.ToUtc); AssertEqual(150L, summary.TotalTokens);
                    request.Records[0].OutputTokens = 41;
                    using (StringContent content = JsonHelper.ToJsonContent(request))
                    using (HttpResponseMessage response = await server.AuthClient.PostAsync("/api/v1/token-usage/synthetic", content, token).ConfigureAwait(false))
                        AssertEqual(HttpStatusCode.Conflict, response.StatusCode);
                })
            });
        }
    }
}
