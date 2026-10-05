namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised at startup when the Admiral would listen on a non-loopback hostname with an unsafe configuration (for
    /// example default credentials still in use). The message says how to fix the configuration. Program entry points
    /// catch this type to print the reason and exit non-zero instead of crashing with a stack trace.
    /// </summary>
    public class UnsafeListenerConfigurationException : InvalidOperationException
    {
        /// <summary>
        /// Hostname the Admiral was configured to listen on.
        /// </summary>
        public string Hostname { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Reason and remediation.</param>
        /// <param name="hostname">Configured listener hostname.</param>
        public UnsafeListenerConfigurationException(string message, string hostname)
            : base(message)
        {
            Hostname = hostname ?? String.Empty;
        }
    }
}
