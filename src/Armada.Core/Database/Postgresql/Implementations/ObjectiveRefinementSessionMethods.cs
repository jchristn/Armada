namespace Armada.Core.Database.Postgresql.Implementations
{
    using System.Text;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of objective refinement session persistence.
    /// </summary>
    public class ObjectiveRefinementSessionMethods : IObjectiveRefinementSessionMethods
    {
        private readonly PostgresqlDatabaseDriver _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectiveRefinementSessionMethods"/> class.
        /// </summary>
        public ObjectiveRefinementSessionMethods(PostgresqlDatabaseDriver driver, Settings.DatabaseSettings settings, SyslogLogging.LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession> CreateAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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
            return await ReadInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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
            return await EnumerateInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id ORDER BY last_update_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY last_update_utc DESC;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateByObjectiveAsync(string objectiveId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            return await EnumerateInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE objective_id = @objective_id ORDER BY created_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@objective_id", objectiveId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(captainId)) throw new ArgumentNullException(nameof(captainId));
            return await EnumerateInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE captain_id = @captain_id ORDER BY last_update_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@captain_id", captainId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementSession>> EnumerateByStatusAsync(ObjectiveRefinementSessionStatusEnum status, CancellationToken token = default)
        {
            return await EnumerateInternalAsync(
                "SELECT * FROM objective_refinement_sessions WHERE status = @status ORDER BY last_update_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@status", status.ToString()),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ObjectiveRefinementSession>> EnumeratePageByObjectiveAsync(string objectiveId, string? tenantId, string? userId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
            StringBuilder where = new StringBuilder("FROM objective_refinement_sessions WHERE objective_id = @objective_id");
            if (!String.IsNullOrEmpty(tenantId)) where.Append(" AND tenant_id = @tenant_id");
            if (!String.IsNullOrEmpty(userId)) where.Append(" AND user_id = @user_id");
            if (!String.IsNullOrEmpty(query.Status)) where.Append(" AND status = @status");
            if (!String.IsNullOrEmpty(query.CaptainId)) where.Append(" AND captain_id = @captain_id");
            if (!String.IsNullOrEmpty(query.VesselId)) where.Append(" AND vessel_id = @vessel_id");
            if (query.CreatedAfter.HasValue) where.Append(" AND created_utc > @created_after");
            if (query.CreatedBefore.HasValue) where.Append(" AND created_utc < @created_before");
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await PostgresqlBoundedPage.ReadAsync(conn, where.ToString(), "last_update_utc " + direction + ", id " + direction,
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@objective_id", objectiveId);
                        if (!String.IsNullOrEmpty(tenantId)) PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        if (!String.IsNullOrEmpty(userId)) PostgresqlCommandHelper.Add(cmd, "@user_id", userId);
                        if (!String.IsNullOrEmpty(query.Status)) PostgresqlCommandHelper.Add(cmd, "@status", query.Status);
                        if (!String.IsNullOrEmpty(query.CaptainId)) PostgresqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
                        if (!String.IsNullOrEmpty(query.VesselId)) PostgresqlCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
                        if (query.CreatedAfter.HasValue) cmd.Parameters.AddWithValue("@created_after", query.CreatedAfter.Value.ToUniversalTime());
                        if (query.CreatedBefore.HasValue) cmd.Parameters.AddWithValue("@created_before", query.CreatedBefore.Value.ToUniversalTime());
                    }, FromReader, query, token).ConfigureAwait(false);
            }
        }

        private async Task<ObjectiveRefinementSession?> ReadInternalAsync(string sql, Action<NpgsqlCommand> parameterize, CancellationToken token)
        {
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<ObjectiveRefinementSession>> EnumerateInternalAsync(string sql, Action<NpgsqlCommand>? parameterize, CancellationToken token)
        {
            List<ObjectiveRefinementSession> results = new List<ObjectiveRefinementSession>();
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize?.Invoke(cmd);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }
            }

            return results;
        }

        private static void Bind(NpgsqlCommand cmd, ObjectiveRefinementSession session)
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
            cmd.Parameters.AddWithValue("@process_id", (object?)session.ProcessId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@failure_reason", (object?)session.FailureReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", session.CreatedUtc);
            cmd.Parameters.AddWithValue("@started_utc", (object?)session.StartedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", (object?)session.CompletedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_update_utc", session.LastUpdateUtc);
        }

        private static ObjectiveRefinementSession FromReader(NpgsqlDataReader reader)
        {
            return new ObjectiveRefinementSession
            {
                Id = reader["id"].ToString()!,
                ObjectiveId = reader["objective_id"].ToString()!,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                CaptainId = reader["captain_id"].ToString()!,
                FleetId = NullableString(reader["fleet_id"]),
                VesselId = NullableString(reader["vessel_id"]),
                Title = reader["title"].ToString()!,
                Status = ObjectivePersistenceHelper.ParseEnum(reader["status"], ObjectiveRefinementSessionStatusEnum.Created),
                ProcessId = NullableInt(reader["process_id"]),
                FailureReason = NullableString(reader["failure_reason"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                StartedUtc = NullableDateTime(reader["started_utc"]),
                CompletedUtc = NullableDateTime(reader["completed_utc"]),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return String.IsNullOrEmpty(str) ? null : str;
        }

        private static int? NullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        private static DateTime? NullableDateTime(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc);
        }
    }
}
