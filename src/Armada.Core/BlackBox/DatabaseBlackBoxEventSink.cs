namespace Armada.Core.BlackBox
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Records black box events as ArmadaEvent rows (EntityType "blackbox", EntityId the entry id, Payload the typed
    /// payload as camelCase JSON). Never throws for delivery problems. An optional callback delivers the recorded event
    /// further (the Admiral sets it to broadcast over the WebSocket). Thread-safe.
    /// </summary>
    public class DatabaseBlackBoxEventSink : IBlackBoxEventSink
    {
        #region Public-Members

        /// <summary>
        /// Called after an event is recorded, with the event and its payload (for example a WebSocket broadcast), or null.
        /// Exceptions it throws are logged.
        /// </summary>
        public Func<ArmadaEvent, object, Task>? OnEmitted { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[DatabaseBlackBoxEventSink] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public DatabaseBlackBoxEventSink(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EmitAsync(string eventType, string message, string? tenantId, string? entryId, string? missionId, string? voyageId, string? vesselId, object payload, CancellationToken token = default)
        {
            try
            {
                ArmadaEvent evt = new ArmadaEvent(eventType, message);
                evt.TenantId = tenantId;
                evt.EntityType = BlackBoxEventTypes.EntityType;
                evt.EntityId = entryId;
                evt.MissionId = missionId;
                evt.VoyageId = voyageId;
                evt.VesselId = vesselId;
                evt.Payload = payload == null ? null : JsonSerializer.Serialize(payload, payload.GetType(), BlackBoxJson.CompactOptions);
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
                if (OnEmitted != null)
                {
                    try
                    {
                        await OnEmitted(evt, payload!).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        _Logging.Warn(_Header + "delivering " + eventType + " failed: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "recording " + eventType + " failed: " + e.Message);
            }
        }

        #endregion
    }
}
