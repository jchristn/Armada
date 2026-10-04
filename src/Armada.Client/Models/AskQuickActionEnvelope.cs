namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Wrapper shapes the quick-action catalog may arrive in.
    /// </summary>
    public class AskQuickActionEnvelope
    {
        #region Public-Members

        /// <summary>
        /// QuickActions wrapper, or null.
        /// </summary>
        public List<Armada.Core.Models.AskQuickAction>? QuickActions { get; set; } = null;

        /// <summary>
        /// Actions wrapper, or null.
        /// </summary>
        public List<Armada.Core.Models.AskQuickAction>? Actions { get; set; } = null;

        /// <summary>
        /// Objects wrapper, or null.
        /// </summary>
        public List<Armada.Core.Models.AskQuickAction>? Objects { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskQuickActionEnvelope()
        {
        }

        #endregion
    }
}
