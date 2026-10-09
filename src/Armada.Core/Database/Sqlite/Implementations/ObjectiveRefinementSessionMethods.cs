namespace Armada.Core.Database.Sqlite.Implementations
{
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of objective refinement session persistence.
    /// </summary>
    public class ObjectiveRefinementSessionMethods : IObjectiveRefinementSessionMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectiveRefinementSessionMethods"/> class.
        /// </summary>
        public ObjectiveRefinementSessionMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession> CreateAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO objective_refinement_sessions
                        (id, objective_id, tenant_id, user_id, captain_id, fleet_id, vessel_id, title, status, process_id, failure_reason, created_utc, started_utc, completed_utc, last_update_utc)
                        VALUES
                        (@id, @objective_id, @tenant_id, @user_id, @captain_id, @fleet_id, @vessel_id, @title, @status, @process_id, @failure_reason, @created_utc, @started_utc, @completed_utc, @last_update_utc);";
                    Bind(cmd, session);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return session;
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession> UpdateAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE objective_refinement_sessions SET
                        objective_id = @objective_id,
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        captain_id = @captain_id,
                        fleet_id = @fleet_id,
                        vessel_id = @vessel_id,
                        title = @title,
                        status = @status,
                        process_id = @process_id,
                        failure_reason = @failure_reason,
                        created_utc = @created_utc,
                        started_utc = @started_utc,
                        completed_utc = @completed_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    Bind(cmd, session);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return session;
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM objective_refinement_sessions WHERE id = @id;", cmd => cmd.Parameters.AddWithValue("@id", id), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id AND id = @id;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@id", id);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@user_id", userId);
                cmd.Parameters.AddWithValue("@id", id);
            }, token).ConfigureAwait(false);
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
                    cmd.CommandText = "DELETE FROM objective_refinement_sessions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync("SELECT * FROM objective_refinement_sessions ORDER BY last_update_utc DESC;", null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync("SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id ORDER BY last_update_utc DESC;", cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync("SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY last_update_utc DESC;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@user_id", userId);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateByObjectiveAsync(string objectiveId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            return await EnumerateInternalAsync("SELECT * FROM objective_refinement_sessions WHERE objective_id = @objective_id ORDER BY created_utc DESC;", cmd => cmd.Parameters.AddWithValue("@objective_id", objectiveId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(captainId)) throw new ArgumentNullException(nameof(captainId));
            return await EnumerateInternalAsync("SELECT * FROM objective_refinement_sessions WHERE captain_id = @captain_id ORDER BY last_update_utc DESC;", cmd => cmd.Parameters.AddWithValue("@captain_id", captainId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateByStatusAsync(ObjectiveRefinementSessionStatusEnum status, CancellationToken token = default)
        {
            return await EnumerateInternalAsync("SELECT * FROM objective_refinement_sessions WHERE status = @status ORDER BY last_update_utc DESC;", cmd => cmd.Parameters.AddWithValue("@status", status.ToString()), token).ConfigureAwait(false);
        }

        private async Task<ObjectiveRefinementSession?> ReadInternalAsync(string sql, Action<SqliteCommand> parameterize, CancellationToken token)
        {
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<ObjectiveRefinementSession>> EnumerateInternalAsync(string sql, Action<SqliteCommand>? parameterize, CancellationToken token)
        {
            List<ObjectiveRefinementSession> results = new List<ObjectiveRefinementSession>();
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize?.Invoke(cmd);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }
            }

            return results;
        }

        private static void Bind(SqliteCommand cmd, ObjectiveRefinementSession session)
        {
            cmd.Parameters.AddWithValue("@id", session.Id);
            cmd.Parameters.AddWithValue("@objective_id", session.ObjectiveId);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)session.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)session.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@captain_id", session.CaptainId);
            cmd.Parameters.AddWithValue("@fleet_id", (object?)session.FleetId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)session.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@title", session.Title);
            cmd.Parameters.AddWithValue("@status", session.Status.ToString());
            cmd.Parameters.AddWithValue("@process_id", session.ProcessId.HasValue ? (object)session.ProcessId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@failure_reason", (object?)session.FailureReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(session.CreatedUtc));
            cmd.Parameters.AddWithValue("@started_utc", session.StartedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(session.StartedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", session.CompletedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(session.CompletedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(session.LastUpdateUtc));
        }

        private static ObjectiveRefinementSession FromReader(SqliteDataReader reader)
        {
            return new ObjectiveRefinementSession
            {
                Id = reader["id"].ToString()!,
                ObjectiveId = reader["objective_id"].ToString()!,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                CaptainId = reader["captain_id"].ToString()!,
                FleetId = SqliteDatabaseDriver.NullableString(reader["fleet_id"]),
                VesselId = SqliteDatabaseDriver.NullableString(reader["vessel_id"]),
                Title = reader["title"].ToString()!,
                Status = Enum.TryParse<ObjectiveRefinementSessionStatusEnum>(reader["status"].ToString(), true, out ObjectiveRefinementSessionStatusEnum parsed) ? parsed : ObjectiveRefinementSessionStatusEnum.Created,
                ProcessId = SqliteDatabaseDriver.NullableInt(reader["process_id"]),
                FailureReason = SqliteDatabaseDriver.NullableString(reader["failure_reason"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                StartedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["started_utc"]),
                CompletedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["completed_utc"]),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };
        }
    }
}
