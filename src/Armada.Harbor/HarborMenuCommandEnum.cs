namespace Armada.Harbor
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Every command the Harbor menus offer. The macOS menu bar, the in-window menu on Windows and Linux, and the tray
    /// menu are all built from these, so each surface offers the same commands with the same enabled state.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborMenuCommandEnum
    {
        /// <summary>
        /// Show and focus the Harbor window.
        /// </summary>
        ShowWindow,

        /// <summary>
        /// Show the About window.
        /// </summary>
        About,

        /// <summary>
        /// Open settings (Harbor and Armada).
        /// </summary>
        Settings,

        /// <summary>
        /// Start the link loop.
        /// </summary>
        Connect,

        /// <summary>
        /// Stop the link loop.
        /// </summary>
        Disconnect,

        /// <summary>
        /// Drop the current link and dial again.
        /// </summary>
        Reconnect,

        /// <summary>
        /// Edit this Harbor's settings.
        /// </summary>
        HarborSettings,

        /// <summary>
        /// Copy the Harbor identifier.
        /// </summary>
        CopyHarborId,

        /// <summary>
        /// Copy the MCP URL the Admiral advertised.
        /// </summary>
        CopyMcpUrl,

        /// <summary>
        /// Open the Harbor settings folder (~/.armada-harbor).
        /// </summary>
        OpenHarborFolder,

        /// <summary>
        /// Show Admiral and Harbor status.
        /// </summary>
        Status,

        /// <summary>
        /// Edit the Admiral's settings.json.
        /// </summary>
        ArmadaSettings,

        /// <summary>
        /// Edit the terminal UI's tui.json.
        /// </summary>
        TuiSettings,

        /// <summary>
        /// List the Admiral's database backups.
        /// </summary>
        Backups,

        /// <summary>
        /// Open the Armada data directory (~/.armada).
        /// </summary>
        OpenDataFolder,

        /// <summary>
        /// Open the dashboard in the browser.
        /// </summary>
        OpenDashboard,

        /// <summary>
        /// Open the Admiral's current log file.
        /// </summary>
        OpenAdmiralLog,

        /// <summary>
        /// Open Harbor's own log file.
        /// </summary>
        OpenHarborLog,

        /// <summary>
        /// Open a mission's log by mission identifier.
        /// </summary>
        MissionLog,

        /// <summary>
        /// Browse every Armada log.
        /// </summary>
        LogBrowser,

        /// <summary>
        /// Open the Admiral's log directory.
        /// </summary>
        OpenLogsFolder,

        /// <summary>
        /// Minimize the Harbor window.
        /// </summary>
        MinimizeWindow,

        /// <summary>
        /// Close (hide) the Harbor window; Harbor keeps running in the tray.
        /// </summary>
        CloseWindow,

        /// <summary>
        /// Open the Armada documentation.
        /// </summary>
        Documentation,

        /// <summary>
        /// Copy versions, paths, link state, and recent errors for a bug report.
        /// </summary>
        CopyDiagnostics,

        /// <summary>
        /// Quit Harbor.
        /// </summary>
        Quit
    }
}
