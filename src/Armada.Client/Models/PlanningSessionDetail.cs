namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A planning session with its transcript, captain, and vessel.
    /// </summary>
    public class PlanningSessionDetail
    {
        #region Public-Members

        /// <summary>
        /// The session.
        /// </summary>
        public Armada.Core.Models.PlanningSession? Session { get; set; } = null;

        /// <summary>
        /// Transcript messages. Never null.
        /// </summary>
        public List<Armada.Core.Models.PlanningSessionMessage> Messages { get; set; } = new List<Armada.Core.Models.PlanningSessionMessage>();

        /// <summary>
        /// Captain, or null.
        /// </summary>
        public Armada.Core.Models.Captain? Captain { get; set; } = null;

        /// <summary>
        /// Vessel, or null.
        /// </summary>
        public Armada.Core.Models.Vessel? Vessel { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionDetail()
        {
        }

        #endregion
    }
}
