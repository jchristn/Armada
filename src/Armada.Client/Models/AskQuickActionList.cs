namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Quick actions from GET /api/v1/ask/quick-actions, which may be a bare array or an object wrapper
    /// (<see cref="AskQuickActionEnvelope"/>). The shape is chosen by the JSON token type in
    /// <see cref="AskQuickActionListConverter"/>, not by inspecting text.
    /// </summary>
    [JsonConverter(typeof(AskQuickActionListConverter))]
    public class AskQuickActionList
    {
        #region Public-Members

        /// <summary>
        /// Actions. Never null.
        /// </summary>
        public List<AskQuickAction> Actions
        {
            get { return _Actions; }
            set { _Actions = value ?? new List<AskQuickAction>(); }
        }

        #endregion

        #region Private-Members

        private List<AskQuickAction> _Actions = new List<AskQuickAction>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskQuickActionList()
        {
        }

        #endregion
    }
}
