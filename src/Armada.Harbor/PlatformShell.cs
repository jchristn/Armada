namespace Armada.Harbor
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using Armada.Core.Hosting;

    /// <summary>
    /// Opens folders, files, and URLs and reveals files through the desktop shell (Finder, Explorer, the Linux file
    /// manager). Commands come from <see cref="DesktopShellCommandBuilder"/>; this class only starts them.
    /// </summary>
    public static class PlatformShell
    {
        #region Public-Methods

        /// <summary>
        /// Open a folder, document, or URL with its default handler.
        /// </summary>
        /// <param name="target">Path or URL.</param>
        /// <param name="error">Why the launch failed, or null.</param>
        /// <returns>True when launched.</returns>
        public static bool Open(string target, out string? error)
        {
            return TryBuildAndStart(() => DesktopShellCommandBuilder.BuildOpen(RegistrationContext.DetectPlatform(), target), out error);
        }

        /// <summary>
        /// Show a file or folder selected in the file manager.
        /// </summary>
        /// <param name="path">File or folder.</param>
        /// <param name="error">Why the launch failed, or null.</param>
        /// <returns>True when launched.</returns>
        public static bool Reveal(string path, out string? error)
        {
            return TryBuildAndStart(() => DesktopShellCommandBuilder.BuildReveal(RegistrationContext.DetectPlatform(), path), out error);
        }

        /// <summary>
        /// Open a text file in a text editor, falling back to Notepad on Windows when the file type has no handler.
        /// </summary>
        /// <param name="path">File.</param>
        /// <param name="error">Why the launch failed, or null.</param>
        /// <returns>True when launched.</returns>
        public static bool OpenInTextEditor(string path, out string? error)
        {
            HostPlatformEnum platform = RegistrationContext.DetectPlatform();
            if (TryBuildAndStart(() => DesktopShellCommandBuilder.BuildOpenInTextEditor(platform, path), out error)) return true;

            ShellLaunch? fallback = DesktopShellCommandBuilder.BuildTextEditorFallback(platform, path);
            if (fallback == null) return false;
            return TryStart(fallback, out error);
        }

        #endregion

        #region Private-Methods

        private static bool TryBuildAndStart(Func<ShellLaunch> build, out string? error)
        {
            ShellLaunch launch;
            try
            {
                launch = build();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is PlatformNotSupportedException)
            {
                error = ex.Message;
                return false;
            }

            return TryStart(launch, out error);
        }

        private static bool TryStart(ShellLaunch launch, out string? error)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(launch.FileName) { UseShellExecute = launch.UseShellExecute };
                if (launch.RawArguments != null) psi.Arguments = launch.RawArguments;
                else foreach (string argument in launch.Arguments) psi.ArgumentList.Add(argument);

                using (Process? process = Process.Start(psi))
                {
                }

                error = null;
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is PlatformNotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }

        #endregion
    }
}
