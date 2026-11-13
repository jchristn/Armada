namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// The values a fleet action template is rendered against for one target vessel. Every value is plain text and
    /// is substituted verbatim (no escaping, no re-expansion).
    /// </summary>
    public class FleetActionTemplateContext
    {
        #region Public-Members

        /// <summary>Safe summary of the latest completed source scan for this vessel.</summary>
        public string ScanSummary { get; set; } = "";
        /// <summary>Validated runtime input values.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per declared fleet action input")]
        public System.Collections.Generic.Dictionary<string, string> InputValues { get; set; } = new();

        /// <summary>
        /// Value of {{vessel.name}}. Never null.
        /// </summary>
        public string VesselName
        {
            get => _VesselName;
            set => _VesselName = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.id}}. Never null.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.defaultBranch}}. Never null.
        /// </summary>
        public string DefaultBranch
        {
            get => _DefaultBranch;
            set => _DefaultBranch = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.workingDirectory}}. Never null; empty when the vessel has no working directory.
        /// </summary>
        public string WorkingDirectory
        {
            get => _WorkingDirectory;
            set => _WorkingDirectory = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.buildCommand}}: the vessel's definition-of-done build command, else the repository's build
        /// script or the catalog default for its detected ecosystem. Never null; empty when nothing resolves.
        /// </summary>
        public string BuildCommand
        {
            get => _BuildCommand;
            set => _BuildCommand = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.ecosystems}}: detected ecosystem names, comma-separated (for example "Node, Container").
        /// Never null; empty when none were detected.
        /// </summary>
        public string Ecosystems
        {
            get => _Ecosystems;
            set => _Ecosystems = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.testCommand}}: the resolved Test command (vessel override, then repo script, then
        /// ecosystem default). Never null; empty when nothing applies.
        /// </summary>
        public string TestCommand
        {
            get => _TestCommand;
            set => _TestCommand = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.lintCommand}}: the resolved Lint command. Never null; empty when nothing applies.
        /// </summary>
        public string LintCommand
        {
            get => _LintCommand;
            set => _LintCommand = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.auditCommand}}: the resolved Audit command (osv-scanner when no detected ecosystem has
        /// its own audit tool). Never null.
        /// </summary>
        public string AuditCommand
        {
            get => _AuditCommand;
            set => _AuditCommand = value ?? String.Empty;
        }

        /// <summary>
        /// Resolved outdated-dependency report command, or empty when the vessel's ecosystems have none.
        /// </summary>
        public string OutdatedCommand
        {
            get => _OutdatedCommand;
            set => _OutdatedCommand = value ?? String.Empty;
        }

        /// <summary>
        /// Resolved license report command, or empty when the vessel's ecosystems have none.
        /// </summary>
        public string LicensesCommand
        {
            get => _LicensesCommand;
            set => _LicensesCommand = value ?? String.Empty;
        }

        /// <summary>
        /// Output parser identifier that matches the resolved audit command (for example npm-audit-json), or null. A
        /// run whose OutputParser is <c>auto</c> uses it.
        /// </summary>
        public string? AuditParserId { get; set; } = null;

        /// <summary>
        /// Value of {{health.summary}}. Never null.
        /// </summary>
        public string HealthSummary
        {
            get => _HealthSummary;
            set => _HealthSummary = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _VesselName = String.Empty;
        private string _VesselId = String.Empty;
        private string _DefaultBranch = String.Empty;
        private string _WorkingDirectory = String.Empty;
        private string _BuildCommand = String.Empty;
        private string _Ecosystems = String.Empty;
        private string _TestCommand = String.Empty;
        private string _LintCommand = String.Empty;
        private string _AuditCommand = String.Empty;
        private string _OutdatedCommand = String.Empty;
        private string _LicensesCommand = String.Empty;
        private string _HealthSummary = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionTemplateContext()
        {
        }

        /// <summary>
        /// Build a context from a vessel, detecting ecosystems in its working directory (when it exists on this
        /// host) and resolving the test, lint and audit commands for the current platform.
        /// <see cref="HealthSummary"/> is left empty; callers fill it when the template references {{health.summary}}.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>Context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="vessel"/> is null.</exception>
        public static FleetActionTemplateContext FromVessel(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            List<DetectedEcosystem> ecosystems = VesselEcosystemDetector.Detect(vessel.WorkingDirectory);
            return FromVessel(vessel, ecosystems, OperatingSystem.IsWindows());
        }

        /// <summary>
        /// Build a context from a vessel and already-detected ecosystems (for example detected on a Harbor host).
        /// Repo scripts are read from the vessel's working directory when it exists on this host.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="ecosystems">Detected ecosystems; null means none.</param>
        /// <param name="windows">True for Windows command variants, false for POSIX.</param>
        /// <returns>Context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="vessel"/> is null.</exception>
        public static FleetActionTemplateContext FromVessel(Vessel vessel, IReadOnlyList<DetectedEcosystem>? ecosystems, bool windows)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            ResolvedEcosystemCommand audit = EcosystemCommandResolver.Resolve(vessel, ecosystems, EcosystemCommandIntentEnum.Audit, vessel.WorkingDirectory, windows);
            return new FleetActionTemplateContext
            {
                VesselName = vessel.Name,
                VesselId = vessel.Id,
                DefaultBranch = vessel.DefaultBranch,
                WorkingDirectory = vessel.WorkingDirectory ?? String.Empty,
                BuildCommand = ResolveLine(vessel, ecosystems, EcosystemCommandIntentEnum.Build, windows),
                Ecosystems = VesselEcosystemDetector.Format(ecosystems),
                TestCommand = ResolveLine(vessel, ecosystems, EcosystemCommandIntentEnum.Test, windows),
                LintCommand = ResolveLine(vessel, ecosystems, EcosystemCommandIntentEnum.Lint, windows),
                AuditCommand = EcosystemCommandResolver.ToShellLine(audit, windows),
                OutdatedCommand = ResolveLine(vessel, ecosystems, EcosystemCommandIntentEnum.Outdated, windows),
                LicensesCommand = ResolveLine(vessel, ecosystems, EcosystemCommandIntentEnum.Licenses, windows),
                AuditParserId = audit.AuditParserId
            };
        }

        #endregion

        #region Private-Methods

        private static string ResolveLine(Vessel vessel, IReadOnlyList<DetectedEcosystem>? ecosystems, EcosystemCommandIntentEnum intent, bool windows)
        {
            ResolvedEcosystemCommand resolved = EcosystemCommandResolver.Resolve(vessel, ecosystems, intent, vessel.WorkingDirectory, windows);
            return EcosystemCommandResolver.ToShellLine(resolved, windows);
        }

        #endregion
    }
}
