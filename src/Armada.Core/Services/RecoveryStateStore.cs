namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Typed access to recovery state that survives an Admiral restart (RELIABILITY_SIMPLICITY_PLAN.md RS-3.7). The
    /// owning service keeps the state in memory and writes each change through; a failed write is logged and never
    /// fails the caller, so a database hiccup costs only the state's survival across a restart.
    /// </summary>
    public sealed class RecoveryStateStore
    {
        private readonly string _Header = "[RecoveryStateStore] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule? _Logging;
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public RecoveryStateStore(DatabaseDriver database, LoggingModule? logging = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging;
        }

        /// <summary>
        /// Write a value.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="kind">Kind; one of <see cref="RecoveryStateKinds"/>.</param>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        /// <param name="expiresUtc">When the value no longer matters (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task SaveAsync<T>(string kind, string key, T value, DateTime expiresUtc, CancellationToken token = default)
        {
            if (_Database.RecoveryState == null || String.IsNullOrEmpty(key)) return;
            try
            {
                RecoveryStateEntry entry = new RecoveryStateEntry();
                entry.Kind = kind;
                entry.Key = key;
                entry.PayloadJson = JsonSerializer.Serialize(value, _JsonOptions);
                entry.ExpiresUtc = expiresUtc;
                entry.LastUpdateUtc = DateTime.UtcNow;
                await _Database.RecoveryState.UpsertAsync(entry, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging?.Warn(_Header + kind + " " + key + " could not be saved: " + e.Message);
            }
        }

        /// <summary>
        /// Delete a value.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string kind, string key, CancellationToken token = default)
        {
            if (_Database.RecoveryState == null || String.IsNullOrEmpty(key)) return;
            try
            {
                await _Database.RecoveryState.DeleteAsync(kind, key, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging?.Warn(_Header + kind + " " + key + " could not be deleted: " + e.Message);
            }
        }

        /// <summary>
        /// Every unexpired value of a kind, by key. An unreadable value is skipped.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="kind">Kind.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Values by key.</returns>
        public async Task<Dictionary<string, T>> LoadAsync<T>(string kind, CancellationToken token = default) where T : class
        {
            Dictionary<string, T> values = new Dictionary<string, T>(StringComparer.Ordinal);
            if (_Database.RecoveryState == null) return values;
            try
            {
                await _Database.RecoveryState.PurgeExpiredAsync(token).ConfigureAwait(false);
                foreach (RecoveryStateEntry entry in await _Database.RecoveryState.EnumerateAsync(kind, token).ConfigureAwait(false))
                {
                    try
                    {
                        T? value = JsonSerializer.Deserialize<T>(entry.PayloadJson, _JsonOptions);
                        if (value != null) values[entry.Key] = value;
                    }
                    catch (JsonException e)
                    {
                        _Logging?.Warn(_Header + kind + " " + entry.Key + " is unreadable and was skipped: " + e.Message);
                    }
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging?.Warn(_Header + kind + " could not be loaded: " + e.Message);
            }
            return values;
        }
    }
}
