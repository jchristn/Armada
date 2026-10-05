namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised at startup when a listener cannot bind its address and port (for example the port is already in use).
    /// Carries the listener name, host, and port so callers do not parse the message.
    /// </summary>
    public class ListenerBindException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Name of the listener that failed, for example MCP.
        /// </summary>
        public string Listener { get; }

        /// <summary>
        /// Host name or address the listener tried to bind.
        /// </summary>
        public string Hostname { get; }

        /// <summary>
        /// Port the listener tried to bind.
        /// </summary>
        public int Port { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="listener">Name of the listener that failed.</param>
        /// <param name="hostname">Host name or address.</param>
        /// <param name="port">Port.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="inner">Underlying exception, or null.</param>
        public ListenerBindException(string listener, string hostname, int port, string message, Exception? inner = null)
            : base(message, inner)
        {
            Listener = listener ?? String.Empty;
            Hostname = hostname ?? String.Empty;
            Port = port;
        }

        #endregion
    }
}
