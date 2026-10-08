namespace Armada.Core.Connectivity
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Tests whether a URL is reachable, one stage at a time, so a failure says where it failed: DNS, TCP connect, TLS
    /// (https and wss), then an HTTP GET (http and https) or a WebSocket upgrade (ws and wss). The probe never sends
    /// credentials: no Authorization or access-key headers, no cookies, and a user name or password in the URL is ignored.
    /// A server that answers 401 or 403 is reported as reachable and asking for credentials.
    /// </summary>
    public static class UrlProbe
    {
        #region Public-Members

        /// <summary>
        /// The GUID RFC 6455 appends to Sec-WebSocket-Key to compute Sec-WebSocket-Accept.
        /// </summary>
        public const string WebSocketAcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        #endregion

        #region Private-Members

        private const int _MaxHeaderBytes = 32768;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe a URL.
        /// </summary>
        /// <param name="url">http, https, ws, or wss URL.</param>
        /// <param name="options">Timeout and resolver, or null for the defaults.</param>
        /// <param name="token">Cancels the probe; the result then reports <see cref="UrlProbeFailureEnum.Cancelled"/>.</param>
        /// <returns>The result. Never throws for a network or URL problem.</returns>
        public static async Task<UrlProbeResult> ProbeAsync(string url, UrlProbeOptions? options = null, CancellationToken token = default)
        {
            UrlProbeOptions settings = options ?? new UrlProbeOptions();
            UrlProbeResult result = new UrlProbeResult { Url = url ?? String.Empty };
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch stage = Stopwatch.StartNew();

            if (!TryParse(url, out Uri? uri, out UrlProbeFailureEnum parseFailure, out string parseDetail))
            {
                Fail(result, UrlProbeStepEnum.Parse, parseFailure, parseDetail, stage);
                return Finish(result, total);
            }

            Uri target = uri!;
            bool secure = IsSecure(target);
            bool webSocket = IsWebSocket(target);
            int port = PortOf(target);
            result.CredentialsInUrlIgnored = target.UserInfo.Length > 0;
            Pass(result, UrlProbeStepEnum.Parse, Describe(target, port) + (result.CredentialsInUrlIgnored ? "; the user name and password in the URL are not sent" : ""), stage);

            UrlProbeStepEnum current = UrlProbeStepEnum.Dns;
            Socket? socket = null;
            Stream? stream = null;
            using (CancellationTokenSource timeout = new CancellationTokenSource(settings.TimeoutMs))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
            {
                CancellationToken probeToken = linked.Token;
                try
                {
                    // DNS.
                    stage.Restart();
                    string host = target.IdnHost;
                    IPAddress[] addresses;
                    if (IPAddress.TryParse(host, out IPAddress? literal))
                    {
                        addresses = new IPAddress[] { literal };
                        result.Steps.Add(new UrlProbeStep(UrlProbeStepEnum.Dns, UrlProbeStepStatusEnum.Skipped, host + " is an IP address; no lookup needed", stage.ElapsedMilliseconds));
                    }
                    else
                    {
                        addresses = settings.Resolver != null
                            ? await settings.Resolver(host, probeToken).ConfigureAwait(false)
                            : await Dns.GetHostAddressesAsync(host, probeToken).ConfigureAwait(false);
                        if (addresses == null || addresses.Length == 0)
                        {
                            Fail(result, UrlProbeStepEnum.Dns, UrlProbeFailureEnum.DnsFailed, "The host name " + host + " did not resolve to any address", stage);
                            return Finish(result, total);
                        }

                        Pass(result, UrlProbeStepEnum.Dns, host + " resolved to " + JoinAddresses(addresses), stage);
                    }

                    // TCP: each address in turn until one connects.
                    current = UrlProbeStepEnum.Tcp;
                    stage.Restart();
                    SocketException? lastError = null;
                    IPEndPoint? connectedTo = null;
                    foreach (IPAddress address in addresses)
                    {
                        Socket attempt = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        IPEndPoint endPoint = new IPEndPoint(address, port);
                        try
                        {
                            await attempt.ConnectAsync(endPoint, probeToken).ConfigureAwait(false);
                            socket = attempt;
                            connectedTo = endPoint;
                            break;
                        }
                        catch (SocketException ex)
                        {
                            lastError = ex;
                            attempt.Dispose();
                        }
                        catch
                        {
                            attempt.Dispose();
                            throw;
                        }
                    }

                    if (socket == null || connectedTo == null)
                    {
                        UrlProbeFailureEnum failure = Classify(lastError);
                        Fail(result, UrlProbeStepEnum.Tcp, failure, DescribeConnectFailure(failure, host, port, lastError), stage);
                        return Finish(result, total);
                    }

                    result.RemoteEndPoint = connectedTo.ToString();
                    Pass(result, UrlProbeStepEnum.Tcp, "Connected to " + connectedTo, stage);
                    stream = new NetworkStream(socket, true);
                    socket = null;

                    // TLS.
                    current = UrlProbeStepEnum.Tls;
                    stage.Restart();
                    if (secure)
                    {
                        SslPolicyErrors policyErrors = SslPolicyErrors.None;
                        SslStream ssl = new SslStream(stream, false);
                        stream = ssl;
                        SslClientAuthenticationOptions tls = new SslClientAuthenticationOptions
                        {
                            TargetHost = host,
                            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                            {
                                policyErrors = errors;
                                return errors == SslPolicyErrors.None;
                            }
                        };

                        try
                        {
                            await ssl.AuthenticateAsClientAsync(tls, probeToken).ConfigureAwait(false);
                        }
                        catch (AuthenticationException ex)
                        {
                            Fail(result, UrlProbeStepEnum.Tls, UrlProbeFailureEnum.TlsFailed, DescribeTlsFailure(policyErrors, host, ex), stage);
                            return Finish(result, total);
                        }
                        catch (IOException ex)
                        {
                            Fail(result, UrlProbeStepEnum.Tls, UrlProbeFailureEnum.TlsFailed, "TLS negotiation failed: " + Trim(ex.Message) + ". Is this a TLS port? Try http:// or ws:// if the server does not use TLS", stage);
                            return Finish(result, total);
                        }

                        Pass(result, UrlProbeStepEnum.Tls, DescribeTls(ssl), stage);
                    }
                    else
                    {
                        result.Steps.Add(new UrlProbeStep(UrlProbeStepEnum.Tls, UrlProbeStepStatusEnum.Skipped, "Not used for " + target.Scheme + "://", 0));
                    }

                    // HTTP or WebSocket.
                    current = webSocket ? UrlProbeStepEnum.WebSocket : UrlProbeStepEnum.Http;
                    stage.Restart();
                    string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                    byte[] request = Encoding.ASCII.GetBytes(BuildRequest(target, port, webSocket, key, settings.UserAgent));
                    await stream.WriteAsync(request, probeToken).ConfigureAwait(false);
                    await stream.FlushAsync(probeToken).ConfigureAwait(false);

                    byte[] buffer = new byte[_MaxHeaderBytes];
                    int count = await ReadHeadAsync(stream, buffer, probeToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        Fail(result, current, UrlProbeFailureEnum.ConnectionClosed, "The server accepted the connection but closed it without answering"
                            + (secure ? "" : ". If it expects TLS, use https:// or wss://"), stage);
                        return Finish(result, total);
                    }

                    if (!UrlProbeHttpResponse.TryParse(buffer, count, out UrlProbeHttpResponse? response))
                    {
                        Fail(result, current, UrlProbeFailureEnum.NotHttp, "The server answered, but not with HTTP"
                            + (secure ? "" : ". If it expects TLS, use https:// or wss://"), stage);
                        return Finish(result, total);
                    }

                    result.HttpStatusCode = response!.StatusCode;
                    if (webSocket) await JudgeWebSocketAnswerAsync(result, response, key, stream, stage, probeToken).ConfigureAwait(false);
                    else JudgeHttpAnswer(result, response, stage);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                        Fail(result, current, UrlProbeFailureEnum.Cancelled, "Cancelled", stage);
                    else
                        Fail(result, current, UrlProbeFailureEnum.Timeout, "No answer within " + settings.TimeoutMs.ToString(CultureInfo.InvariantCulture) + " ms (" + Waiting(current) + ")", stage);
                }
                catch (SocketException ex)
                {
                    UrlProbeFailureEnum failure = current == UrlProbeStepEnum.Dns ? UrlProbeFailureEnum.DnsFailed : Classify(ex);
                    Fail(result, current, failure, current == UrlProbeStepEnum.Dns
                        ? "The host name " + target.IdnHost + " did not resolve: " + Trim(ex.Message)
                        : DescribeNetworkFailure(failure, ex), stage);
                }
                catch (IOException ex)
                {
                    UrlProbeFailureEnum failure = ex.InnerException is SocketException inner ? Classify(inner) : UrlProbeFailureEnum.ConnectionClosed;
                    Fail(result, current, failure, "The connection failed: " + Trim(ex.Message), stage);
                }
                finally
                {
                    if (stream != null) await stream.DisposeAsync().ConfigureAwait(false);
                    socket?.Dispose();
                }
            }

            return Finish(result, total);
        }

        /// <summary>
        /// Read a URL for probing: an absolute http, https, ws, or wss URL with a host.
        /// </summary>
        /// <param name="url">Text.</param>
        /// <param name="uri">The URL, when valid.</param>
        /// <param name="failure">Why it is not valid.</param>
        /// <param name="detail">A sentence describing the problem, or empty.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParse(string? url, out Uri? uri, out UrlProbeFailureEnum failure, out string detail)
        {
            uri = null;
            failure = UrlProbeFailureEnum.None;
            detail = String.Empty;
            string text = (url ?? String.Empty).Trim();
            if (text.Length == 0)
            {
                failure = UrlProbeFailureEnum.InvalidUrl;
                detail = "No URL was entered";
                return false;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed) || String.IsNullOrEmpty(parsed.Host))
            {
                failure = UrlProbeFailureEnum.InvalidUrl;
                detail = "\"" + text + "\" is not a complete URL; it needs a scheme and a host, for example http://host:7890";
                return false;
            }

            string scheme = parsed.Scheme.ToLowerInvariant();
            if (scheme != "http" && scheme != "https" && scheme != "ws" && scheme != "wss")
            {
                failure = UrlProbeFailureEnum.UnsupportedScheme;
                detail = parsed.Scheme + " URLs cannot be tested; use http, https, ws, or wss";
                return false;
            }

            uri = parsed;
            return true;
        }

        #endregion

        #region Private-Methods

        private static bool IsSecure(Uri uri)
        {
            return String.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) || String.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWebSocket(Uri uri)
        {
            return String.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) || String.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
        }

        private static int PortOf(Uri uri)
        {
            if (uri.Port > 0) return uri.Port;
            return IsSecure(uri) ? 443 : 80;
        }

        private static string HostHeader(Uri uri, int port)
        {
            string host = uri.HostNameType == UriHostNameType.IPv6 ? "[" + uri.IdnHost.Trim('[', ']') + "]" : uri.IdnHost;
            bool defaultPort = IsSecure(uri) ? port == 443 : port == 80;
            return defaultPort ? host : host + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        private static string Describe(Uri uri, int port)
        {
            string kind = IsWebSocket(uri) ? "WebSocket" : "HTTP";
            return kind + (IsSecure(uri) ? " over TLS" : "") + " to " + uri.IdnHost + " port " + port.ToString(CultureInfo.InvariantCulture) + ", path " + uri.PathAndQuery;
        }

        private static string BuildRequest(Uri uri, int port, bool webSocket, string key, string userAgent)
        {
            // Deliberately no Authorization, cookie, or access-key headers: the probe tests reachability only.
            StringBuilder request = new StringBuilder();
            request.Append("GET ").Append(String.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery).Append(" HTTP/1.1\r\n");
            request.Append("Host: ").Append(HostHeader(uri, port)).Append("\r\n");
            request.Append("User-Agent: ").Append(userAgent).Append("\r\n");
            if (webSocket)
            {
                request.Append("Upgrade: websocket\r\n");
                request.Append("Connection: Upgrade\r\n");
                request.Append("Sec-WebSocket-Version: 13\r\n");
                request.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n");
            }
            else
            {
                request.Append("Accept: */*\r\n");
                request.Append("Connection: close\r\n");
            }

            request.Append("\r\n");
            return request.ToString();
        }

        private static async Task<int> ReadHeadAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            int count = 0;
            while (count < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(count, buffer.Length - count), token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
                if (UrlProbeHttpResponse.FindHeaderEnd(buffer, count) > 0) break;
            }

            return count;
        }

        private static void JudgeHttpAnswer(UrlProbeResult result, UrlProbeHttpResponse response, Stopwatch stage)
        {
            int code = response.StatusCode;
            if (code == 401 || code == 403)
            {
                result.CredentialsRequired = true;
                Pass(result, UrlProbeStepEnum.Http, "HTTP " + response.DescribeStatus() + ": the server is reachable and asks for credentials; none were sent", stage);
                return;
            }

            if (code < 400)
            {
                string location = code >= 300 && response.Headers.TryGetValue("Location", out string? to) ? " (redirects to " + to + ", not followed)" : "";
                Pass(result, UrlProbeStepEnum.Http, "HTTP " + response.DescribeStatus() + location, stage);
                return;
            }

            Fail(result, UrlProbeStepEnum.Http, UrlProbeFailureEnum.HttpError, "The server is reachable but answered HTTP " + response.DescribeStatus()
                + (code == 404 ? "; check the path" : ""), stage);
        }

        private static async Task JudgeWebSocketAnswerAsync(UrlProbeResult result, UrlProbeHttpResponse response, string key, Stream stream, Stopwatch stage, CancellationToken token)
        {
            int code = response.StatusCode;
            if (code == 101)
            {
                string expected = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketAcceptGuid)));
                if (!response.Headers.TryGetValue("Sec-WebSocket-Accept", out string? accept) || !String.Equals(accept, expected, StringComparison.Ordinal))
                {
                    Fail(result, UrlProbeStepEnum.WebSocket, UrlProbeFailureEnum.UpgradeRejected, "The server answered 101 but its Sec-WebSocket-Accept is wrong, so this is not a working WebSocket endpoint", stage);
                    return;
                }

                Pass(result, UrlProbeStepEnum.WebSocket, "WebSocket upgrade accepted (HTTP " + response.DescribeStatus() + "); the probe then closed the connection", stage);
                await SendCloseFrameAsync(stream, token).ConfigureAwait(false);
                return;
            }

            if (code == 401 || code == 403)
            {
                result.CredentialsRequired = true;
                Pass(result, UrlProbeStepEnum.WebSocket, "HTTP " + response.DescribeStatus() + ": the WebSocket endpoint is reachable and asks for credentials before upgrading; none were sent", stage);
                return;
            }

            Fail(result, UrlProbeStepEnum.WebSocket, UrlProbeFailureEnum.UpgradeRejected, "The server is reachable but answered HTTP " + response.DescribeStatus()
                + " instead of upgrading to a WebSocket" + (code == 404 ? "; check the path" : ""), stage);
        }

        private static async Task SendCloseFrameAsync(Stream stream, CancellationToken token)
        {
            // A masked close frame with status 1000 (normal closure), as a client must send.
            byte[] mask = RandomNumberGenerator.GetBytes(4);
            byte[] frame = new byte[] { 0x88, 0x82, mask[0], mask[1], mask[2], mask[3], (byte)(0x03 ^ mask[0]), (byte)(0xE8 ^ mask[1]) };
            try
            {
                await stream.WriteAsync(frame, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The server may already have closed; the upgrade result stands.
            }
            catch (SocketException)
            {
            }
        }

        private static UrlProbeFailureEnum Classify(SocketException? ex)
        {
            if (ex == null) return UrlProbeFailureEnum.NetworkError;
            switch (ex.SocketErrorCode)
            {
                case SocketError.ConnectionRefused:
                    return UrlProbeFailureEnum.ConnectionRefused;
                case SocketError.TimedOut:
                    return UrlProbeFailureEnum.Timeout;
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                case SocketError.HostDown:
                case SocketError.NetworkDown:
                case SocketError.AddressNotAvailable:
                    return UrlProbeFailureEnum.Unreachable;
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                    return UrlProbeFailureEnum.DnsFailed;
                case SocketError.ConnectionReset:
                case SocketError.ConnectionAborted:
                case SocketError.Shutdown:
                    return UrlProbeFailureEnum.ConnectionClosed;
                default:
                    return UrlProbeFailureEnum.NetworkError;
            }
        }

        private static string DescribeConnectFailure(UrlProbeFailureEnum failure, string host, int port, SocketException? ex)
        {
            string where = host + " port " + port.ToString(CultureInfo.InvariantCulture);
            switch (failure)
            {
                case UrlProbeFailureEnum.ConnectionRefused:
                    return "Connection refused by " + where + ": the host is up, but nothing is listening on that port";
                case UrlProbeFailureEnum.Timeout:
                    return "Connecting to " + where + " timed out: a firewall may be dropping the connection, or the host is down";
                case UrlProbeFailureEnum.Unreachable:
                    return where + " cannot be reached from this computer (" + Trim(ex?.Message) + ")";
                default:
                    return "Could not connect to " + where + ": " + Trim(ex?.Message);
            }
        }

        private static string DescribeNetworkFailure(UrlProbeFailureEnum failure, SocketException ex)
        {
            if (failure == UrlProbeFailureEnum.ConnectionClosed) return "The server closed the connection: " + Trim(ex.Message);
            return "Network error: " + Trim(ex.Message);
        }

        private static string DescribeTls(SslStream ssl)
        {
            string protocol = DescribeProtocol(ssl.SslProtocol);
            if (ssl.RemoteCertificate is X509Certificate2 certificate)
            {
                return protocol + "; certificate for " + certificate.GetNameInfo(X509NameType.DnsName, false)
                    + ", issued by " + certificate.GetNameInfo(X509NameType.SimpleName, true)
                    + ", valid until " + certificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return protocol;
        }

        private static string DescribeProtocol(SslProtocols protocol)
        {
            switch (protocol)
            {
                case SslProtocols.Tls12: return "TLS 1.2";
                case SslProtocols.Tls13: return "TLS 1.3";
                default: return "TLS (" + protocol + ")";
            }
        }

        private static string DescribeTlsFailure(SslPolicyErrors errors, string host, AuthenticationException ex)
        {
            if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0) return "The server sent no TLS certificate";
            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return "The server's certificate is not for " + host;
            if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0) return "The server's certificate is not trusted by this computer (self-signed, expired, or from an unknown issuer)";
            return "TLS negotiation failed: " + Trim(ex.Message);
        }

        private static string Waiting(UrlProbeStepEnum step)
        {
            switch (step)
            {
                case UrlProbeStepEnum.Dns: return "resolving the host name";
                case UrlProbeStepEnum.Tcp: return "connecting";
                case UrlProbeStepEnum.Tls: return "negotiating TLS";
                case UrlProbeStepEnum.WebSocket: return "waiting for the WebSocket upgrade";
                default: return "waiting for the HTTP response";
            }
        }

        private static string JoinAddresses(IPAddress[] addresses)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < addresses.Length && i < 4; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(addresses[i]);
            }

            if (addresses.Length > 4) text.Append(" and ").Append(addresses.Length - 4).Append(" more");
            return text.ToString();
        }

        private static string Trim(string? message)
        {
            return (message ?? String.Empty).Trim().TrimEnd('.');
        }

        private static void Pass(UrlProbeResult result, UrlProbeStepEnum step, string detail, Stopwatch stage)
        {
            result.Steps.Add(new UrlProbeStep(step, UrlProbeStepStatusEnum.Passed, detail, stage.ElapsedMilliseconds));
        }

        private static void Fail(UrlProbeResult result, UrlProbeStepEnum step, UrlProbeFailureEnum failure, string detail, Stopwatch stage)
        {
            result.Steps.Add(new UrlProbeStep(step, UrlProbeStepStatusEnum.Failed, detail, stage.ElapsedMilliseconds));
            result.Failure = failure;
            result.FailedStep = step;
            result.Summary = detail;
        }

        private static UrlProbeResult Finish(UrlProbeResult result, Stopwatch total)
        {
            result.ElapsedMs = total.ElapsedMilliseconds;
            if (result.Succeeded)
            {
                UrlProbeStep? last = result.Steps.Count > 0 ? result.Steps[result.Steps.Count - 1] : null;
                result.Summary = "Reachable in " + result.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms"
                    + (last != null ? ": " + last.Detail : "");
            }

            return result;
        }

        #endregion
    }
}
