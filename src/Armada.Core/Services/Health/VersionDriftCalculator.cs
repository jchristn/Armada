namespace Armada.Core.Services.Health
{
    using System;
    using System.Globalization;
    using Armada.Core.Enums;

    /// <summary>
    /// Computes semantic-version drift between a current and a latest version. Versions are compared as
    /// major.minor.patch after stripping any -prerelease and +build suffix; missing or non-numeric components count as
    /// 0. The first component where the latest version is higher decides the drift; a lower or equal latest version is
    /// None. Stateless and thread-safe.
    /// </summary>
    public static class VersionDriftCalculator
    {
        #region Public-Methods

        /// <summary>
        /// Compute the drift from <paramref name="current"/> to <paramref name="latest"/>.
        /// </summary>
        /// <param name="current">Current version, or null.</param>
        /// <param name="latest">Latest version, or null.</param>
        /// <returns>The drift; None when either version is missing or the latest is not newer.</returns>
        public static DependencyDriftEnum Compute(string? current, string? latest)
        {
            if (String.IsNullOrWhiteSpace(current) || String.IsNullOrWhiteSpace(latest)) return DependencyDriftEnum.None;
            if (String.Equals(current.Trim(), latest.Trim(), StringComparison.OrdinalIgnoreCase)) return DependencyDriftEnum.None;

            int[] c = Parse(current);
            int[] l = Parse(latest);

            if (l[0] > c[0]) return DependencyDriftEnum.Major;
            if (l[0] < c[0]) return DependencyDriftEnum.None;
            if (l[1] > c[1]) return DependencyDriftEnum.Minor;
            if (l[1] < c[1]) return DependencyDriftEnum.None;
            if (l[2] > c[2]) return DependencyDriftEnum.Patch;
            return DependencyDriftEnum.None;
        }

        #endregion

        #region Private-Methods

        private static int[] Parse(string version)
        {
            int[] parts = new int[] { 0, 0, 0 };
            string value = version.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            int dash = value.IndexOf('-');
            if (dash >= 0) value = value.Substring(0, dash);
            int plus = value.IndexOf('+');
            if (plus >= 0) value = value.Substring(0, plus);

            string[] segments = value.Split('.');
            for (int i = 0; i < 3 && i < segments.Length; i++)
            {
                if (Int32.TryParse(segments[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    parts[i] = parsed;
            }

            return parts;
        }

        #endregion
    }
}
