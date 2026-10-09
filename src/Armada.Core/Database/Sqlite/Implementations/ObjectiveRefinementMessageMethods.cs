namespace Armada.Core.Database.Sqlite.Implementations
{
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of objective refinement transcript persistence.
    /// </summary>
    public class ObjectiveRefinementMessageMethods : IObjectiveRefinementMessageMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectiveRefinementMessageMethods"/> class.
        /// </summary>
        public ObjectiveRefinementMessageMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementMessage> CreateAsync(ObjectiveRefinementMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO objective_refinement_messages
                        (id, objective_refinement_session_id, objective_id, tenant_id, user_id, role, sequence, content, is_selected, created_utc, last_update_utc)
                        VALUES
                        (@id, @objective_refinement_session_id, @objective_id, @tenant_id, @user_id, @role, @sequence, @content, @is_selected, @created_utc, @last_update_utc);";
                    Bind(cmd, message);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return message;
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementMessage> UpdateAsync(ObjectiveRefinementMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE objective_refinement_messages SET
                        objective_refinement_session_id = @objective_refinement_session_id,
                        objective_id = @objective_id,
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        role = @role,
                        sequence = @sequence,
                        content = @content,
                        is_selected = @is_selected,
                        created_utc = @created_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    Bind(cmd, message);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return message;
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementMessage?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM objective_refinement_messages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM objective_refinement_messages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task DeleteBySessionAsync(string objectiveRefinementSessionId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveRefinementSessionId)) throw new ArgumentNullException(nameof(objectiveRefinementSessionId));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM objective_refinement_messages WHERE objective_refinement_session_id = @session_id;";
                    cmd.Parameters.AddWithValue("@session_id", objectiveRefinementSessionId);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementMessage>> EnumerateBySessionAsync(string objectiveRefinementSessionId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveRefinementSessionId)) throw new ArgumentNullException(nameof(objectiveRefinementSessionId));
            return await EnumerateAsync("SELECT * FROM objective_refinement_messages WHERE objective_refinement_session_id = @session_id ORDER BY sequence ASC, created_utc ASC;", cmd => cmd.Parameters.AddWithValue("@session_id", objectiveRefinementSessionId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementMessage>> EnumerateByObjectiveAsync(string objectiveId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            return await EnumerateAsync("SELECT * FROM objective_refinement_messages WHERE objective_id = @objective_id ORDER BY created_utc ASC, sequence ASC;", cmd => cmd.Parameters.AddWithValue("@objective_id", objectiveId), token).ConfigureAwait(false);
        }

        private async Task<List<ObjectiveRefinementMessage>> EnumerateAsync(string sql, Action<SqliteCommand> parameterize, CancellationToken token)
        {
            List<ObjectiveRefinementMessage> results = new List<ObjectiveRefinementMessage>();
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }
            }

            return results;
        }

        private static void Bind(SqliteCommand cmd, ObjectiveRefinementMessage message)
        {
            cmd.Parameters.AddWithValue("@id", message.Id);
            cmd.Parameters.AddWithValue("@objective_refinement_session_id", message.ObjectiveRefinementSessionId);
            cmd.Parameters.AddWithValue("@objective_id", message.ObjectiveId);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)message.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)message.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@role", message.Role);
            cmd.Parameters.AddWithValue("@sequence", message.Sequence);
            cmd.Parameters.AddWithValue("@content", message.Content);
            cmd.Parameters.AddWithValue("@is_selected", message.IsSelected ? 1 : 0);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(message.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(message.LastUpdateUtc));
        }

        private static ObjectiveRefinementMessage FromReader(SqliteDataReader reader)
        {
            return new ObjectiveRefinementMessage
            {
                Id = reader["id"].ToString()!,
                ObjectiveRefinementSessionId = reader["objective_refinement_session_id"].ToString()!,
                ObjectiveId = reader["objective_id"].ToString()!,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                Role = reader["role"].ToString()!,
                Sequence = Convert.ToInt32(reader["sequence"]),
                Content = SqliteDatabaseDriver.NullableString(reader["content"]) ?? String.Empty,
                IsSelected = Convert.ToInt64(reader["is_selected"]) == 1,
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };
        }
    }
}
