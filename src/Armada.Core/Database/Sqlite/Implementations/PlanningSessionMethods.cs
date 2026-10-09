namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of planning session database operations.
    /// </summary>
    public class PlanningSessionMethods : IPlanningSessionMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<PlanningSession> CreateAsync(PlanningSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO planning_sessions
                        (id, tenant_id, user_id, captain_id, vessel_id, fleet_id, dock_id, branch_name, title, status, pipeline_id, selected_playbooks_json, process_id, failure_reason, created_utc, started_utc, completed_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @captain_id, @vessel_id, @fleet_id, @dock_id, @branch_name, @title, @status, @pipeline_id, @selected_playbooks_json, @process_id, @failure_reason, @created_utc, @started_utc, @completed_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", session.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)session.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)session.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@captain_id", session.CaptainId);
                    cmd.Parameters.AddWithValue("@vessel_id", session.VesselId);
                    cmd.Parameters.AddWithValue("@fleet_id", (object?)session.FleetId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@dock_id", (object?)session.DockId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", (object?)session.BranchName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@title", session.Title);
                    cmd.Parameters.AddWithValue("@status", session.Status.ToString());
                    cmd.Parameters.AddWithValue("@pipeline_id", (object?)session.PipelineId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@selected_playbooks_json", session.SerializeSelectedPlaybooks());
                    cmd.Parameters.AddWithValue("@process_id", session.ProcessId.HasValue ? (object)session.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@failure_reason", (object?)session.FailureReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(session.CreatedUtc));
                    cmd.Parameters.AddWithValue("@started_utc", session.StartedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(session.StartedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", session.CompletedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(session.CompletedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(session.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return session;
        }

        /// <inheritdoc />
        public async Task<PlanningSession?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM planning_sessions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqliteDatabaseDriver.PlanningSessionFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<PlanningSession> UpdateAsync(PlanningSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE planning_sessions SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        captain_id = @captain_id,
                        vessel_id = @vessel_id,
                        fleet_id = @fleet_id,
                        dock_id = @dock_id,
                        branch_name = @branch_name,
                        title = @title,
                        status = @status,
                        pipeline_id = @pipeline_id,
                        selected_playbooks_json = @selected_playbooks_json,
                        process_id = @process_id,
                        failure_reason = @failure_reason,
                        started_utc = @started_utc,
                        completed_utc = @completed_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", session.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)session.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)session.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@captain_id", session.CaptainId);
                    cmd.Parameters.AddWithValue("@vessel_id", session.VesselId);
                    cmd.Parameters.AddWithValue("@fleet_id", (object?)session.FleetId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@dock_id", (object?)session.DockId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", (object?)session.BranchName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@title", session.Title);
                    cmd.Parameters.AddWithValue("@status", session.Status.ToString());
                    cmd.Parameters.AddWithValue("@pipeline_id", (object?)session.PipelineId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@selected_playbooks_json", session.SerializeSelectedPlaybooks());
                    cmd.Parameters.AddWithValue("@process_id", session.ProcessId.HasValue ? (object)session.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@failure_reason", (object?)session.FailureReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@started_utc", session.StartedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(session.StartedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", session.CompletedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(session.CompletedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(session.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return session;
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
                    cmd.CommandText = "DELETE FROM planning_sessions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<PlanningSession>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync("SELECT * FROM planning_sessions ORDER BY last_update_utc DESC;", null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<PlanningSession>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            return await EnumerateInternalAsync(
                "SELECT * FROM planning_sessions WHERE captain_id = @captain_id ORDER BY last_update_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@captain_id", captainId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<PlanningSession>> EnumerateByStatusAsync(PlanningSessionStatusEnum status, CancellationToken token = default)
        {
            return await EnumerateInternalAsync(
                "SELECT * FROM planning_sessions WHERE status = @status ORDER BY last_update_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@status", status.ToString()),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<PlanningSession?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM planning_sessions WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqliteDatabaseDriver.PlanningSessionFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<List<PlanningSession>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync(
                "SELECT * FROM planning_sessions WHERE tenant_id = @tenantId ORDER BY last_update_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<PlanningSession?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM planning_sessions WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqliteDatabaseDriver.PlanningSessionFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<List<PlanningSession>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync(
                "SELECT * FROM planning_sessions WHERE tenant_id = @tenantId AND user_id = @userId ORDER BY last_update_utc DESC;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                },
                token).ConfigureAwait(false);
        }

        private async Task<List<PlanningSession>> EnumerateInternalAsync(string sql, Action<SqliteCommand>? parameterizer, CancellationToken token)
        {
            List<PlanningSession> results = new List<PlanningSession>();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterizer?.Invoke(cmd);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqliteDatabaseDriver.PlanningSessionFromReader(reader));
                    }
                }
            }

            return results;
        }
    }
}
