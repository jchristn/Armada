namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// Path handling for the platform a registration targets, independent of the platform the code runs on.
    /// Unit files, plists, and service commands describe paths on the target machine, so they must not go through
    /// <see cref="System.IO.Path"/>, which applies the host's rules (on a Windows host it turns
    /// <c>/opt/armada/armada-server</c> into <c>\opt\armada</c>; on a Linux host it does not split <c>C:\Armada\a.exe</c>).
    /// </summary>
    public static class TargetPath
    {
        #region Public-Methods

        /// <summary>
        /// Directory part of a path on the target platform: POSIX rules (only <c>/</c> separates) for Linux and macOS,
        /// Windows rules (<c>\</c> and <c>/</c> separate, drive roots kept) for Windows.
        /// </summary>
        /// <param name="path">Path on the target platform.</param>
        /// <param name="platform">Target platform.</param>
        /// <returns>The directory, or null when the path has no directory part.</returns>
        public static string? GetDirectoryName(string? path, HostPlatformEnum platform)
        {
            if (String.IsNullOrEmpty(path)) return null;
            return platform == HostPlatformEnum.Windows ? GetWindowsDirectoryName(path) : GetPosixDirectoryName(path);
        }

        /// <summary>
        /// Root directory used when a path has no directory part.
        /// </summary>
        /// <param name="platform">Target platform.</param>
        /// <returns><c>C:\</c> for Windows, <c>/</c> otherwise.</returns>
        public static string DefaultRoot(HostPlatformEnum platform)
        {
            return platform == HostPlatformEnum.Windows ? "C:\\" : "/";
        }

        #endregion

        #region Private-Methods

        private static string? GetPosixDirectoryName(string path)
        {
            string trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
            if (trimmed.Length == 0) return null;
            int slash = trimmed.LastIndexOf('/');
            if (slash < 0) return null;
            if (slash == 0) return trimmed.Length == 1 ? null : "/";
            string directory = trimmed.Substring(0, slash).TrimEnd('/');
            return directory.Length == 0 ? "/" : directory;
        }

        private static string? GetWindowsDirectoryName(string path)
        {
            char[] separators = new char[] { '\\', '/' };
            string trimmed = path.TrimEnd(separators);
            if (trimmed.Length == 0) return null;
            int slash = trimmed.LastIndexOfAny(separators);
            if (slash < 0) return null;
            bool driveRoot = slash == 2 && trimmed.Length > 2 && trimmed[1] == ':';
            if (driveRoot) return trimmed.Substring(0, 3);
            string directory = trimmed.Substring(0, slash).TrimEnd(separators);
            if (directory.Length == 2 && directory[1] == ':') return directory + "\\";
            return directory.Length == 0 ? null : directory;
        }

        #endregion
    }
}
