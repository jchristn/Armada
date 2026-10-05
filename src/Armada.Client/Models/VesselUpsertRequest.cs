namespace Armada.Client.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// A vessel create or update body that can carry the write-only GitHub token override (the dashboard's
    /// <c>gitHubTokenOverride</c> field): null leaves the stored override alone, an empty string clears it, and a value
    /// replaces it. Pass it to <see cref="ArmadaClient.CreateVesselAsync"/> or <see cref="ArmadaClient.UpdateVesselAsync"/>.
    /// </summary>
    public class VesselUpsertRequest : Vessel
    {
        #region Public-Members

        /// <summary>
        /// GitHub token override to write: null to keep the stored value, empty to clear it, or the new token.
        /// </summary>
        [JsonPropertyName("gitHubTokenOverride")]
        public new string? GitHubTokenOverrideInput { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselUpsertRequest()
        {
        }

        /// <summary>
        /// A request carrying every field of an existing vessel (a full-record update).
        /// </summary>
        /// <param name="vessel">Vessel to copy.</param>
        /// <returns>Request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="vessel"/> is null.</exception>
        public static VesselUpsertRequest From(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            VesselUpsertRequest? copy = ArmadaJson.Deserialize<VesselUpsertRequest>(ArmadaJson.Serialize(vessel));
            if (copy == null) throw new InvalidOperationException("Could not copy vessel " + vessel.Id + ".");
            copy.GitHubTokenOverrideInput = null;
            return copy;
        }

        #endregion
    }
}
