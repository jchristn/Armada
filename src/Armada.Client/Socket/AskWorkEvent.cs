namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of ask.work: tracked work changed.
    /// </summary>
    public class AskWorkEvent
    {
        #region Public-Members

        /// <summary>
        /// Thread id.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Tracked work id.
        /// </summary>
        public string? TrackedWorkId { get; set; } = null;

        /// <summary>
        /// Live snapshot.
        /// </summary>
        public Armada.Core.Models.AskWorkSnapshot? Snapshot { get; set; } = null;

        /// <summary>
        /// Tracked work record.
        /// </summary>
        public Armada.Core.Models.AskTrackedWork? TrackedWork { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkEvent()
        {
        }

        #endregion
    }
}
