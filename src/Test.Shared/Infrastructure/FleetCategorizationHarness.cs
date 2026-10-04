namespace Test.Shared.Infrastructure
{
    using Armada.Core.Services;
    using Armada.Core.Settings;

    /// <summary>
    /// Wiring for fleet categorization tests: settings with an allowed import root and a scratch data directory, a job
    /// service, a stubbed captain runner, and the categorization and import services built on them.
    /// </summary>
    public sealed class FleetCategorizationHarness
    {
        /// <summary>
        /// Allowed import root holding the test repositories.
        /// </summary>
        public string Root { get; set; } = "";

        /// <summary>
        /// Settings the services read.
        /// </summary>
        public ArmadaSettings Settings { get; set; } = null!;

        /// <summary>
        /// Job service.
        /// </summary>
        public JobService Jobs { get; set; } = null!;

        /// <summary>
        /// Stubbed captain runner.
        /// </summary>
        public StubCaptainPromptRunner Runner { get; set; } = null!;

        /// <summary>
        /// Fleet categorization service under test.
        /// </summary>
        public FleetCategorizationService Categorization { get; set; } = null!;

        /// <summary>
        /// Vessel import service wired to the categorization service.
        /// </summary>
        public VesselImportService Import { get; set; } = null!;
    }
}
