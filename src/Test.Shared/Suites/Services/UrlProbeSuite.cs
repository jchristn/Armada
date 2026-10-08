namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Connectivity;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Covers the URL connectivity probe behind Harbor's Validate buttons (<see cref="UrlProbe"/>) against scripted
    /// loopback servers on free test ports: an HTTP success, a refused connection, a server that never answers (timeout),
    /// cancellation, bad URLs, DNS failure, HTTP errors and non-HTTP answers, TLS with an untrusted certificate, and the
    /// WebSocket upgrade (accepted, wrong accept key, and asking for credentials). Every request is checked to carry no
    /// credentials.
    /// </summary>
    public sealed class UrlProbeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UrlProbe";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("http_success", "An HTTP 200 passes every stage, reports the time, and sends no credentials", TestTags.Positive, async () =>
            {
                using (ScriptedTcpServer server = new ScriptedTcpServer((stream, head, token) =>
                    ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok", token)))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("http://user:secret@127.0.0.1:" + server.Port + "/api/v1/status/health?x=1").ConfigureAwait(false);
                    AssertTrue(result.Succeeded, result.Summary);
                    AssertEqual(UrlProbeFailureEnum.None, result.Failure);
                    AssertNull(result.FailedStep);
                    AssertEqual(200, result.HttpStatusCode);
                    AssertFalse(result.CredentialsRequired);
                    AssertTrue(result.CredentialsInUrlIgnored, "the URL's user name and password are noticed");
                    AssertTrue(result.ElapsedMs >= 0);
                    AssertEqual("127.0.0.1:" + server.Port, result.RemoteEndPoint);
                    AssertStatuses(result,
                        UrlProbeStepStatusEnum.Passed,
                        UrlProbeStepStatusEnum.Skipped,
                        UrlProbeStepStatusEnum.Passed,
                        UrlProbeStepStatusEnum.Skipped,
                        UrlProbeStepStatusEnum.Passed);
                    AssertEqual(UrlProbeStepEnum.Http, result.Steps[4].Step);

                    string head = await server.FirstRequest.ConfigureAwait(false);
                    AssertEqual("GET /api/v1/status/health?x=1 HTTP/1.1", FirstLine(head), "request line");
                    Dictionary<string, string> headers = Headers(head);
                    AssertEqual("127.0.0.1:" + server.Port, headers["Host"]);
                    AssertNoCredentials(headers);
                }
            }));

            cases.Add(CaseAsync("connection_refused", "A port nothing listens on fails at TCP as connection refused", TestTags.Negative, async () =>
            {
                int port = TestPorts.Reserve(1)[0];
                UrlProbeResult result = await UrlProbe.ProbeAsync("http://127.0.0.1:" + port + "/").ConfigureAwait(false);
                AssertFalse(result.Succeeded);
                AssertEqual(UrlProbeFailureEnum.ConnectionRefused, result.Failure, result.Summary);
                AssertEqual(UrlProbeStepEnum.Tcp, result.FailedStep);
                AssertEqual(UrlProbeStepStatusEnum.Failed, result.Steps[result.Steps.Count - 1].Status);
                AssertNull(result.Find(UrlProbeStepEnum.Http), "no HTTP stage after a failed connect");
                AssertNull(result.HttpStatusCode);
            }));

            cases.Add(CaseAsync("timeout", "A server that accepts but never answers fails at HTTP with a timeout", TestTags.Negative, async () =>
            {
                using (ScriptedTcpServer server = new ScriptedTcpServer((stream, head, token) => Task.Delay(Timeout.Infinite, token)))
                {
                    UrlProbeOptions options = new UrlProbeOptions { TimeoutMs = 400 };
                    UrlProbeResult result = await UrlProbe.ProbeAsync("http://127.0.0.1:" + server.Port + "/slow", options).ConfigureAwait(false);
                    AssertEqual(UrlProbeFailureEnum.Timeout, result.Failure, result.Summary);
                    AssertEqual(UrlProbeStepEnum.Http, result.FailedStep);
                    AssertEqual(UrlProbeStepStatusEnum.Passed, result.Find(UrlProbeStepEnum.Tcp)!.Status, "the connection itself worked");
                    AssertTrue(result.ElapsedMs >= 300 && result.ElapsedMs < 10000, "stopped at about the timeout: " + result.ElapsedMs + " ms");
                }
            }));

            cases.Add(CaseAsync("cancelled", "Cancelling the token stops the probe and reports it cancelled, not timed out", TestTags.Positive, async () =>
            {
                using (CancellationTokenSource cancel = new CancellationTokenSource())
                using (ScriptedTcpServer server = new ScriptedTcpServer(async (stream, head, token) =>
                {
                    cancel.Cancel();
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                }))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("http://127.0.0.1:" + server.Port + "/", new UrlProbeOptions { TimeoutMs = 60000 }, cancel.Token).ConfigureAwait(false);
                    AssertEqual(UrlProbeFailureEnum.Cancelled, result.Failure, result.Summary);
                    AssertEqual(UrlProbeStepEnum.Http, result.FailedStep);
                }
            }));

            cases.Add(CaseAsync("bad_urls", "Empty, relative, hostless, and non-web URLs fail at Parse without touching the network", TestTags.Negative, async () =>
            {
                Dictionary<string, UrlProbeFailureEnum> expected = new Dictionary<string, UrlProbeFailureEnum>
                {
                    [""] = UrlProbeFailureEnum.InvalidUrl,
                    ["   "] = UrlProbeFailureEnum.InvalidUrl,
                    ["not a url"] = UrlProbeFailureEnum.InvalidUrl,
                    ["/v1.0/harbor/connect"] = UrlProbeFailureEnum.InvalidUrl,
                    ["ftp://127.0.0.1/file"] = UrlProbeFailureEnum.UnsupportedScheme,
                    ["file:///etc/hosts"] = UrlProbeFailureEnum.InvalidUrl,
                    ["mailto:a@b.c"] = UrlProbeFailureEnum.UnsupportedScheme
                };

                foreach (KeyValuePair<string, UrlProbeFailureEnum> pair in expected)
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync(pair.Key).ConfigureAwait(false);
                    AssertEqual(pair.Value, result.Failure, "'" + pair.Key + "': " + result.Summary);
                    AssertEqual(UrlProbeStepEnum.Parse, result.FailedStep, pair.Key);
                    AssertEqual(1, result.Steps.Count, pair.Key);
                    AssertTrue(result.Summary.Length > 0, "a reason is given for '" + pair.Key + "'");
                }
            }));

            cases.Add(CaseAsync("dns_failure", "A host name that does not resolve fails at DNS", TestTags.Negative, async () =>
            {
                UrlProbeOptions options = new UrlProbeOptions
                {
                    Resolver = (host, token) => Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound))
                };
                UrlProbeResult result = await UrlProbe.ProbeAsync("https://admiral.example.invalid:7890/", options).ConfigureAwait(false);
                AssertEqual(UrlProbeFailureEnum.DnsFailed, result.Failure, result.Summary);
                AssertEqual(UrlProbeStepEnum.Dns, result.FailedStep);

                UrlProbeOptions none = new UrlProbeOptions { Resolver = (host, token) => Task.FromResult(new IPAddress[0]) };
                UrlProbeResult empty = await UrlProbe.ProbeAsync("http://admiral.example.invalid/", none).ConfigureAwait(false);
                AssertEqual(UrlProbeFailureEnum.DnsFailed, empty.Failure, empty.Summary);
            }));

            cases.Add(CaseAsync("dns_then_connect", "A resolved host name is connected to on the address the resolver gave", TestTags.Positive, async () =>
            {
                using (ScriptedTcpServer server = new ScriptedTcpServer((stream, head, token) =>
                    ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n", token)))
                {
                    UrlProbeOptions options = new UrlProbeOptions { Resolver = (host, token) => Task.FromResult(new IPAddress[] { IPAddress.Loopback }) };
                    UrlProbeResult result = await UrlProbe.ProbeAsync("http://admiral.test:" + server.Port + "/dashboard", options).ConfigureAwait(false);
                    AssertTrue(result.Succeeded, result.Summary);
                    AssertEqual(UrlProbeStepStatusEnum.Passed, result.Find(UrlProbeStepEnum.Dns)!.Status);
                    AssertEqual("admiral.test:" + server.Port, Headers(await server.FirstRequest.ConfigureAwait(false))["Host"], "Host carries the name, not the address");
                }
            }));

            cases.Add(CaseAsync("http_statuses", "401 is reachable and asks for credentials; 404 and 500 fail; a redirect passes without being followed", TestTags.Positive, async () =>
            {
                Dictionary<string, bool> succeeded = new Dictionary<string, bool>
                {
                    ["401 Unauthorized"] = true,
                    ["403 Forbidden"] = true,
                    ["302 Found\r\nLocation: /login"] = true,
                    ["404 Not Found"] = false,
                    ["500 Internal Server Error"] = false
                };

                foreach (KeyValuePair<string, bool> pair in succeeded)
                {
                    using (ScriptedTcpServer server = new ScriptedTcpServer((stream, head, token) =>
                        ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 " + pair.Key + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", token)))
                    {
                        UrlProbeResult result = await UrlProbe.ProbeAsync("http://127.0.0.1:" + server.Port + "/").ConfigureAwait(false);
                        int code = Int32.Parse(pair.Key.Substring(0, 3), System.Globalization.CultureInfo.InvariantCulture);
                        AssertEqual(code, result.HttpStatusCode, pair.Key);
                        AssertEqual(pair.Value, result.Succeeded, pair.Key + ": " + result.Summary);
                        AssertEqual(code == 401 || code == 403, result.CredentialsRequired, pair.Key);
                        if (!pair.Value) AssertEqual(UrlProbeFailureEnum.HttpError, result.Failure, pair.Key);
                        AssertEqual(1, server.Requests.Count, "one request, redirects not followed: " + pair.Key);
                    }
                }
            }));

            cases.Add(CaseAsync("not_http", "A server that answers with something other than HTTP, or closes at once, fails clearly", TestTags.Negative, async () =>
            {
                using (ScriptedTcpServer ssh = new ScriptedTcpServer((stream, head, token) => ScriptedTcpServer.WriteAsync(stream, "SSH-2.0-OpenSSH_9.6\r\n\r\n", token)))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("http://127.0.0.1:" + ssh.Port + "/").ConfigureAwait(false);
                    AssertEqual(UrlProbeFailureEnum.NotHttp, result.Failure, result.Summary);
                }

                using (ScriptedTcpServer silent = new ScriptedTcpServer((stream, head, token) => Task.CompletedTask))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("http://127.0.0.1:" + silent.Port + "/").ConfigureAwait(false);
                    AssertEqual(UrlProbeFailureEnum.ConnectionClosed, result.Failure, result.Summary);
                }
            }));

            cases.Add(CaseAsync("tls_untrusted", "https to a server with a self-signed certificate fails at TLS after connecting", TestTags.Negative, async () =>
            {
                using (X509Certificate2 certificate = SelfSigned("localhost"))
                using (ScriptedTcpServer server = new ScriptedTcpServer((stream, head, token) =>
                    ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n", token), certificate))
                {
                    UrlProbeOptions options = new UrlProbeOptions { Resolver = (host, token) => Task.FromResult(new IPAddress[] { IPAddress.Loopback }) };
                    UrlProbeResult result = await UrlProbe.ProbeAsync("https://localhost:" + server.Port + "/", options).ConfigureAwait(false);
                    AssertEqual(UrlProbeFailureEnum.TlsFailed, result.Failure, result.Summary);
                    AssertEqual(UrlProbeStepEnum.Tls, result.FailedStep);
                    AssertEqual(UrlProbeStepStatusEnum.Passed, result.Find(UrlProbeStepEnum.Tcp)!.Status);
                    AssertNull(result.HttpStatusCode, "nothing was sent over an untrusted connection");
                    AssertEqual(0, server.Requests.Count, "no request reached the server");
                }

                using (ScriptedTcpServer plain = new ScriptedTcpServer((stream, head, token) =>
                    ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n", token)))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("https://127.0.0.1:" + plain.Port + "/", new UrlProbeOptions { TimeoutMs = 1000 }).ConfigureAwait(false);
                    AssertEqual(UrlProbeStepEnum.Tls, result.FailedStep, "https to a plain HTTP port fails at TLS: " + result.Summary);
                    AssertFalse(result.Succeeded);
                }
            }));

            cases.Add(CaseAsync("ws_upgrade", "ws:// asks for a WebSocket upgrade, checks the accept key, and closes cleanly", TestTags.Positive, async () =>
            {
                TaskCompletionSource<byte[]> closeFrame = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ScriptedTcpServer server = new ScriptedTcpServer(async (stream, head, token) =>
                {
                    string key = Headers(head)["Sec-WebSocket-Key"];
                    string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + UrlProbe.WebSocketAcceptGuid)));
                    await ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n", token).ConfigureAwait(false);
                    byte[] frame = new byte[8];
                    int read = 0;
                    while (read < frame.Length)
                    {
                        int n = await stream.ReadAsync(frame.AsMemory(read), token).ConfigureAwait(false);
                        if (n == 0) break;
                        read += n;
                    }

                    closeFrame.TrySetResult(frame);
                }))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("ws://127.0.0.1:" + server.Port + "/v1.0/harbor/connect").ConfigureAwait(false);
                    AssertTrue(result.Succeeded, result.Summary);
                    AssertEqual(101, result.HttpStatusCode);
                    AssertNull(result.Find(UrlProbeStepEnum.Http), "ws:// runs the WebSocket stage, not plain HTTP");
                    AssertEqual(UrlProbeStepStatusEnum.Passed, result.Find(UrlProbeStepEnum.WebSocket)!.Status);
                    AssertEqual(UrlProbeStepStatusEnum.Skipped, result.Find(UrlProbeStepEnum.Tls)!.Status);

                    string head = await server.FirstRequest.ConfigureAwait(false);
                    AssertEqual("GET /v1.0/harbor/connect HTTP/1.1", FirstLine(head));
                    Dictionary<string, string> headers = Headers(head);
                    AssertEqual("websocket", headers["Upgrade"]);
                    AssertEqual("13", headers["Sec-WebSocket-Version"]);
                    AssertEqual(16, Convert.FromBase64String(headers["Sec-WebSocket-Key"]).Length, "a 16-byte nonce");
                    AssertNoCredentials(headers);

                    byte[] frame = await closeFrame.Task.ConfigureAwait(false);
                    AssertEqual((byte)0x88, frame[0], "a close frame");
                    AssertEqual((byte)0x82, frame[1], "masked, two-byte payload");
                    AssertEqual(1000, ((frame[6] ^ frame[2]) << 8) | (frame[7] ^ frame[3]), "status 1000, normal closure");
                }
            }));

            cases.Add(CaseAsync("ws_upgrade_refused", "A wrong accept key or a plain 404 rejects the upgrade; 401 is reachable and asks for credentials", TestTags.Negative, async () =>
            {
                using (ScriptedTcpServer wrong = new ScriptedTcpServer((stream, head, token) =>
                    ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: AAAAAAAAAAAAAAAAAAAAAAAAAAA=\r\n\r\n", token)))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("ws://127.0.0.1:" + wrong.Port + "/").ConfigureAwait(false);
                    AssertEqual(UrlProbeFailureEnum.UpgradeRejected, result.Failure, result.Summary);
                    AssertEqual(UrlProbeStepEnum.WebSocket, result.FailedStep);
                }

                using (ScriptedTcpServer missing = new ScriptedTcpServer((stream, head, token) =>
                    ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n", token)))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("ws://127.0.0.1:" + missing.Port + "/wrong/path").ConfigureAwait(false);
                    AssertEqual(UrlProbeFailureEnum.UpgradeRejected, result.Failure, result.Summary);
                    AssertEqual(404, result.HttpStatusCode);
                }

                using (ScriptedTcpServer locked = new ScriptedTcpServer((stream, head, token) =>
                    ScriptedTcpServer.WriteAsync(stream, "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n\r\n", token)))
                {
                    UrlProbeResult result = await UrlProbe.ProbeAsync("ws://127.0.0.1:" + locked.Port + "/v1.0/harbor/connect").ConfigureAwait(false);
                    AssertTrue(result.Succeeded, result.Summary);
                    AssertTrue(result.CredentialsRequired);
                    AssertNoCredentials(Headers(await locked.FirstRequest.ConfigureAwait(false)));
                }
            }));

            cases.Add(Case("options_validated", "The timeout must be between 100 ms and 2 minutes", TestTags.Negative, () =>
            {
                UrlProbeOptions options = new UrlProbeOptions();
                AssertEqual(10000, options.TimeoutMs);
                AssertThrows<ArgumentOutOfRangeException>(() => options.TimeoutMs = 99);
                AssertThrows<ArgumentOutOfRangeException>(() => options.TimeoutMs = 120001);
                options.TimeoutMs = 100;
                AssertEqual(100, options.TimeoutMs);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "URL Probe",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void AssertStatuses(UrlProbeResult result, params UrlProbeStepStatusEnum[] expected)
        {
            AssertEqual(expected.Length, result.Steps.Count, "stage count");
            for (int i = 0; i < expected.Length; i++) AssertEqual(expected[i], result.Steps[i].Status, "stage " + result.Steps[i].Step);
        }

        private static void AssertNoCredentials(Dictionary<string, string> headers)
        {
            foreach (string name in new string[] { "Authorization", "Proxy-Authorization", "Cookie", "x-access-key", "x-secret-key", "x-api-key", "X-Token" })
                AssertFalse(headers.ContainsKey(name), "no " + name + " header");
        }

        private static string FirstLine(string head)
        {
            int end = head.IndexOf("\r\n", StringComparison.Ordinal);
            return end < 0 ? head : head.Substring(0, end);
        }

        private static Dictionary<string, string> Headers(string head)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = head.Split("\r\n");
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            return headers;
        }

        private static X509Certificate2 SelfSigned(string host)
        {
            using (RSA rsa = RSA.Create(2048))
            {
                CertificateRequest request = new CertificateRequest("CN=" + host, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                SubjectAlternativeNameBuilder names = new SubjectAlternativeNameBuilder();
                names.AddDnsName(host);
                request.CertificateExtensions.Add(names.Build());
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                using (X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1)))
                {
                    // Round-trip through PFX so the private key is usable by SslStream on every platform (macOS refuses
                    // an ephemeral key).
                    byte[] pfx = created.Export(X509ContentType.Pfx, "probe");
#if NET9_0_OR_GREATER
                    return X509CertificateLoader.LoadPkcs12(pfx, "probe");
#else
                    return new X509Certificate2(pfx, "probe");
#endif
                }
            }
        }

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
    }
}
