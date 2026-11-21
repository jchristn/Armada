namespace Armada.Core.Hosting
{
    using System;
    using System.IO;

    /// <summary>
    /// Expands a leading <c>~</c> in a path to a home directory, the way a shell does for the current user: <c>~</c>
    /// alone, <c>~/...</c>, and <c>~\...</c>. Other forms (<c>~user</c>, a <c>~</c> later in the path) are returned
    /// unchanged. Used by the Admiral and by Harbor so a folder such as <c>~/Code</c> means the home directory of the
    /// machine that runs the captain. Stateless and thread safe.
    /// </summary>
    public static class HomePath
    {
        #region Public-Methods

        /// <summary>
        /// Expand a leading <c>~</c> to the current user's home directory
        /// (<see cref="Environment.SpecialFolder.UserProfile"/>).
        /// </summary>
        /// <param name="path">Path, or null.</param>
        /// <returns>The expanded path; null or empty input is returned as is.</returns>
        public static string? Expand(string? path)
        {
            return Expand(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        /// <summary>
        /// Expand a leading <c>~</c> to the given home directory.
        /// </summary>
        /// <param name="path">Path, or null.</param>
        /// <param name="homeDirectory">Home directory to substitute. When null or empty the path is returned unchanged.</param>
        /// <returns>The expanded path; null or empty input is returned as is.</returns>
        public static string? Expand(string? path, string? homeDirectory)
        {
            if (String.IsNullOrEmpty(path)) return path;
            if (!StartsWithHome(path)) return path;
            if (String.IsNullOrEmpty(homeDirectory)) return path;

            if (path.Length == 1) return homeDirectory;
            string rest = path.Substring(2);
            if (rest.Length == 0) return homeDirectory;
            return Path.Combine(homeDirectory, rest);
        }

        /// <summary>
        /// Whether a path starts with a home-directory reference this class expands: <c>~</c> alone, <c>~/</c>, or
        /// <c>~\</c>.
        /// </summary>
        /// <param name="path">Path, or null.</param>
        /// <returns>True when <see cref="Expand(string?)"/> would substitute the home directory.</returns>
        public static bool StartsWithHome(string? path)
        {
            if (String.IsNullOrEmpty(path) || path[0] != '~') return false;
            return path.Length == 1 || path[1] == '/' || path[1] == '\\';
        }

        #endregion
    }
}
