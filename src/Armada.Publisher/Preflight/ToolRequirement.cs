namespace Armada.Publisher.Preflight
{
    using System;
    using Armada.Publisher.Manifest;

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

        /// <summary>
        /// Condition under which the tool is needed, evaluated against the manifest and the current environment.
        /// Null means always. When it returns false the Doctor reports the tool as skipped, not missing.
        /// </summary>
        public Func<PublisherManifest, bool>? RequiredWhen { get; set; }

        /// <summary>
        /// Why the tool is not needed when <see cref="RequiredWhen"/> returns false, printed by the Doctor.
        /// </summary>
        public string NotRequiredReason { get; set; } = string.Empty;

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

        /// <summary>
        /// The signtool requirement shared by the Windows installer channels. signtool is needed only when the
        /// Windows certificate secret is set; without it the installers are built unsigned.
        /// </summary>
        /// <returns>Conditional signtool requirement.</returns>
        public static ToolRequirement WindowsSigntool()
        {
            return new ToolRequirement("signtool", "Install the Windows SDK and put signtool.exe on PATH; needed to Authenticode-sign the installer.", "win")
            {
                RequiredWhen = manifest => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(manifest.Signing.Windows.CertBase64Secret)),
                NotRequiredReason = "Windows certificate secret not set; installers will be unsigned"
            };
        }

        #endregion
    }
}
