namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>
    /// Loopback ports for the in-process test servers. Ports are drawn at random from 20000-31999 (below the ephemeral
    /// ranges of Linux, macOS, and Windows, so outbound client sockets never take them) and never handed out twice in
    /// one process. Random choice matters when several test processes run at once (two frameworks, several worktrees):
    /// a sequential scan from a fixed start sends every process to the same first free port, and the loser of that race
    /// either fails to bind or, worse, sends its requests to another process's server.
    /// 21000-21099 and 25000-25099 are left for manually started local servers.
    /// </summary>
    public static class TestPorts
    {
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

        #endregion
    }
}
