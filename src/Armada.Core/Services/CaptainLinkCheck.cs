namespace Armada.Core.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Result of the link-episode part of a captain's health check.
    /// </summary>
    internal sealed class CaptainLinkCheck
    {
        /// <summary>
        /// The health check stops here: the captain is held as unreachable, waits on an unconfirmed stop, or was
        /// reconciled on this tick.
        /// </summary>
        internal static readonly CaptainLinkCheck Hold = new CaptainLinkCheck(true, null);

        /// <summary>
        /// The max-runtime and stall checks run.
        /// </summary>
        internal static readonly CaptainLinkCheck Continue = new CaptainLinkCheck(false, null);

        /// <summary>
        /// True when the health check stops here.
        /// </summary>
        internal bool Handled { get; }

        /// <summary>
        /// The purpose of a pending stop the reconnected Harbor confirmed on this tick, for the Admiral to continue
        /// (fail the runaway mission, relaunch the recovery, or fail after exhausted recovery); null otherwise.
        /// </summary>
        internal CaptainStopPurposeEnum? ConfirmedStopPurpose { get; }

        private CaptainLinkCheck(bool handled, CaptainStopPurposeEnum? confirmedStopPurpose)
        {
            Handled = handled;
            ConfirmedStopPurpose = confirmedStopPurpose;
        }

        /// <summary>
        /// A pending stop was confirmed after the Harbor reconnected; the health check stops here.
        /// </summary>
        /// <param name="purpose">Why the stop was requested.</param>
        /// <returns>The check result.</returns>
        internal static CaptainLinkCheck StopConfirmed(CaptainStopPurposeEnum purpose) => new CaptainLinkCheck(true, purpose);
    }
}
