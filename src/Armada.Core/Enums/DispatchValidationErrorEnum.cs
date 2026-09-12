namespace Armada.Core.Enums
{
    /// <summary>
    /// Reason a voyage-dispatch request failed shared validation. Both the REST and MCP dispatch surfaces
    /// map these to their own error shapes (HTTP status codes vs. structured error objects), but they agree
    /// on what is valid.
    /// </summary>
    public enum DispatchValidationErrorEnum
    {
        /// <summary>
        /// The request is valid.
        /// </summary>
        None,

        /// <summary>
        /// A linked objective id was supplied but no such objective exists.
        /// </summary>
        ObjectiveNotFound,

        /// <summary>
        /// A pipeline name was supplied but no pipeline with that name exists.
        /// </summary>
        PipelineNotFound,

        /// <summary>
        /// A full dispatch (not a bare voyage) requires a target vessel.
        /// </summary>
        MissingVessel,

        /// <summary>
        /// A full dispatch (not a bare voyage) requires at least one mission.
        /// </summary>
        NoMissions
    }
}
