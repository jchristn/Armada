namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Divergence filter for vessel health enumeration, relative to the default branch.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselDivergenceFilterEnum
    {
        /// <summary>
        /// Ahead of the default branch and not behind.
        /// </summary>
        Ahead,

        /// <summary>
        /// Behind the default branch and not ahead.
        /// </summary>
        Behind,

        /// <summary>
        /// Both ahead of and behind the default branch.
        /// </summary>
        Diverged,

        /// <summary>
        /// Neither ahead nor behind (evaluated vessels only).
        /// </summary>
        Even
    }
}
