namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box storage through a running Admiral: the twelfth doctor check (Black Box Storage) passes for the default
    /// target and fails with the reason for an unreachable one, PUT /api/v1/settings refuses invalid storage settings
    /// before applying anything, applies valid ones to the live runtime in place, masks storage secrets on read and keeps
    /// them when written back masked.
    /// </summary>
    public sealed class BlackBoxStorageApiSuite : IArmadaTestSuite
    {
        private const string SuiteId = "E2E.BlackBoxStorage";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("doctor_and_settings", "Doctor reports black box storage; invalid storage settings are refused; valid ones apply in place with secrets masked", TestTags.Positive, async () =>
            {
                using SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                await server.StartAsync().ConfigureAwait(false);
                using HttpClient admin = server.CreateRestClient(true);
                BlackBoxSettings live = server.Settings.BlackBox;

                List<DoctorRow> doctor = await DoctorAsync(admin).ConfigureAwait(false);
                AssertEqual(12, doctor.Count, "twelve checks");
                DoctorRow storage = doctor.Single(d => d.Name == BlackBoxStorageDoctorResult.CheckName);
                AssertEqual("Pass", storage.Status, "database target passes: " + storage.Message);

                HttpResponseMessage refused = await admin.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                {
                    BlackBox = new { Enabled = true, Storage = new { Target = "S3", S3 = new { Region = "us-east-1", AccessKey = "AKIAE2E", SecretKey = "e2e-secret-value" } } }
                })).ConfigureAwait(false);
                AssertEqual(400, (int)refused.StatusCode, "missing bucket refused");
                string refusedBody = await refused.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertContains("blackBox.storage.s3.bucket is required", refusedBody, "names the setting");
                AssertFalse(refusedBody.Contains("e2e-secret-value"), "no secret in the error");
                AssertEqual(BlackBoxStorageTargetEnum.Database, server.Settings.BlackBox.Storage.Target, "nothing applied");

                int deadPort = TestPorts.Reserve(1)[0];
                HttpResponseMessage applied = await admin.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                {
                    BlackBox = new
                    {
                        Enabled = true,
                        Storage = new { Target = "S3", S3 = new { Endpoint = "http://127.0.0.1:" + deadPort, UseSsl = false, Region = "us-east-1", Bucket = "armada", AccessKey = "AKIAE2E", SecretKey = "e2e-secret-value" } },
                        Retention = new { RetentionDays = 45 }
                    }
                })).ConfigureAwait(false);
                AssertEqual(200, (int)applied.StatusCode, "valid settings applied: " + await applied.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertTrue(ReferenceEquals(live, server.Settings.BlackBox), "applied in place (the runtime keeps reading it)");
                AssertEqual(BlackBoxStorageTargetEnum.S3, server.Server.BlackBox!.Stores.GetConfigured().Target, "the runtime uses the new target");
                AssertEqual(45, server.Settings.BlackBox.Retention.RetentionDays, "retention applied");

                string read = await (await admin.GetAsync("/api/v1/settings").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertFalse(read.Contains("e2e-secret-value") || read.Contains("AKIAE2E"), "storage secrets masked on read");
                AssertContains("http://127.0.0.1:" + deadPort, read, "endpoint visible");

                DoctorRow unreachable = (await DoctorAsync(admin).ConfigureAwait(false)).Single(d => d.Name == BlackBoxStorageDoctorResult.CheckName);
                AssertEqual("Fail", unreachable.Status, "unreachable target fails");
                AssertContains("S3", unreachable.Message, "names the target");
                AssertFalse(unreachable.Message.Contains("e2e-secret-value"), "no secret in the doctor");

                HttpResponseMessage masked = await admin.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                {
                    BlackBox = new { Enabled = true, Storage = new { Target = "Database", S3 = new { Endpoint = "http://127.0.0.1:" + deadPort, UseSsl = false, Region = "us-east-1", Bucket = "armada", AccessKey = "********", SecretKey = "********" } } }
                })).ConfigureAwait(false);
                AssertEqual(200, (int)masked.StatusCode, "masked round trip accepted");
                AssertEqual("e2e-secret-value", server.Settings.BlackBox.Storage.S3.SecretKey, "masked value keeps the stored secret");
                AssertEqual("Pass", (await DoctorAsync(admin).ConfigureAwait(false)).Single(d => d.Name == BlackBoxStorageDoctorResult.CheckName).Status, "back on the database");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box storage through the REST API", cases: cases);
        }

        private static async Task<List<DoctorRow>> DoctorAsync(HttpClient admin)
        {
            HttpResponseMessage response = await admin.GetAsync("/api/v1/doctor").ConfigureAwait(false);
            AssertEqual(200, (int)response.StatusCode, "doctor");
            return JsonHelper.Deserialize<List<DoctorRow>>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag, TestTags.EndToEnd });
        }
    }
}
