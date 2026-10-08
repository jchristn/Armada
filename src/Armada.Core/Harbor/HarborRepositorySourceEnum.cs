namespace Armada.Core.Harbor
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a Harbor found the repository it serves a vessel from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborRepositorySourceEnum
    {
        /// <summary>
        /// The Harbor has no repository for the vessel and cannot clone one.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// A checkout named for the vessel in the Harbor's settings (Repositories).
        /// </summary>
        [EnumMember(Value = "Mapped")]
        Mapped,

        /// <summary>
        /// A checkout found under one of the Harbor's repository root folders whose remote matches the vessel's URL.
        /// </summary>
        [EnumMember(Value = "Discovered")]
        Discovered,

        /// <summary>
        /// The Harbor's own bare clone of the vessel's repository URL (created when first needed).
        /// </summary>
        [EnumMember(Value = "Clone")]
        Clone
    }
}
