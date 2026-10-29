namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.BlackBox.Enrichment;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Composition root of the black box: builds and holds every black box component for one Admiral (or test) so they
    /// share one store factory, redactor, writer and queue. <see cref="Create"/> registers the built-in enrichers
    /// (<see cref="BlackBoxEnricherCatalog"/>) on the pipeline. Construct with <see cref="Create"/>, wire
    /// <see cref="Capture"/> into the failure sites, call <see cref="Background"/>.Start(), and route every surface to
    /// <see cref="Service"/>. Thread-safe.
    /// </summary>
    public class BlackBoxRuntime : IDisposable
    {
        #region Public-Members

        /// <summary>Redactor (register secret setting values with RegisterSecrets).</summary>
        public BlackBoxRedactor Redactor { get; }

        /// <summary>Store factory.</summary>
        public BlackBoxStoreFactory Stores { get; }

        /// <summary>Object repository.</summary>
        public BlackBoxObjectRepository Objects { get; }

        /// <summary>Brief renderer.</summary>
        public BlackBoxBriefRenderer Renderer { get; }

        /// <summary>Entry writer.</summary>
        public BlackBoxEntryWriter Writer { get; }

        /// <summary>Event sink.</summary>
        public IBlackBoxEventSink Events { get; }

        /// <summary>Capture (failure sites call it).</summary>
        public BlackBoxCapture Capture { get; }

        /// <summary>Enrichment pipeline (add enrichers, set the advisor).</summary>
        public BlackBoxPipeline Pipeline { get; }

        /// <summary>The advisor the pipeline runs (built-in rule catalog).</summary>
        public BlackBoxAdvisor Advisor { get; }

        /// <summary>Background re-analysis of entries analyzed with older rules.</summary>
        public BlackBoxReanalyzer Reanalyzer { get; }

        /// <summary>Enrichment queue.</summary>
        public BlackBoxEnrichmentQueue Queue { get; }

        /// <summary>Sweeper and backfill.</summary>
        public BlackBoxSweeper Sweeper { get; }

        /// <summary>Garbage collector.</summary>
        public BlackBoxGarbageCollector GarbageCollector { get; }

        /// <summary>The facade every surface calls.</summary>
        public BlackBoxService Service { get; }

        /// <summary>Background loops (queue worker, sweeper, garbage collection).</summary>
        public BlackBoxBackgroundService Background { get; }

        /// <summary>
        /// Host-aware git inspector the built-in enrichers read docks with. It starts with the Admiral's host only; the
        /// Admiral sets <see cref="VesselGitInspector.Hosts"/> to its Harbor-aware resolver once Harbor delegation exists.
        /// </summary>
        public VesselGitInspector Inspector { get; }

        #endregion

        #region Constructors-and-Factories

        private BlackBoxRuntime(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, IBlackBoxVersionProvider version, IBlackBoxEventSink events)
        {
            Redactor = new BlackBoxRedactor();
            Redactor.RegisterSecrets(SecretValues(settings.BlackBox));
            Stores = new BlackBoxStoreFactory(database, settings.BlackBox);
            Objects = new BlackBoxObjectRepository(database, Stores);
            Renderer = new BlackBoxBriefRenderer(Redactor);
            Events = events;
            Writer = new BlackBoxEntryWriter(database, settings.BlackBox, Objects, Renderer, Redactor, Events, logging);
            Capture = new BlackBoxCapture(database, settings, Writer, Events, version, Redactor, logging);
            Pipeline = new BlackBoxPipeline(database, settings.BlackBox, Objects, Writer, Events, logging);
            Inspector = new VesselGitInspector(database, new DockHostResolver(new GitService(logging), logging, null), logging);
            BlackBoxEnricherCatalog.Register(Pipeline, database, settings, Inspector, logging);
            Advisor = new BlackBoxAdvisor(BlackBoxRuleCatalog.Default, logging);
            Pipeline.Advisor = Advisor;
            Pipeline.SummaryWriter = new ModelEndpointBlackBoxSummaryWriter(database, logging);
            Pipeline.AddEnricher(new BlackBoxConfigurationEnricher(database, settings, logging));
            Reanalyzer = new BlackBoxReanalyzer(database, Pipeline, logging);
            Queue = new BlackBoxEnrichmentQueue(Pipeline, logging);
            Capture.Enrichment = Queue;
            Sweeper = new BlackBoxSweeper(database, settings, Capture, Queue, logging);
            GarbageCollector = new BlackBoxGarbageCollector(database, settings.BlackBox, Stores, Events, logging);
            Service = new BlackBoxService(database, settings, Objects, Renderer, Pipeline, GarbageCollector, Sweeper, Stores, Events, logging);
            Service.Redactor = Redactor;
            Background = new BlackBoxBackgroundService(settings, Queue, Sweeper, GarbageCollector, logging);
            Background.Reanalyzer = Reanalyzer;
        }

        /// <summary>
        /// Build the black box.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="version">Build identity, or null for the entry assembly's.</param>
        /// <param name="events">Event sink, or null for a <see cref="DatabaseBlackBoxEventSink"/>.</param>
        /// <returns>The runtime.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static BlackBoxRuntime Create(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, IBlackBoxVersionProvider? version = null, IBlackBoxEventSink? events = null)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            return new BlackBoxRuntime(database, settings, logging, version ?? new AssemblyBlackBoxVersionProvider(), events ?? new DatabaseBlackBoxEventSink(database, logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The secret values of black box storage settings (for redaction).
        /// </summary>
        /// <param name="settings">Black box settings.</param>
        /// <returns>Values (may contain nulls).</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static string?[] SecretValues(BlackBoxSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            BlackBoxStorageSettings s = settings.Storage;
            return new string?[] { s.S3.AccessKey, s.S3.SecretKey, s.AzureBlob.AccessKey, s.AzureBlob.ConnectionString, s.Cifs.Password };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Background.Dispose();
            Queue.Dispose();
        }

        #endregion
    }
}
