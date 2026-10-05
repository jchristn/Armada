namespace Armada.Client.Models
{
    /// <summary>
    /// The reply of <c>POST /api/v1/captains/{id}/unquarantine</c> when the captain was not quarantined:
    /// <c>{ Status: "not_quarantined", CaptainId }</c> (otherwise the server returns the captain).
    /// </summary>
    public class UnquarantineReply
    {
        #region Public-Members

        /// <summary>
        /// "not_quarantined", or null when the reply is the captain.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Captain id.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        #endregion
    }
}
