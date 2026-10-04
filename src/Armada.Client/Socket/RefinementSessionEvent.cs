namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of objective-refinement-session.* events. Fields not carried by a given event are null.
    /// </summary>
    public class RefinementSessionEvent
    {
        #region Public-Members

        /// <summary>
        /// Session id.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Backlog item id.
        /// </summary>
        public string? ObjectiveId { get; set; } = null;

        /// <summary>
        /// Session (changed).
        /// </summary>
        public Armada.Core.Models.ObjectiveRefinementSession? Session { get; set; } = null;

        /// <summary>
        /// Message (message.created, message.updated).
        /// </summary>
        public Armada.Core.Models.ObjectiveRefinementMessage? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RefinementSessionEvent()
        {
        }

        #endregion
    }
}
