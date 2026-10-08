namespace Armada.Core.Metrics.Charts
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The color role of a chart series. Each surface maps a role to its own theme color (Harbor to its light and dark
    /// theme resources, the TUI to its palette), so a chart follows the theme instead of carrying fixed colors.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChartColorEnum
    {
        /// <summary>
        /// The theme accent (blue).
        /// </summary>
        [EnumMember(Value = "Accent")]
        Accent,

        /// <summary>
        /// Success (green).
        /// </summary>
        [EnumMember(Value = "Success")]
        Success,

        /// <summary>
        /// Danger (red).
        /// </summary>
        [EnumMember(Value = "Danger")]
        Danger,

        /// <summary>
        /// Warning (amber).
        /// </summary>
        [EnumMember(Value = "Warning")]
        Warning,

        /// <summary>
        /// Idle or unknown (gray).
        /// </summary>
        [EnumMember(Value = "Idle")]
        Idle,

        /// <summary>
        /// First categorical series color.
        /// </summary>
        [EnumMember(Value = "Series1")]
        Series1,

        /// <summary>
        /// Second categorical series color.
        /// </summary>
        [EnumMember(Value = "Series2")]
        Series2,

        /// <summary>
        /// Third categorical series color.
        /// </summary>
        [EnumMember(Value = "Series3")]
        Series3,

        /// <summary>
        /// Fourth categorical series color.
        /// </summary>
        [EnumMember(Value = "Series4")]
        Series4,

        /// <summary>
        /// Fifth categorical series color.
        /// </summary>
        [EnumMember(Value = "Series5")]
        Series5,

        /// <summary>
        /// Sixth categorical series color.
        /// </summary>
        [EnumMember(Value = "Series6")]
        Series6
    }
}
