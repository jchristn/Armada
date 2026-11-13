namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Payload of the mission.landing_deferred event: a LocalMerge landing refused because the target checkout had
    /// tracked changes. The landing-integrity sweep retries such a mission once the checkout is clean, until the deadline.
    /// </summary>
    public class LandingDeferral
    {
        #region Public-Members

        /// <summary>
        /// The checkout that was dirty.
        /// </summary>
        public string CheckoutPath { get; set; } = "";

        /// <summary>
        /// Harbor that hosts the checkout, or null for the Admiral's host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Mission branch waiting to land.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Modified tracked paths git reported.
        /// </summary>
        public List<string> ModifiedPaths { get; set; } = new List<string>();

        /// <summary>
        /// When this landing was first deferred on a dirty checkout.
        /// </summary>
        public DateTime FirstDeferredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When automatic retries stop.
        /// </summary>
        public DateTime DeadlineUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
