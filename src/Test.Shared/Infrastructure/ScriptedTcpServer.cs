namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A loopback TCP server on a free test port (<see cref="TestPorts"/>) that runs a script for each connection: it
    /// reads the request head (up to the blank line), records it, and hands the stream to the script, which answers as
    /// the test needs (an HTTP response, a WebSocket upgrade, garbage, or nothing at all). With a certificate it speaks
    /// TLS first. Used by the URL probe tests.
    /// </summary>
    public sealed class ScriptedTcpServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The port it listens on (127.0.0.1).
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Request heads received, in order (each up to and including the blank line).
        /// </summary>
        public ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Completes with the first request head received.
        /// </summary>
        public Task<string> FirstRequest
        {
            get { return _FirstRequest.Task; }
        }

        #endregion

        #region Private-Members

        private readonly TcpListener _Listener;
        private readonly Func<Stream, string, CancellationToken, Task> _Script;
        private readonly X509Certificate2? _Certificate;
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly TaskCompletionSource<string> _FirstRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _AcceptLoop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening.
        /// </summary>
        /// <param name="script">Runs for each connection with the stream, the request head, and a token cancelled when the
        /// server is disposed.</param>
        /// <param name="certificate">Server certificate for TLS, or null for plain TCP.</param>
        public ScriptedTcpServer(Func<Stream, string, CancellationToken, Task> script, X509Certificate2? certificate = null)
        {
            _Script = script ?? throw new ArgumentNullException(nameof(script));
            _Certificate = certificate;
            _Listener = TestPorts.StartOnFreePorts(1, ports =>
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, ports[0]);
                listener.Start();
                return listener;
            });
            Port = ((IPEndPoint)_Listener.LocalEndpoint).Port;
            _AcceptLoop = Task.Run(AcceptLoopAsync);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read an HTTP request head (through the blank line) from a stream.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The head, or what arrived before the peer closed.</returns>
        public static async Task<string> ReadHeadAsync(Stream stream, CancellationToken token)
        {
            byte[] buffer = new byte[16384];
            int count = 0;
            while (count < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(count, buffer.Length - count), token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
                if (Encoding.ASCII.GetString(buffer, 0, count).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }

            return Encoding.ASCII.GetString(buffer, 0, count);
        }

        /// <summary>
        /// Write ASCII text to a stream and flush it.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="text">Text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task WriteAsync(Stream stream, string text, CancellationToken token)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stop listening and cancel running scripts.
        /// </summary>
        public void Dispose()
        {
            _Stop.Cancel();
            _Listener.Stop();
            try
            {
                _AcceptLoop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            _Stop.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task AcceptLoopAsync()
        {
            while (!_Stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _Listener.AcceptTcpClientAsync(_Stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is SocketException || ex is ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                Stream stream = client.GetStream();
                try
                {
                    if (_Certificate != null)
                    {
                        SslStream ssl = new SslStream(stream, false);
                        stream = ssl;
                        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _Certificate }, _Stop.Token).ConfigureAwait(false);
                    }

                    string head = await ReadHeadAsync(stream, _Stop.Token).ConfigureAwait(false);

                    // A peer that closed without sending anything made no request. Over TLS 1.3 the server's handshake
                    // completes before the client checks the certificate, so a client that rejects it arrives here with
                    // nothing read (seen on Linux).
                    if (head.Length == 0) return;

                    Requests.Enqueue(head);
                    _FirstRequest.TrySetResult(head);
                    await _Script(stream, head, _Stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is IOException || ex is SocketException || ex is System.Security.Authentication.AuthenticationException || ex is ObjectDisposedException)
                {
                    // The client went away, TLS was refused, or the server is stopping.
                }
                finally
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
