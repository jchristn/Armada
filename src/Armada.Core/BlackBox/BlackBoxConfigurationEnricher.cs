namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Enricher that completes the record's effective configuration with what capture could not read in its time budget:
    /// the persona's captain priorities and policies and the vessel's workflow profile commands
    /// (<see cref="BlackBoxConfigurationResolver"/>). Values recorded at the failure site are kept; only missing settings
    /// are added. Thread-safe.
    /// </summary>
    public class BlackBoxConfigurationEnricher : IBlackBoxEnricher
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => "configuration";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxConfigurationEnricher(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EnrichAsync(BlackBoxEnrichmentContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Mission == null && context.Voyage == null) return;
            Captain? captain = null;
            if (!String.IsNullOrEmpty(context.Mission?.CaptainId)) captain = await _Database.Captains.ReadAsync(context.Mission!.CaptainId!, token).ConfigureAwait(false);
            BlackBoxConfigurationInputs inputs = await BlackBoxConfigurationResolver.LoadAsync(_Database, _Settings, _Logging, context.Mission, context.Voyage, context.Vessel, captain, token).ConfigureAwait(false);
            BlackBoxConfiguration resolved = BlackBoxConfigurationResolver.Resolve(inputs);
            if (context.Record.Configuration == null) context.Record.Configuration = resolved;
            else BlackBoxConfigurationResolver.Merge(context.Record.Configuration, resolved);
        }

        #endregion
    }
}
