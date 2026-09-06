namespace Armada.Core.Harbor
{
    using System.Collections.Generic;

    /// <summary>
    /// Harbor-to-server periodic liveness message. Also carries the set of jobs the Harbor still has
    /// running, so the Admiral can rebind them after a reconnect and reconcile any it thought were lost.
    /// </summary>
    public class HarborHeartbeat : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job identifiers currently alive on the Harbor.
        /// </summary>
        public List<string> LiveJobIds { get; set; } = new List<string>();

        #endregion
    }
}
