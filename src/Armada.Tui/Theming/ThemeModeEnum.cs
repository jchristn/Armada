namespace Armada.Tui.Theming
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Theme selection.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThemeModeEnum
    {
        /// <summary>
        /// Dark palette.
        /// </summary>
        Dark = 0,

        /// <summary>
        /// Light palette.
        /// </summary>
        Light = 1,

        /// <summary>
        /// High-contrast palette with ASCII borders.
        /// </summary>
        HighContrast = 2,

        /// <summary>
        /// Dark or light chosen from the terminal background (COLORFGBG), dark when unknown.
        /// </summary>
        Auto = 3
    }
}
