namespace Armada.Tui.Screens.Operations
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a <see cref="BlackBoxFailurePanel"/> reads its entry from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxPanelSourceEnum
    {
        /// <summary>
        /// The mission's current entry (<c>GET /api/v1/missions/{id}/blackbox</c>).
        /// </summary>
        Mission = 0,

        /// <summary>
        /// The voyage's entry (<c>GET /api/v1/voyages/{id}/blackbox</c>).
        /// </summary>
        Voyage = 1,

        /// <summary>
        /// An entry by id (<c>GET /api/v1/blackbox/{id}</c>).
        /// </summary>
        Entry = 2
    }
}
