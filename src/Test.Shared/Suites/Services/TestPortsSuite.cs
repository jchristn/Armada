namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The test listeners' port allocation. <see cref="TestPorts.Reserve"/> checks that a port is free and releases it,
    /// so a listener that bound the reserved port afterwards failed with "Address already in use" whenever something
    /// else bound the port in that gap (the intermittent HelmRemoteTarget mission create failure on macOS CI). These
    /// cases take the port in the gap on purpose: test listeners must then bind a fresh port.
    /// </summary>
    public sealed class TestPortsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.TestPorts";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("reserved_port_can_be_taken_before_bind", "A reserved port is released, so a socket bound in the gap makes the reserving listener's bind fail with address in use", TestTags.Negative, () =>
            {
                int port = TestPorts.Reserve(1)[0];
                TcpListener squatter = new TcpListener(IPAddress.Loopback, port);
                squatter.Start();
                try
                {
                    TcpListener late = new TcpListener(IPAddress.Loopback, port);
                    Exception? failure = null;
                    try
                    {
                        late.Start();
                    }
                    catch (SocketException ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        late.Stop();
                    }

                    AssertNotNull(failure, "binding a reserved port that someone else bound first fails");
                    AssertTrue(TestPorts.IsAddressInUse(failure), "the failure is recognized as address in use: " + failure!.Message);
                }
                finally
                {
                    squatter.Stop();
                }
            }));

            cases.Add(CaseAsync("stub_binds_fresh_port_when_reserved_port_is_taken", "The Admiral stub binds a fresh port and serves requests when its reserved port is taken before it binds (it used to throw 'Address already in use')", TestTags.Reliability, async () =>
            {
                List<int> offered = new List<int>();
                PortSquatter? squatter = null;
                try
                {
                    using (RecordingAdmiralStub stub = new RecordingAdmiralStub(port =>
                    {
                        offered.Add(port);
                        if (squatter == null) squatter = PortSquatter.Take(port);
                    }))
                    {
                        AssertEqual(2, offered.Count, "the taken port is replaced by exactly one fresh reservation");
                        AssertNotEqual(offered[0], offered[1], "a different port is offered after the collision");
                        AssertEqual("http://127.0.0.1:" + offered[1], stub.BaseUrl, "the stub listens on the fresh port");

                        using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                        {
                            HttpResponseMessage health = await client.GetAsync(stub.BaseUrl + "/api/v1/status/health").ConfigureAwait(false);
                            AssertEqual(HttpStatusCode.OK, health.StatusCode, "the stub answers on its port");
                        }

                        AssertTrue(stub.Requests().Exists(r => r.Path == "/api/v1/status/health"), "the request reached the stub");
                    }
                }
                finally
                {
                    squatter?.Dispose();
                }
            }));

            cases.Add(Case("other_start_failures_are_not_retried", "A start failure other than address in use is thrown unchanged after a single attempt", TestTags.Negative, () =>
            {
                int attempts = 0;
                InvalidOperationException? thrown = null;
                try
                {
                    TestPorts.StartOnFreePorts<int>(1, ports =>
                    {
                        attempts++;
                        throw new InvalidOperationException("refused for another reason");
                    });
                }
                catch (InvalidOperationException ex)
                {
                    thrown = ex;
                }

                AssertNotNull(thrown);
                AssertEqual("refused for another reason", thrown!.Message);
                AssertEqual(1, attempts, "no retry for a failure that is not a taken port");
            }));

            cases.Add(CaseAsync("start_gives_up_after_bounded_attempts", "A start whose every port is taken gives up after MaxStartAttempts fresh reservations with an error naming the ports", TestTags.Negative, async () =>
            {
                List<int> offered = new List<int>();
                InvalidOperationException? thrown = null;
                try
                {
                    await TestPorts.StartOnFreePortsAsync<int>(1, ports =>
                    {
                        offered.Add(ports[0]);
                        throw new SocketException((int)SocketError.AddressAlreadyInUse);
                    }).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    thrown = ex;
                }

                AssertNotNull(thrown, "the allocation gives up");
                AssertEqual(TestPorts.MaxStartAttempts, offered.Count, "bounded attempts");
                AssertEqual(TestPorts.MaxStartAttempts, new HashSet<int>(offered).Count, "every attempt uses a fresh port");
                AssertContains(offered[0].ToString(), thrown!.Message, "the error names the taken ports");
                AssertTrue(TestPorts.IsAddressInUse(thrown.InnerException), "the last bind failure is kept as the inner exception");
            }));

            cases.Add(Case("address_in_use_detection", "Address-in-use is recognized directly, wrapped, and in an AggregateException; other socket errors are not", TestTags.Positive, () =>
            {
                SocketException inUse = new SocketException((int)SocketError.AddressAlreadyInUse);
                AssertTrue(TestPorts.IsAddressInUse(inUse), "socket exception");
                AssertTrue(TestPorts.IsAddressInUse(new InvalidOperationException("wrapped", inUse)), "wrapped");
                AssertTrue(TestPorts.IsAddressInUse(new AggregateException(new TimeoutException(), inUse)), "aggregate");
                AssertFalse(TestPorts.IsAddressInUse(new SocketException((int)SocketError.ConnectionRefused)), "connection refused");
                AssertFalse(TestPorts.IsAddressInUse(new InvalidOperationException("Address already in use")), "the message alone is not enough");
                AssertFalse(TestPorts.IsAddressInUse(null), "null");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Test Ports",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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

        #region Private-Classes

        /// <summary>
        /// Holds a loopback port the way a listener that is not the stub's would: a TCP listener on Linux and macOS,
        /// where the stub's HttpListener binds a socket; an http.sys registration on Windows, where HttpListener does
        /// not bind a socket of its own.
        /// </summary>
        private sealed class PortSquatter : IDisposable
        {
            private TcpListener? _Socket = null;
            private HttpListener? _Http = null;

            public static PortSquatter Take(int port)
            {
                PortSquatter squatter = new PortSquatter();
                if (OperatingSystem.IsWindows())
                {
                    squatter._Http = new HttpListener();
                    squatter._Http.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    squatter._Http.Start();
                }
                else
                {
                    squatter._Socket = new TcpListener(IPAddress.Loopback, port);
                    squatter._Socket.Start();
                }

                return squatter;
            }

            public void Dispose()
            {
                try { _Socket?.Stop(); } catch { }
                try { _Http?.Close(); } catch { }
            }
        }

        #endregion
    }
}
