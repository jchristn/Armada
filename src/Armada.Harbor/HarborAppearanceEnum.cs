namespace Armada.Harbor
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Color scheme of the Harbor window.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborAppearanceEnum
    {
        /// <summary>
        /// Follow the operating system's light or dark setting.
        /// </summary>
        System,

        /// <summary>
        /// Always use the light scheme.
        /// </summary>
        Light,

        /// <summary>
        /// Always use the dark scheme.
        /// </summary>
        Dark
    }
}
