namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Helpers for turning a release version string into the forms native installers accept.
    /// </summary>
    public static class VersionText
    {
        #region Public-Methods

        /// <summary>
        /// Return the numeric "major.minor.patch" core of a semantic version, dropping any prerelease or
        /// build suffix ("1.0.0-rc.1+abc" becomes "1.0.0"). MSI ProductVersion and CFBundleVersion only
        /// accept dotted integers. Missing parts are filled with zero.
        /// </summary>
        /// <param name="version">Release version string.</param>
        /// <returns>Dotted numeric version with exactly three parts.</returns>
        public static string NumericCore(string version)
        {
            if (string.IsNullOrEmpty(version)) throw new ArgumentNullException(nameof(version));

            string core = version;
            int cut = core.IndexOfAny(new char[] { '-', '+' });
            if (cut >= 0) core = core.Substring(0, cut);
            if (core.StartsWith("v", StringComparison.OrdinalIgnoreCase)) core = core.Substring(1);

            List<string> parts = new List<string>();
            foreach (string part in core.Split('.'))
            {
                if (parts.Count == 3) break;
                int value;
                if (!int.TryParse(part, out value) || value < 0) throw new ArgumentException("Version '" + version + "' does not start with a numeric major.minor.patch.");
                parts.Add(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            while (parts.Count < 3) parts.Add("0");
            return string.Join(".", parts);
        }

        #endregion
    }
}
