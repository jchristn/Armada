namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Richer per-runtime account usage capture capability, beside the boolean
    /// <see cref="AgentRuntimeCapabilities.SupportsUsageCapture"/>: what kind of capture the runtime has,
    /// and why it does not when it does not. A supported capability does not promise credentials or quota
    /// numbers; it only says a verified source exists, and an unusable observation still answers
    /// Unsupported, AuthMissing or Failed. The reason is operator-facing text, so it stays ASCII and
    /// never contains credentials.
    /// </summary>
    public class UsageCaptureCapability
    {
        #region Public-Members

        /// <summary>
        /// Runtime the descriptor is about.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; }

        /// <summary>
        /// True when the runtime has a usable capture source at all.
        /// </summary>
        public bool Supported { get; }

        /// <summary>
        /// What kind of capture the runtime uses: an active read of a verified account-limit source, a
        /// passive harvest from existing traffic, or an operator-configured estimate.
        /// </summary>
        public UsageCaptureKindEnum Kind { get; }

        /// <summary>
        /// Operator-facing reason a capture is not supported, or null when it is. Names the adapter
        /// limitation, never a credential.
        /// </summary>
        public string? Reason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a supported capability.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="kind">Capture kind.</param>
        public UsageCaptureCapability(AgentRuntimeEnum runtime, UsageCaptureKindEnum kind)
        {
            Runtime = runtime;
            Supported = true;
            Kind = kind;
            Reason = null;
        }

        /// <summary>
        /// Instantiate an unsupported capability with its reason.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="reason">Reason the runtime has no usable capture source.</param>
        public UsageCaptureCapability(AgentRuntimeEnum runtime, string reason)
        {
            Runtime = runtime;
            Supported = false;
            Kind = UsageCaptureKindEnum.Unsupported;
            Reason = String.IsNullOrWhiteSpace(reason) ? "No verified account limit source." : reason;
        }

        #endregion
    }
}