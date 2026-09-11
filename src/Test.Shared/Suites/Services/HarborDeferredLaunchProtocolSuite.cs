namespace Test.Shared.Suites.Services
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors verifying that the deferred-launch cutover messages
    /// (<see cref="HarborDeferredLaunchRequest"/> and <see cref="HarborDeferredLaunchAck"/>) round-trip through
    /// the polymorphic Harbor serializer with their type discriminators and fields intact. See
    /// docs/SERVER_REBUILD.md and docs/HARBOR_PROTOCOL.md.
    /// </summary>
    public sealed class HarborDeferredLaunchProtocolSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the deferred-launch protocol suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("request_round_trips", "Deferred-launch request round-trips", TestTags.Positive, () =>
            {
                HarborDeferredLaunchRequest request = new HarborDeferredLaunchRequest
                {
                    RequestId = "req-123",
                    LaunchExePath = @"C:\slots\new\Armada.Server.exe",
                    WaitForPid = 4242,
                    WorkingDirectory = @"C:\slots\new",
                    HealthUrl = "http://127.0.0.1:7890/api/v1/status/health",
                    HealthTimeoutSeconds = 90,
                    FallbackExePath = @"C:\slots\old\Armada.Server.exe",
                    FallbackSlot = "2026-09-08_old",
                    CurrentPointerPath = @"C:\bin\current"
                };

                string json = HarborProtocol.Serialize(request);
                HarborMessage decoded = HarborProtocol.Deserialize(json);

                HarborDeferredLaunchRequest? typed = decoded as HarborDeferredLaunchRequest;
                AssertNotNull(typed, "Expected a HarborDeferredLaunchRequest after deserialization.");
                AssertEqual("req-123", typed!.RequestId);
                AssertEqual(4242, typed.WaitForPid);
                AssertEqual(90, typed.HealthTimeoutSeconds);
                AssertEqual("2026-09-08_old", typed.FallbackSlot);
                AssertEqual("http://127.0.0.1:7890/api/v1/status/health", typed.HealthUrl);
            }));

            cases.Add(Case("ack_round_trips", "Deferred-launch ack round-trips", TestTags.Positive, () =>
            {
                HarborDeferredLaunchAck ack = new HarborDeferredLaunchAck
                {
                    RequestId = "req-123",
                    Armed = true
                };

                string json = HarborProtocol.Serialize(ack);
                HarborMessage decoded = HarborProtocol.Deserialize(json);

                HarborDeferredLaunchAck? typed = decoded as HarborDeferredLaunchAck;
                AssertNotNull(typed, "Expected a HarborDeferredLaunchAck after deserialization.");
                AssertEqual("req-123", typed!.RequestId);
                AssertTrue(typed.Armed, "Expected Armed to survive the round-trip.");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborDeferredLaunchProtocol",
                displayName: "Harbor Deferred-Launch Protocol",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, System.Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.HarborDeferredLaunchProtocol",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
