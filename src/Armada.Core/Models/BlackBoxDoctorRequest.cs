namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Query of the configuration doctor (GET /api/v1/vessels/{id}/blackbox-doctor and its enumerate twin): paging plus
    /// the window.
    /// </summary>
    public class BlackBoxDoctorRequest : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Window in days (1 to 365), or null for 30.
        /// </summary>
        public int? WindowDays { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDoctorRequest()
        {
        }

        #endregion
    }
}
