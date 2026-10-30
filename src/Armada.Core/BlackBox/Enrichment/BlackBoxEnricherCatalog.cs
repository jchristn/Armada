namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Database;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The built-in enrichers in the order they run: Workspace, TargetMovement, ConflictAttribution, ConcurrentOverlap,
    /// DirectoryRollup, Attempts, Evidence, Related, Voyage, FileRows. Later steps read what earlier ones recorded (target
    /// commits feed attribution; attribution and overlap feed the file rows).
    /// </summary>
    public static class BlackBoxEnricherCatalog
    {
        #region Public-Methods

        /// <summary>
        /// Build the built-in enrichers.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings.</param>
        /// <param name="inspector">Host-aware git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>The enrichers, in order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static List<IBlackBoxEnricher> CreateDefaults(DatabaseDriver database, ArmadaSettings settings, VesselGitInspector inspector, LoggingModule logging)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (inspector == null) throw new ArgumentNullException(nameof(inspector));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            return new List<IBlackBoxEnricher>
            {
                new BlackBoxWorkspaceEnricher(database, inspector, logging),
                new BlackBoxTargetMovementEnricher(database, inspector, logging),
                new BlackBoxConflictAttributionEnricher(database, logging),
                new BlackBoxConcurrentOverlapEnricher(database, inspector, logging),
                new BlackBoxDirectoryRollupEnricher(logging),
                new BlackBoxAttemptsEnricher(database, logging),
                new BlackBoxEvidenceEnricher(database, settings, inspector, logging),
                new BlackBoxRelatedEnricher(database, logging),
                new BlackBoxVoyageEnricher(database, logging),
                new BlackBoxFileRowsEnricher(logging)
            };
        }

        /// <summary>
        /// Register the built-in enrichers on a pipeline.
        /// </summary>
        /// <param name="pipeline">Pipeline.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings.</param>
        /// <param name="inspector">Host-aware git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void Register(BlackBoxPipeline pipeline, DatabaseDriver database, ArmadaSettings settings, VesselGitInspector inspector, LoggingModule logging)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            foreach (IBlackBoxEnricher enricher in CreateDefaults(database, settings, inspector, logging)) pipeline.AddEnricher(enricher);
        }

        #endregion
    }
}
