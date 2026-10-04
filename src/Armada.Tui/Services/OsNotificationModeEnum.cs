namespace Armada.Tui.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How the TUI raises an operating-system notification for things that need the user while the terminal is not
    /// focused.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OsNotificationModeEnum
    {
        /// <summary>
        /// No OS notifications.
        /// </summary>
        Off = 0,

        /// <summary>
        /// OSC 9 escape (iTerm2, Windows Terminal, WezTerm, kitty).
        /// </summary>
        Osc9 = 1,

        /// <summary>
        /// OSC 777 escape (VTE terminals, foot, Ghostty).
        /// </summary>
        Osc777 = 2,

        /// <summary>
        /// The platform notifier: osascript on macOS, notify-send on Linux.
        /// </summary>
        Native = 3
    }
}
