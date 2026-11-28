namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What chose a mission's persona prompt variant (<see cref="PromptVariantEnum"/>), in resolution order.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PromptVariantSourceEnum
    {
        /// <summary>
        /// Nothing chose one; the mission renders Current.
        /// </summary>
        [EnumMember(Value = "Default")]
        Default = 0,

        /// <summary>
        /// The voyage's captain override for the mission's persona.
        /// </summary>
        [EnumMember(Value = "VoyageOverride")]
        VoyageOverride = 1,

        /// <summary>
        /// The assigned captain's <c>promptVariant</c> runtime option.
        /// </summary>
        [EnumMember(Value = "CaptainOption")]
        CaptainOption = 2,

        /// <summary>
        /// The captain's model profile rendering template.
        /// </summary>
        [EnumMember(Value = "ModelProfile")]
        ModelProfile = 3,

        /// <summary>
        /// The captain's served context, against the automatic Compact and Lean thresholds.
        /// </summary>
        [EnumMember(Value = "ServedContext")]
        ServedContext = 4
    }
}
