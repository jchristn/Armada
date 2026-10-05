namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response of <c>GET /api/v1/voyages/{id}</c>: the voyage and its missions (with playbook snapshots).
    /// </summary>
    public class VoyageDetail
    {
        #region Public-Members

        /// <summary>
        /// Voyage.
        /// </summary>
        public Armada.Core.Models.Voyage? Voyage { get; set; } = null;

        /// <summary>
        /// Missions of the voyage.
        /// </summary>
        public List<Armada.Core.Models.Mission> Missions { get; set; } = new List<Armada.Core.Models.Mission>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VoyageDetail()
        {
        }

        #endregion
    }
}
