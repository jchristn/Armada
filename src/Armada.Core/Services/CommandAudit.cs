namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Writes an <c>audit.command</c> event for every shell command or process Armada runs on a user's behalf
    /// (workspace exec, fleet action Command runs, check runs, Harbor probes, merge queue test commands). The record is
    /// written before the command starts, so it exists even if the process hangs or the server stops. Audit events can
    /// be deleted only by a global admin.
    /// </summary>
    public static class CommandAudit
    {
        #region Public-Members

        /// <summary>
        /// Event type of command audit records.
        /// </summary>
        public const string EventType = "audit.command";

        /// <summary>
        /// Prefix shared by all audit event types; events with it are protected from deletion by non-admins.
        /// </summary>
        public const string EventTypePrefix = "audit.";

        #endregion

        #region Private-Members

        private const int _MaxCommandLength = 4000;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an event type is an audit record.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <returns>True for audit.* events.</returns>
        public static bool IsAuditEvent(string? eventType)
        {
            return !String.IsNullOrEmpty(eventType) && eventType.StartsWith(EventTypePrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Persist a command audit record. Never throws: an audit write failure is logged and the caller proceeds.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="record">Record.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task RecordAsync(DatabaseDriver database, CommandAuditRecord record, LoggingModule? logging = null, CancellationToken token = default)
        {
            if (database == null || record == null) return;
            try
            {
                string command = SecretRedactor.Redact(record.Command ?? String.Empty);
                if (command.Length > _MaxCommandLength) command = command.Substring(0, _MaxCommandLength) + "...[truncated]";
                record.Command = command;

                ArmadaEvent evt = new ArmadaEvent(EventType, record.Source + " ran a command" + (String.IsNullOrEmpty(record.UserId) ? "" : " for " + record.UserId) + ": " + Truncate(command, 200));
                evt.TenantId = record.TenantId;
                evt.UserId = record.UserId;
                evt.EntityType = record.EntityType;
                evt.EntityId = record.EntityId;
                evt.VesselId = record.VesselId;
                evt.Payload = JsonSerializer.Serialize(record, _JsonOptions);
                await database.Events.CreateAsync(evt, token).ConfigureAwait(false);
                logging?.Info("[CommandAudit] " + evt.Message);
            }
            catch (Exception ex)
            {
                logging?.Warn("[CommandAudit] failed to write the audit record for a " + record.Source + " command: " + ex.Message);
            }
        }

        #endregion

        #region Private-Methods

        private static string Truncate(string value, int max)
        {
            if (value.Length <= max) return value;
            return value.Substring(0, max) + "...";
        }

        #endregion
    }
}
