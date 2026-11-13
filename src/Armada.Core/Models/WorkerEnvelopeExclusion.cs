namespace Armada.Core.Models
{
    /// <summary>
    /// A captain the Worker eligibility envelope excluded from a Worker mission, and why.
    /// </summary>
    public class WorkerEnvelopeExclusion
    {
        #region Public-Members

        /// <summary>
        /// Captain identifier.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Captain name.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Why the captain is not eligible, for example "no model profile (the Worker envelope needs one)".
        /// </summary>
        public string Reason { get; set; } = "";

        #endregion
    }
}
