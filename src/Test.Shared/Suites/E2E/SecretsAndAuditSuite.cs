namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// W1.5 and W1.6 coverage on a dedicated server: seeds known secrets through the API (remote tunnel password and
    /// enrollment token, a new bearer token, passwords in JSON and non-JSON login bodies, a token in a query string)
    /// and proves none of them appears in read responses, request history (entries and captured bodies and headers),
    /// or the server log; and proves a workspace command writes an audit.command event that a tenant admin cannot
    /// delete.
    /// </summary>
    public sealed class SecretsAndAuditSuite : IArmadaTestSuite
    {
        #region Private-Members

        // Requests the seeded_secrets_never_leak case makes before it reads the history: settings PUT, credential
        // POST, two authenticate POSTs, fleets GET and POST, five reads, and the redacted settings round trip.
        private const int ExpectedCapturedRequests = 12;

        private const string SuiteId = "E2E.SecretsAndAudit";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("seeded_secrets_never_leak", "Seeded secrets do not appear in responses, request history, or logs", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    string tunnelPassword = "TunnelPw-" + Guid.NewGuid().ToString("N");
                    string enrollmentToken = "Enroll-" + Guid.NewGuid().ToString("N");
                    string loginPasswordText = "LoginText-" + Guid.NewGuid().ToString("N");
                    string loginPasswordJson = "LoginJson-" + Guid.NewGuid().ToString("N");
                    string queryToken = "QueryTok-" + Guid.NewGuid().ToString("N");
                    string githubShaped = "ghp_" + Guid.NewGuid().ToString("N") + "ABCD";

                    using (HttpClient admin = server.CreateRestClient(true))
                    using (HttpClient anonymous = server.CreateRestClient(false))
                    {
                        // Seed: remote tunnel secrets through settings (Enabled stays false so nothing dials out).
                        HttpResponseMessage put = await admin.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                        {
                            RemoteControl = new { Enabled = false, Password = tunnelPassword, EnrollmentToken = enrollmentToken }
                        })).ConfigureAwait(false);
                        AssertEqual(200, (int)put.StatusCode, "settings update");
                        string putBody = await put.Content.ReadAsStringAsync().ConfigureAwait(false);

                        // Seed: a credential; its token is returned once, by the create call only.
                        HttpResponseMessage created = await admin.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new
                        {
                            TenantId = Armada.Core.Constants.DefaultTenantId,
                            UserId = Armada.Core.Constants.DefaultUserId,
                            Name = "leak-test",
                            BearerToken = "client-chosen-token-must-be-ignored"
                        })).ConfigureAwait(false);
                        AssertEqual(201, (int)created.StatusCode, "credential create");
                        Credential credential = JsonHelper.Deserialize<Credential>(await created.Content.ReadAsStringAsync().ConfigureAwait(false));
                        string bearerToken = credential.BearerToken;
                        AssertTrue(bearerToken.Length >= 32 && bearerToken != "client-chosen-token-must-be-ignored", "server-generated token");

                        // Seed: passwords in a non-JSON and a JSON login body, a token in a query string, a
                        // secret-shaped value under an innocuous key.
                        await anonymous.PostAsync("/api/v1/authenticate", new StringContent(
                            "{\"TenantId\":\"default\",\"Email\":\"admin@armada\",\"Password\": \"" + loginPasswordText + "\"}", Encoding.UTF8, "text/plain")).ConfigureAwait(false);
                        await anonymous.PostAsync("/api/v1/authenticate", JsonHelper.ToJsonContent(new
                        {
                            TenantId = "default",
                            Email = "admin@armada",
                            Password = loginPasswordJson
                        })).ConfigureAwait(false);
                        await admin.GetAsync("/api/v1/fleets?token=" + queryToken).ConfigureAwait(false);
                        await admin.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = "leak-fleet", Description = githubShaped })).ConfigureAwait(false);

                        List<string> secrets = new List<string> { tunnelPassword, enrollmentToken, loginPasswordText, loginPasswordJson, queryToken, bearerToken };

                        // Read responses
                        List<string> reads = new List<string>
                        {
                            putBody,
                            await (await admin.GetAsync("/api/v1/settings").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false),
                            await (await admin.GetAsync("/api/v1/credentials").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false),
                            await (await admin.GetAsync("/api/v1/credentials/" + credential.Id).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false),
                            await (await admin.GetAsync("/api/v1/status").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false),
                            await (await admin.GetAsync("/api/v1/whoami").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false)
                        };
                        foreach (string body in reads) AssertNoSecrets(body, secrets, "read response");

                        // A redacted settings round trip keeps the stored secrets.
                        HttpResponseMessage roundTrip = await admin.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                        {
                            RemoteControl = new { Enabled = false, Password = "********", EnrollmentToken = "********" }
                        })).ConfigureAwait(false);
                        AssertEqual(200, (int)roundTrip.StatusCode);
                        AssertEqual(tunnelPassword, server.Settings.RemoteControl.Password, "stored tunnel password kept");
                        AssertEqual(enrollmentToken, server.Settings.RemoteControl.EnrollmentToken, "stored enrollment token kept");

                        // Request history: every captured entry and its detail (headers and bodies).
                        StringBuilder history = new StringBuilder();
                        // The server records request history after it has sent the response (PostRouting), so the
                        // last requests above can still be in flight to the database when the client reads the
                        // history. Wait until every request this case made is recorded instead of assuming the
                        // write finished before the response arrived (on slow disks it does not).
                        EnumerationResult<RequestHistoryEntry> entries = await WaitForCapturedRequestsAsync(admin, ExpectedCapturedRequests).ConfigureAwait(false);
                        foreach (RequestHistoryEntry entry in entries.Objects)
                        {
                            history.AppendLine(await (await admin.GetAsync("/api/v1/request-history/" + entry.Id).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false));
                        }

                        string historyText = history.ToString();
                        AssertNoSecrets(historyText, secrets, "request history");
                        AssertFalse(historyText.Contains(githubShaped), "secret-shaped value under an innocuous key is redacted in request history");
                        AssertContains("[REDACTED]", historyText);
                    }

                    // Logs
                    StringBuilder logs = new StringBuilder();
                    foreach (string file in Directory.GetFiles(server.LogDirectory, "*", SearchOption.AllDirectories))
                    {
                        using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            logs.AppendLine(await reader.ReadToEndAsync().ConfigureAwait(false));
                        }
                    }

                    AssertNoSecrets(logs.ToString(), new List<string> { tunnelPassword, enrollmentToken, loginPasswordText, loginPasswordJson, queryToken }, "server log");
                }
            }));

            cases.Add(CaseAsync("workspace_exec_is_audited", "Workspace exec writes an audit.command event a tenant admin cannot delete", TestTags.Positive, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    string workDir = Path.Combine(server.TempDir, "exec-vessel");
                    Directory.CreateDirectory(workDir);
                    string marker = "audit-marker-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                    using (HttpClient admin = server.CreateRestClient(true))
                    {
                        HttpResponseMessage vesselResponse = await admin.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new
                        {
                            Name = "Audit Vessel",
                            RepoUrl = "file:///tmp/audit-vessel.git",
                            WorkingDirectory = workDir,
                            DefaultBranch = "main"
                        })).ConfigureAwait(false);
                        AssertEqual(201, (int)vesselResponse.StatusCode, "vessel create");
                        Vessel vessel = JsonHelper.Deserialize<Vessel>(await vesselResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

                        HttpResponseMessage exec = await admin.PostAsync("/api/v1/workspace/vessels/" + vessel.Id + "/exec",
                            JsonHelper.ToJsonContent(new { Command = "echo " + marker, TimeoutSeconds = 30 })).ConfigureAwait(false);
                        AssertEqual(200, (int)exec.StatusCode, "exec");

                        EnumerationResult<ArmadaEvent> events = JsonHelper.Deserialize<EnumerationResult<ArmadaEvent>>(
                            await (await admin.GetAsync("/api/v1/events?pageSize=200").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false));
                        ArmadaEvent? audit = events.Objects.FirstOrDefault(e => e.EventType == CommandAudit.EventType
                            && !String.IsNullOrEmpty(e.Payload)
                            && JsonHelper.Deserialize<CommandAuditRecord>(e.Payload!).Command == "echo " + marker);
                        AssertNotNull(audit, "audit.command event for the exec");
                        AssertEqual("WorkspaceExec", JsonHelper.Deserialize<CommandAuditRecord>(audit!.Payload!).Source, "audit source");
                        AssertEqual(vessel.Id, audit.VesselId);

                        // A tenant admin (not a global admin) cannot delete it.
                        string email = "tenant-admin-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com";
                        HttpResponseMessage userResponse = await admin.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
                        {
                            TenantId = Armada.Core.Constants.DefaultTenantId,
                            Email = email,
                            Password = "tenant-admin-password",
                            IsTenantAdmin = true
                        })).ConfigureAwait(false);
                        AssertEqual(201, (int)userResponse.StatusCode, "tenant admin create");
                        UserMaster user = JsonHelper.Deserialize<UserMaster>(await userResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
                        HttpResponseMessage credentialResponse = await admin.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new
                        {
                            TenantId = Armada.Core.Constants.DefaultTenantId,
                            UserId = user.Id,
                            Name = "tenant-admin"
                        })).ConfigureAwait(false);
                        Credential credential = JsonHelper.Deserialize<Credential>(await credentialResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

                        using (HttpClient tenantAdmin = server.CreateRestClient(false))
                        {
                            tenantAdmin.DefaultRequestHeaders.Add("Authorization", "Bearer " + credential.BearerToken);
                            HttpResponseMessage denied = await tenantAdmin.DeleteAsync("/api/v1/events/" + audit.Id).ConfigureAwait(false);
                            AssertTrue((int)denied.StatusCode == 403 || (int)denied.StatusCode == 404, "tenant admin delete of an audit event refused, got " + (int)denied.StatusCode);

                            // A tenant admin also cannot take over the global admin in the same tenant.
                            HttpResponseMessage takeover = await tenantAdmin.PutAsync("/api/v1/users/" + Armada.Core.Constants.DefaultUserId, JsonHelper.ToJsonContent(new
                            {
                                Email = Armada.Core.Constants.DefaultUserEmail,
                                Password = "taken-over-password",
                                Active = true
                            })).ConfigureAwait(false);
                            AssertEqual(403, (int)takeover.StatusCode, "tenant admin edit of a global admin");

                            HttpResponseMessage mint = await tenantAdmin.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new
                            {
                                UserId = Armada.Core.Constants.DefaultUserId,
                                Name = "escalation"
                            })).ConfigureAwait(false);
                            AssertEqual(403, (int)mint.StatusCode, "tenant admin credential for a global admin");
                        }

                        HttpResponseMessage allowed = await admin.DeleteAsync("/api/v1/events/" + audit.Id).ConfigureAwait(false);
                        AssertEqual(204, (int)allowed.StatusCode, "global admin may delete an audit event");
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Secrets and Audit",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<EnumerationResult<RequestHistoryEntry>> WaitForCapturedRequestsAsync(HttpClient admin, int expected)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(15));
            EnumerationResult<RequestHistoryEntry> entries = new EnumerationResult<RequestHistoryEntry>();
            List<RequestHistoryEntry> captured = new List<RequestHistoryEntry>();
            while (true)
            {
                HttpResponseMessage response = await admin.GetAsync("/api/v1/request-history?pageSize=500").ConfigureAwait(false);
                AssertEqual(200, (int)response.StatusCode, "request history list");
                entries = JsonHelper.Deserialize<EnumerationResult<RequestHistoryEntry>>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                // The history reads made by this wait are captured too; count only the requests under test.
                captured = entries.Objects.Where(e => !(e.Route ?? "").StartsWith("/api/v1/request-history", StringComparison.Ordinal)).ToList();
                if (captured.Count >= expected || deadline.Passed) break;
                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert(captured.Count >= expected, "expected " + expected + " captured requests, found " + captured.Count + ": "
                + String.Join(", ", captured.Select(e => e.Method + " " + e.Route + " " + e.StatusCode)));
            return entries;
        }

        private static void AssertNoSecrets(string text, List<string> secrets, string where)
        {
            foreach (string secret in secrets)
            {
                Assert(!text.Contains(secret, StringComparison.Ordinal), "secret " + secret.Substring(0, Math.Min(12, secret.Length)) + "... leaked in " + where);
            }
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
