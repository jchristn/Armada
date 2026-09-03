namespace Armada.Core.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Typed outcome of shared voyage-dispatch validation. Carries either a resolved plan (pipeline id and
    /// whether the request is a bare voyage) or a single typed error. Not a tuple, so both dispatch surfaces
    /// consume it the same way.
    /// </summary>
    public sealed class DispatchValidationResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the request passed validation.
        /// </summary>
        public bool IsValid => Error == DispatchValidationErrorEnum.None;

        /// <summary>
        /// The validation error, or <see cref="DispatchValidationErrorEnum.None"/> when valid.
        /// </summary>
        public DispatchValidationErrorEnum Error { get; }

        /// <summary>
        /// A human-readable message for the error, or null when valid.
        /// </summary>
        public string? Message { get; }

        /// <summary>
        /// The resolved pipeline id (from an explicit id or a name lookup), or null for the vessel/fleet
        /// default. Only meaningful when valid.
        /// </summary>
        public string? ResolvedPipelineId { get; }

        /// <summary>
        /// Whether the request describes a bare voyage (no vessel or no missions) that should be created
        /// without dispatching captains. Only meaningful when valid.
        /// </summary>
        public bool IsBareVoyage { get; }

        #endregion

        #region Constructors-and-Factories

        private DispatchValidationResult(DispatchValidationErrorEnum error, string? message, string? resolvedPipelineId, bool isBareVoyage)
        {
            Error = error;
            Message = message;
            ResolvedPipelineId = resolvedPipelineId;
            IsBareVoyage = isBareVoyage;
        }

        /// <summary>
        /// A valid result carrying the resolved plan.
        /// </summary>
        /// <param name="resolvedPipelineId">Resolved pipeline id, or null for the default.</param>
        /// <param name="isBareVoyage">Whether this is a bare voyage.</param>
        /// <returns>A valid result.</returns>
        public static DispatchValidationResult Valid(string? resolvedPipelineId, bool isBareVoyage)
        {
            return new DispatchValidationResult(DispatchValidationErrorEnum.None, null, resolvedPipelineId, isBareVoyage);
        }

        /// <summary>
        /// A failed result carrying the typed error and a message.
        /// </summary>
        /// <param name="error">The validation error.</param>
        /// <param name="message">A human-readable message.</param>
        /// <returns>A failed result.</returns>
        public static DispatchValidationResult Invalid(DispatchValidationErrorEnum error, string message)
        {
            return new DispatchValidationResult(error, message, null, false);
        }

        #endregion
    }
}
