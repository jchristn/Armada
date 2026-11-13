namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// An edge of a voyage cascade graph, from cause to effect.
    /// </summary>
    public class BlackBoxCascadeEdge
    {
        #region Public-Members

        /// <summary>
        /// Source mission.
        /// </summary>
        public string FromMissionId { get; set; } = "";

        /// <summary>
        /// Target mission.
        /// </summary>
        public string ToMissionId { get; set; } = "";

        /// <summary>
        /// How they relate.
        /// </summary>
        public BlackBoxCascadeEdgeKindEnum Kind { get; set; } = BlackBoxCascadeEdgeKindEnum.DependsOn;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxCascadeEdge()
        {
        }

        #endregion
    }
}
