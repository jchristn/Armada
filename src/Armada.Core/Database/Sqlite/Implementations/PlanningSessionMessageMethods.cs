namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of planning session message database operations.
    /// </summary>
    public class PlanningSessionMessageMethods : IPlanningSessionMessageMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionMessageMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<PlanningSessionMessage> CreateAsync(PlanningSessionMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            message.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO planning_session_messages
                        (id, planning_session_id, tenant_id, user_id, role, sequence, content, is_selected_for_dispatch, created_utc, last_update_utc)
                        VALUES
                        (@id, @planning_session_id, @tenant_id, @user_id, @role, @sequence, @content, @is_selected_for_dispatch, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", message.Id);
                    cmd.Parameters.AddWithValue("@planning_session_id", message.PlanningSessionId);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)message.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)message.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@role", message.Role);
                    cmd.Parameters.AddWithValue("@sequence", message.Sequence);
                    cmd.Parameters.AddWithValue("@content", message.Content);
                    cmd.Parameters.AddWithValue("@is_selected_for_dispatch", message.IsSelectedForDispatch ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(message.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(message.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return message;
        }

        /// <inheritdoc />
        public async Task<PlanningSessionMessage?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM planning_session_messages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqliteDatabaseDriver.PlanningSessionMessageFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<PlanningSessionMessage> UpdateAsync(PlanningSessionMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            message.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE planning_session_messages SET
                        planning_session_id = @planning_session_id,
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        role = @role,
                        sequence = @sequence,
                        content = @content,
                        is_selected_for_dispatch = @is_selected_for_dispatch,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", message.Id);
                    cmd.Parameters.AddWithValue("@planning_session_id", message.PlanningSessionId);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)message.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)message.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@role", message.Role);
                    cmd.Parameters.AddWithValue("@sequence", message.Sequence);
                    cmd.Parameters.AddWithValue("@content", message.Content);
                    cmd.Parameters.AddWithValue("@is_selected_for_dispatch", message.IsSelectedForDispatch ? 1 : 0);
                    cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(message.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return message;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM planning_session_messages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task DeleteBySessionAsync(string planningSessionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(planningSessionId)) throw new ArgumentNullException(nameof(planningSessionId));

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM planning_session_messages WHERE planning_session_id = @planning_session_id;";
                    cmd.Parameters.AddWithValue("@planning_session_id", planningSessionId);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<PlanningSessionMessage>> EnumerateBySessionAsync(string planningSessionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(planningSessionId)) throw new ArgumentNullException(nameof(planningSessionId));

            List<PlanningSessionMessage> results = new List<PlanningSessionMessage>();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM planning_session_messages WHERE planning_session_id = @planning_session_id ORDER BY sequence ASC, created_utc ASC;";
                    cmd.Parameters.AddWithValue("@planning_session_id", planningSessionId);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqliteDatabaseDriver.PlanningSessionMessageFromReader(reader));
                    }
                }
            }

            return results;
        }
    }
}
