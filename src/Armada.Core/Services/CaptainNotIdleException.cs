namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Raised when an operation needs an idle captain (planning, objective refinement, fleet categorization) and the
    /// chosen captain is busy. Carries the captain and its state so callers do not parse the message. REST maps it to
    /// 409 Conflict like any <see cref="InvalidOperationException"/>.
    /// </summary>
    public class CaptainNotIdleException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Captain id.
        /// </summary>
        public string CaptainId { get; }

        /// <summary>
        /// The captain's state when the operation was refused, or null when unknown.
        /// </summary>
        public CaptainStateEnum? State { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="captainId">Captain id.</param>
        /// <param name="state">The captain's state, or null when unknown.</param>
        /// <param name="message">Human-readable message.</param>
        public CaptainNotIdleException(string captainId, CaptainStateEnum? state, string message)
            : base(message)
        {
            CaptainId = captainId ?? String.Empty;
            State = state;
        }

        #endregion
    }
}
