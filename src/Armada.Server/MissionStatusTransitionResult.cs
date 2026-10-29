namespace Armada.Server
{
    using Armada.Core.Models;

    /// <summary>
    /// The stored mission after a manual status change, or the reason the change was refused.
    /// </summary>
    public sealed class MissionStatusTransitionResult
    {
        #region Public-Members

        /// <summary>
        /// Mission row after the change. Null when <see cref="Error"/> is not <see cref="MissionStatusTransitionErrorEnum.None"/>.
        /// </summary>
        public Mission? Mission { get; set; }

        /// <summary>
        /// Refusal reason. <see cref="MissionStatusTransitionErrorEnum.None"/> when <see cref="Mission"/> is set.
        /// </summary>
        public MissionStatusTransitionErrorEnum Error { get; set; } = MissionStatusTransitionErrorEnum.None;

        /// <summary>
        /// Caller-facing refusal text. Null when the change was stored.
        /// </summary>
        public string? Message { get; set; }

        #endregion
    }
}
