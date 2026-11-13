namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of get_model_scorecard.
    /// </summary>
    public class ModelScorecardArgs
    {
        /// <summary>
        /// Model profile ID (mdl_ prefix).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Persona, for example Worker or Judge.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Only stages on this vessel, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Window length in days (1-365), or null for 30.
        /// </summary>
        public int? WindowDays { get; set; } = null;

        /// <summary>
        /// Window length in missions (1-500), or null for 50.
        /// </summary>
        public int? WindowMissions { get; set; } = null;
    }
}
