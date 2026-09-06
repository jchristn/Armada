namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;

    /// <summary>
    /// A live Harbor link tracked by the connection manager: its identity, the send channel back to it, and
    /// the set of jobs it currently reports running (used for load-aware routing and reconnect rebind).
    /// </summary>
    public class HarborConnection
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier.
        /// </summary>
        public string HarborId { get; }

        /// <summary>
        /// Owning tenant identifier, resolved from the Harbor's credential.
        /// </summary>
        public string? TenantId { get; }

        /// <summary>
        /// Owning user identifier, resolved from the Harbor's credential.
        /// </summary>
        public string? UserId { get; }

        /// <summary>
        /// When the Harbor was last heard from (UTC).
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of jobs the Harbor currently reports running.
        /// </summary>
        public int InFlightJobs
        {
            get
            {
                lock (_JobLock) { return _LiveJobIds.Count; }
            }
        }

        #endregion

        #region Private-Members

        private readonly HarborSendDelegate _Send;
        private readonly HashSet<string> _LiveJobIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _JobLock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="tenantId">Owning tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="send">Channel used to send messages to the Harbor.</param>
        public HarborConnection(string harborId, string? tenantId, string? userId, HarborSendDelegate send)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            HarborId = harborId;
            TenantId = tenantId;
            UserId = userId;
            _Send = send ?? throw new ArgumentNullException(nameof(send));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a message to the Harbor.
        /// </summary>
        /// <param name="message">Message to send.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendAsync(HarborMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return _Send(message, token);
        }

        /// <summary>
        /// Replace the set of live job ids with the authoritative list reported in a heartbeat.
        /// </summary>
        /// <param name="jobIds">Live job identifiers.</param>
        public void SetLiveJobs(IEnumerable<string>? jobIds)
        {
            lock (_JobLock)
            {
                _LiveJobIds.Clear();
                if (jobIds == null) return;
                foreach (string id in jobIds)
                    if (!String.IsNullOrWhiteSpace(id)) _LiveJobIds.Add(id);
            }
        }

        /// <summary>
        /// Whether the Harbor currently reports the given job as running.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <returns>True when the job is live.</returns>
        public bool HasJob(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return false;
            lock (_JobLock) { return _LiveJobIds.Contains(jobId); }
        }

        #endregion
    }
}
