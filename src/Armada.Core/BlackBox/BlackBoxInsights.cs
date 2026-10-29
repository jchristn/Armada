namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.BlackBox.Enrichment;
    using Armada.Core.Database;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The black box prevention, forensics and operations components, built once by <see cref="BlackBoxRuntime"/>:
    /// overlap analyzer, dispatch advisor, insight service (the reads every surface calls), success flight recorder,
    /// recovery outcome tracker with the advisor's path statistics, runbook linker and their worker.
    /// </summary>
    public class BlackBoxInsights : IDisposable
    {
        #region Public-Members

        /// <summary>Overlap analyzer.</summary>
        public MissionOverlapAnalyzer Analyzer { get; }

        /// <summary>Dispatch advisor (the Admiral sets it on AdmiralService.DispatchAdvisor).</summary>
        public BlackBoxDispatchAdvisor DispatchAdvisor { get; }

        /// <summary>The reads every surface calls.</summary>
        public BlackBoxInsightService Service { get; }

        /// <summary>Success flight recorder.</summary>
        public BlackBoxSuccessRecorder Successes { get; }

        /// <summary>Recovery path success rates (BlackBoxAdvisor.PathStats).</summary>
        public BlackBoxPathStatsCache PathStats { get; }

        /// <summary>Recovery outcome tracker (BlackBoxService.RecoveryObserver).</summary>
        public BlackBoxRecoveryOutcomeTracker Outcomes { get; }

        /// <summary>Runbook linker (BlackBoxPipeline.AnalysisHook).</summary>
        public BlackBoxRunbookLinker Runbooks { get; }

        /// <summary>Nearby-success enricher (registered on the pipeline).</summary>
        public BlackBoxNearbySuccessEnricher NearbySuccessEnricher { get; }

        /// <summary>The insight worker.</summary>
        public BlackBoxInsightBackgroundService Background { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and wire into the pipeline, advisor and service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="pipeline">Pipeline.</param>
        /// <param name="advisor">Advisor.</param>
        /// <param name="service">Black box service.</param>
        /// <param name="inspector">Git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxInsights(DatabaseDriver database, ArmadaSettings settings, BlackBoxPipeline pipeline, BlackBoxAdvisor advisor, BlackBoxService service, VesselGitInspector inspector, LoggingModule logging)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (advisor == null) throw new ArgumentNullException(nameof(advisor));
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (inspector == null) throw new ArgumentNullException(nameof(inspector));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            Analyzer = new MissionOverlapAnalyzer(database, settings, inspector, logging);
            DispatchAdvisor = new BlackBoxDispatchAdvisor(Analyzer, logging);
            Service = new BlackBoxInsightService(database, settings, pipeline, Analyzer, logging);
            Successes = new BlackBoxSuccessRecorder(database, settings, inspector, logging);
            PathStats = new BlackBoxPathStatsCache(database);
            Outcomes = new BlackBoxRecoveryOutcomeTracker(database, PathStats, logging);
            Runbooks = new BlackBoxRunbookLinker(database, logging);
            NearbySuccessEnricher = new BlackBoxNearbySuccessEnricher(database, settings, logging);
            Background = new BlackBoxInsightBackgroundService(database, settings, Successes, Outcomes, logging);

            pipeline.AddEnricher(NearbySuccessEnricher);
            pipeline.AnalysisHook = Runbooks;
            advisor.PathStats = PathStats;
            service.RecoveryObserver = Outcomes;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Dispose()
        {
            Background.Dispose();
        }

        #endregion
    }
}
