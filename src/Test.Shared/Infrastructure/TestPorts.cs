namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    /// <summary>
    /// Loopback ports for the in-process test servers. Ports are drawn at random from 20000-31999 (below the ephemeral
    /// ranges of Linux, macOS, and Windows, so outbound client sockets never take them) and never handed out twice in
    /// one process. Random choice matters when several test processes run at once (two frameworks, several worktrees):
    /// a sequential scan from a fixed start sends every process to the same first free port, and the loser of that race
    /// either fails to bind or, worse, sends its requests to another process's server.
    /// 21000-21099 and 25000-25099 are left for manually started local servers.
    /// <para>
    /// <see cref="Reserve"/> only checks that a port was free and then releases it, so anything can bind the port
    /// before the caller's listener does (another process, or a socket of this one that does not come from here). A
    /// listener that binds a port itself must start through <see cref="StartOnFreePorts{T}"/> or
    /// <see cref="StartOnFreePortsAsync{T}"/>, which bind on a fresh port when the chosen one was taken in that gap.
    /// </para>
    /// </summary>
    public static class TestPorts
    {
        #region Public-Members

        /// <summary>
        /// Most starts <see cref="StartOnFreePorts{T}"/> and <see cref="StartOnFreePortsAsync{T}"/> attempt, each on
        /// newly reserved ports, before giving up.
        /// </summary>
        public const int MaxStartAttempts = 10;

        #endregion

        #region Private-Members

        private const int _RangeStart = 20000;
        private const int _RangeEnd = 32000;
        private static readonly object _RandomLock = new object();
        private static readonly Random _Random = new Random();
        private static readonly ConcurrentDictionary<int, byte> _HandedOut = new ConcurrentDictionary<int, byte>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reserve distinct ports that were free on loopback when checked. All are held open together while choosing,
        /// so the ports returned always differ from each other.
        /// </summary>
        /// <param name="count">Number of ports.</param>
        /// <returns>The ports.</returns>
        public static int[] Reserve(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            List<TcpListener> held = new List<TcpListener>();
            try
            {
                int tries = 0;
                while (held.Count < count)
                {
                    if (++tries > 500) throw new InvalidOperationException("could not reserve " + count + " free loopback ports in " + _RangeStart + "-" + _RangeEnd);
                    int candidate;
                    lock (_RandomLock) { candidate = _Random.Next(_RangeStart, _RangeEnd); }
                    if (candidate >= 21000 && candidate < 21100) continue;
                    if (candidate >= 25000 && candidate < 25100) continue;
                    if (!_HandedOut.TryAdd(candidate, 0)) continue;
                    TcpListener listener = new TcpListener(IPAddress.Loopback, candidate);
                    try
                    {
                        listener.Start();
                    }
                    catch (SocketException)
                    {
                        continue;
                    }
                    held.Add(listener);
                }

                int[] ports = new int[count];
                for (int i = 0; i < count; i++) ports[i] = ((IPEndPoint)held[i].LocalEndpoint).Port;
                return ports;
            }
            finally
            {
                foreach (TcpListener listener in held) listener.Stop();
            }
        }

        /// <summary>
        /// Start a listener on freshly reserved ports. When the start fails because a chosen port was bound by someone
        /// else after it was reserved, the start is repeated on newly reserved ports; any other failure is thrown. The
        /// retry covers only port allocation: <paramref name="start"/> must release whatever it bound before throwing.
        /// </summary>
        /// <typeparam name="T">What the start returns.</typeparam>
        /// <param name="count">Number of ports the listener binds.</param>
        /// <param name="start">Binds the ports and returns the started listener; throws when a bind fails.</param>
        /// <param name="beforeStart">Optional hook run with the reserved ports just before each start (tests use it to
        /// take a port in the gap between reservation and bind).</param>
        /// <returns>What <paramref name="start"/> returned.</returns>
        public static T StartOnFreePorts<T>(int count, Func<int[], T> start, Action<int[]>? beforeStart = null)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            List<string> taken = new List<string>();
            for (int attempt = 1; ; attempt++)
            {
                int[] ports = Reserve(count);
                beforeStart?.Invoke(ports);
                try
                {
                    return start(ports);
                }
                catch (Exception ex) when (IsAddressInUse(ex) && attempt < MaxStartAttempts)
                {
                    taken.Add(String.Join(",", ports) + ": " + ex.Message);
                }
                catch (Exception ex) when (IsAddressInUse(ex))
                {
                    taken.Add(String.Join(",", ports) + ": " + ex.Message);
                    throw new InvalidOperationException("every reserved port was taken before the listener bound it (" + String.Join("; ", taken) + ")", ex);
                }
            }
        }

        /// <summary>
        /// Start a listener on freshly reserved ports. When the start fails because a chosen port was bound by someone
        /// else after it was reserved, the start is repeated on newly reserved ports; any other failure is thrown. The
        /// retry covers only port allocation: <paramref name="start"/> must release whatever it bound before throwing.
        /// </summary>
        /// <typeparam name="T">What the start returns.</typeparam>
        /// <param name="count">Number of ports the listener binds.</param>
        /// <param name="start">Binds the ports and returns the started listener; throws when a bind fails.</param>
        /// <param name="beforeStart">Optional hook run with the reserved ports just before each start (tests use it to
        /// take a port in the gap between reservation and bind).</param>
        /// <returns>What <paramref name="start"/> returned.</returns>
        public static async Task<T> StartOnFreePortsAsync<T>(int count, Func<int[], Task<T>> start, Action<int[]>? beforeStart = null)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            List<string> taken = new List<string>();
            for (int attempt = 1; ; attempt++)
            {
                int[] ports = Reserve(count);
                beforeStart?.Invoke(ports);
                try
                {
                    return await start(ports).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsAddressInUse(ex) && attempt < MaxStartAttempts)
                {
                    taken.Add(String.Join(",", ports) + ": " + ex.Message);
                }
                catch (Exception ex) when (IsAddressInUse(ex))
                {
                    taken.Add(String.Join(",", ports) + ": " + ex.Message);
                    throw new InvalidOperationException("every reserved port was taken before the listener bound it (" + String.Join("; ", taken) + ")", ex);
                }
            }
        }

        /// <summary>
        /// Start an <see cref="HttpListener"/> on a fresh loopback port (see <see cref="StartOnFreePorts{T}"/>).
        /// </summary>
        /// <param name="prefixes">The listener prefixes for a port, for example http://127.0.0.1:{port}/.</param>
        /// <param name="port">The port the listener bound.</param>
        /// <param name="beforeStart">Optional hook run with the reserved port just before each start.</param>
        /// <returns>The started listener; the caller stops and closes it.</returns>
        public static HttpListener StartHttpListener(Func<int, IEnumerable<string>> prefixes, out int port, Action<int>? beforeStart = null)
        {
            if (prefixes == null) throw new ArgumentNullException(nameof(prefixes));
            int bound = 0;
            HttpListener started = StartOnFreePorts(1, ports =>
            {
                HttpListener listener = new HttpListener();
                try
                {
                    foreach (string prefix in prefixes(ports[0])) listener.Prefixes.Add(prefix);
                    listener.Start();
                    bound = ports[0];
                    return listener;
                }
                catch
                {
                    listener.Close();
                    throw;
                }
            }, beforeStart == null ? null : ports => beforeStart(ports[0]));
            port = bound;
            return started;
        }

        /// <summary>
        /// Whether an exception (or one it wraps) reports that a listener could not bind because the address is
        /// already in use.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>True for an address-in-use bind failure.</returns>
        public static bool IsAddressInUse(Exception? ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse) return true;
                if (current is HttpListenerException listener && IsAddressInUseCode(listener.ErrorCode)) return true;
                if (current is AggregateException aggregate)
                {
                    foreach (Exception inner in aggregate.InnerExceptions)
                    {
                        if (IsAddressInUse(inner)) return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region Private-Methods

        private static bool IsAddressInUseCode(int code)
        {
            // The managed HttpListener (Linux, macOS) reports the platform errno: EADDRINUSE is 98 on Linux and 48 on
            // macOS. http.sys (Windows) reports ERROR_ALREADY_EXISTS or ERROR_SHARING_VIOLATION for a taken port.
            if (code == (int)SocketError.AddressAlreadyInUse) return true;
            if (OperatingSystem.IsWindows()) return code == 183 || code == 32;
            if (OperatingSystem.IsLinux()) return code == 98;
            return code == 48;
        }

        #endregion
    }
}
