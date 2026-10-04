namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;

    /// <summary>
    /// POST /api/v1/missions returns the mission, or { Mission, Warning } when it could not be assigned. This shape
    /// reads either: <see cref="Id"/> from the bare mission, <see cref="Mission"/> from the wrapper.
    /// </summary>
    public sealed class UpgradeMissionCreateResponse
    {
        /// <summary>
        /// Mission ID when the response is the bare mission.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Mission when the response is the wrapper.
        /// </summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>
        /// The mission ID from either shape.
        /// </summary>
        public string? MissionId
        {
            get { return !string.IsNullOrEmpty(Id) ? Id : Mission?.Id; }
        }
    }
}
