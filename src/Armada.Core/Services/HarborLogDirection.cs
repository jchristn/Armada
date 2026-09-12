namespace Armada.Core.Services
{
    /// <summary>
    /// The direction of a Harbor link log entry relative to the Harbor.
    /// </summary>
    public enum HarborLogDirection
    {
        /// <summary>
        /// A local event on the Harbor (not a message over the link).
        /// </summary>
        Info,

        /// <summary>
        /// A message received from the Admiral (work issued to the Harbor).
        /// </summary>
        In,

        /// <summary>
        /// A message sent to the Admiral (status propagating back).
        /// </summary>
        Out
    }
}
