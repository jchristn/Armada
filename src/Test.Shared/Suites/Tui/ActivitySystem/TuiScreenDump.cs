namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.IO;

    /// <summary>
    /// Writes headless frames to <c>ARMADA_TUI_DUMP</c> (a directory) when that variable is set, so screen layouts
    /// can be reviewed as text. Does nothing otherwise.
    /// </summary>
    internal static class TuiScreenDump
    {
        /// <summary>
        /// Write a frame.
        /// </summary>
        /// <param name="name">File name stem.</param>
        /// <param name="frame">Frame text.</param>
        public static void Write(string name, string frame)
        {
            string? dir = Environment.GetEnvironmentVariable("ARMADA_TUI_DUMP");
            if (String.IsNullOrWhiteSpace(dir)) return;
            Directory.CreateDirectory(dir!);
            File.WriteAllText(Path.Combine(dir!, name + ".txt"), frame ?? "");
        }
    }
}
