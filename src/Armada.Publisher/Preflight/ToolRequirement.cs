namespace Armada.Publisher.Preflight
{
    using System;

    /// <summary>
    /// A single external tool a channel needs, paired with the exact command to install it so a
    /// missing tool produces actionable guidance rather than an opaque failure.
    /// </summary>
    public class ToolRequirement
    {
        #region Public-Members

        /// <summary>
        /// Executable name probed on PATH (for example "iscc", "fpm", "signtool").
        /// </summary>
        public string Executable { get; set; }

        /// <summary>
        /// The exact install command to print when the tool is missing
        /// (for example "choco install innosetup -y").
        /// </summary>
        public string InstallCommand { get; set; }

        /// <summary>
        /// The operating systems on which the requirement applies, as RID prefixes ("win", "osx", "linux").
        /// Empty means all.
        /// </summary>
        public string OperatingSystems { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a tool requirement.
        /// </summary>
        /// <param name="executable">Executable probed on PATH.</param>
        /// <param name="installCommand">Exact install command.</param>
        /// <param name="operatingSystems">Applicable OS prefixes, comma-separated, or empty for all.</param>
        public ToolRequirement(string executable, string installCommand, string operatingSystems = "")
        {
            Executable = executable ?? throw new ArgumentNullException(nameof(executable));
            InstallCommand = installCommand ?? throw new ArgumentNullException(nameof(installCommand));
            OperatingSystems = operatingSystems ?? string.Empty;
        }

        #endregion
    }
}
