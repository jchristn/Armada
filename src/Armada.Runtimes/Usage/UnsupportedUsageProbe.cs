namespace Armada.Runtimes.Usage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// A probe for a runtime or source with no verified account-limit interface: it always answers
    /// Unsupported with a stable code and a useful reason, starts nothing, reads no credential and invents no
    /// numbers. Unsupported never quarantines or releases a captain (D6) and is retried only when the
    /// runtime, version or configuration changes.
    /// </summary>
    public class UnsupportedUsageProbe : IUsageProbe
    {
        #region Public-Members

        /// <summary>
        /// Stable error code returned on every capture.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Reason returned on every capture.
        /// </summary>
        public string Reason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="errorCode">Stable error code.</param>
        /// <param name="reason">Human-readable reason.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="errorCode"/> or <paramref name="reason"/> is null or empty.</exception>
        public UnsupportedUsageProbe(string errorCode, string reason)
        {
            if (String.IsNullOrWhiteSpace(errorCode)) throw new ArgumentNullException(nameof(errorCode));
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentNullException(nameof(reason));
            ErrorCode = errorCode;
            Reason = reason;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return Task.FromResult(UsageProbeResult.Unsupported(ErrorCode, Reason));
        }

        #endregion
    }
}
