namespace Armada.Helm.Infrastructure
{
    /// <summary>
    /// A page of a mission or captain session log from <c>GET /api/v1/missions/{id}/log</c> or
    /// <c>GET /api/v1/captains/{id}/log</c>.
    /// </summary>
    public class RemoteLogResponse
    {
        #region Public-Members

        /// <summary>
        /// The returned lines joined with newlines.
        /// </summary>
        public string Log { get; set; } = "";

        /// <summary>
        /// Number of lines returned.
        /// </summary>
        public int Lines { get; set; } = 0;

        /// <summary>
        /// Total lines in the log.
        /// </summary>
        public int TotalLines { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RemoteLogResponse()
        {
        }

        #endregion
    }
}
