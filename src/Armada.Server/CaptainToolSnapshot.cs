namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// A probed tool inventory for one captain, kept briefly so reading further pages of its tools does not probe the
    /// runtime again.
    /// </summary>
    internal sealed class CaptainToolSnapshot
    {
        #region Internal-Members

        /// <summary>
        /// The described result with every tool (before paging).
        /// </summary>
        internal CaptainToolAccessResult Result { get; set; } = new CaptainToolAccessResult();

        /// <summary>
        /// Every tool, in page order.
        /// </summary>
        internal List<CaptainToolSummary> AllTools { get; set; } = new List<CaptainToolSummary>();

        /// <summary>
        /// The captain's last-update time when probed; a changed captain is probed again.
        /// </summary>
        internal DateTime CaptainLastUpdateUtc { get; set; } = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        internal CaptainToolSnapshot()
        {
        }

        #endregion
    }
}
