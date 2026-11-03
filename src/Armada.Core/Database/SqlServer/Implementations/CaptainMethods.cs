namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// SQL Server implementation of captain database operations.
    /// </summary>
    public partial class CaptainMethods : ICaptainMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private readonly string _Header = "[CaptainMethods] ";
#pragma warning restore CS0414
        private readonly SqlServerDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate captain methods for SQL Server.
        /// </summary>
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public CaptainMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Captain> CreateAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            captain.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO captains (id, tenant_id, user_id, name, runtime, model, model_endpoint_id, system_instructions, allowed_personas, preferred_persona, runtime_options_json, cli_permission_policy, reasoning_effort, tier, state, current_mission_id, current_dock_id, process_id, recovery_attempts, last_heartbeat_utc, last_process_alive_utc, quarantine_until_utc, quarantine_reason, created_utc, last_update_utc, usage_capture_enabled, usage_capture_interval_seconds, usage_capture_mode, usage_capture_command, usage_capture_harbor_id, active)
                        VALUES (@id, @tenant_id, @user_id, @name, @runtime, @model, @model_endpoint_id, @system_instructions, @allowed_personas, @preferred_persona, @runtime_options_json, @cli_permission_policy, @reasoning_effort, @tier, @state, @current_mission_id, @current_dock_id, @process_id, @recovery_attempts, @last_heartbeat_utc, @last_process_alive_utc, @quarantine_until_utc, @quarantine_reason, @created_utc, @last_update_utc, @usage_capture_enabled, @usage_capture_interval_seconds, @usage_capture_mode, @usage_capture_command, @usage_capture_harbor_id, @active);";
                    cmd.Parameters.AddWithValue("@id", captain.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)captain.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)captain.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", captain.Name);
                    cmd.Parameters.AddWithValue("@runtime", captain.Runtime.ToString());
                    cmd.Parameters.AddWithValue("@model", (object?)captain.Model ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@model_endpoint_id", (object?)captain.ModelEndpointId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@system_instructions", (object?)captain.SystemInstructions ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@allowed_personas", (object?)captain.AllowedPersonas ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@preferred_persona", (object?)captain.PreferredPersona ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@max_concurrency", captain.MaxConcurrency);
                    cmd.Parameters.AddWithValue("@current_session_id", (object?)captain.CurrentSessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@runtime_options_json", (object?)captain.RuntimeOptionsJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@cli_permission_policy", (object?)captain.CliPermissionPolicy?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@reasoning_effort", (object?)captain.ReasoningEffort?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@tier", (object?)captain.Tier?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@state", captain.State.ToString());
                    cmd.Parameters.AddWithValue("@current_mission_id", (object?)captain.CurrentMissionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@current_dock_id", (object?)captain.CurrentDockId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@process_id", captain.ProcessId.HasValue ? (object)captain.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@recovery_attempts", captain.RecoveryAttempts);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", captain.LastHeartbeatUtc.HasValue ? (object)SqlServerDatabaseDriver.ToIso8601(captain.LastHeartbeatUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_process_alive_utc", captain.LastProcessAliveUtc.HasValue ? (object)captain.LastProcessAliveUtc.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_until_utc", captain.QuarantineUntilUtc.HasValue ? (object)SqlServerDatabaseDriver.ToIso8601(captain.QuarantineUntilUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_reason", (object?)captain.QuarantineReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created_utc", SqlServerDatabaseDriver.ToIso8601(captain.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(captain.LastUpdateUtc));
                    cmd.Parameters.AddWithValue("@usage_capture_enabled", (object?)captain.UsageCaptureEnabled ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_interval_seconds", captain.UsageCaptureIntervalSeconds);
                    cmd.Parameters.AddWithValue("@usage_capture_mode", captain.UsageCaptureMode.ToString());
                    cmd.Parameters.AddWithValue("@usage_capture_command", (object?)captain.UsageCaptureCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_harbor_id", (object?)captain.UsageCaptureHarborId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@active", captain.Active ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return captain;
        }

        /// <inheritdoc />
        public async Task<Captain?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqlServerDatabaseDriver.CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<Captain?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqlServerDatabaseDriver.CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<Captain> UpdateAsync(Captain captain, CancellationToken token = default, bool updateActive = false, bool updateConcurrency = false)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            captain.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET
                        tenant_id = @tenant_id,
                            user_id = @user_id,
                        name = @name,
                        runtime = @runtime,
                        model = @model,
                        model_endpoint_id = @model_endpoint_id,
                        system_instructions = @system_instructions,
                        allowed_personas = @allowed_personas,
                        preferred_persona = @preferred_persona,
                        runtime_options_json = @runtime_options_json,
                            max_concurrency = CASE WHEN @update_concurrency = 1 THEN @max_concurrency ELSE max_concurrency END,
                            current_session_id = @current_session_id,
                        reasoning_effort = @reasoning_effort,
                        tier = @tier,
                        state = CASE WHEN active_mission_count > 0 AND state IN ('Quarantined','Stopping') AND @state = 'Working' THEN state WHEN active_mission_count > 0 AND @state = 'Idle' THEN CASE WHEN state='Quarantined' AND @quarantine_until_utc IS NULL AND @quarantine_reason IS NULL THEN 'Working' ELSE state END WHEN active_mission_count > 0 AND @state IN ('Planning','Refining','Analyzing') THEN state WHEN active_mission_count > 1 AND @state = 'Stalled' THEN 'Working' ELSE @state END,
                        current_mission_id = CASE WHEN active_mission_count > 0 THEN current_mission_id ELSE @current_mission_id END,
                        current_dock_id = CASE WHEN active_mission_count > 0 THEN current_dock_id ELSE @current_dock_id END,
                        process_id = CASE WHEN active_mission_count > 0 THEN process_id ELSE @process_id END,
                        recovery_attempts = @recovery_attempts,
                        last_heartbeat_utc = @last_heartbeat_utc,
                        last_process_alive_utc = @last_process_alive_utc,
                        quarantine_until_utc = @quarantine_until_utc,
                        quarantine_reason = @quarantine_reason,
                        usage_capture_enabled = @usage_capture_enabled,
                        usage_capture_interval_seconds = @usage_capture_interval_seconds,
                        usage_capture_mode = @usage_capture_mode,
                        usage_capture_command = @usage_capture_command,
                        usage_capture_harbor_id = @usage_capture_harbor_id,
                        active = CASE WHEN @update_active = 1 THEN @active ELSE active END,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", captain.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)captain.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)captain.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", captain.Name);
                    cmd.Parameters.AddWithValue("@runtime", captain.Runtime.ToString());
                    cmd.Parameters.AddWithValue("@model", (object?)captain.Model ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@model_endpoint_id", (object?)captain.ModelEndpointId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@system_instructions", (object?)captain.SystemInstructions ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@allowed_personas", (object?)captain.AllowedPersonas ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@preferred_persona", (object?)captain.PreferredPersona ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@max_concurrency", captain.MaxConcurrency);
                    cmd.Parameters.AddWithValue("@current_session_id", (object?)captain.CurrentSessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@runtime_options_json", (object?)captain.RuntimeOptionsJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@reasoning_effort", (object?)captain.ReasoningEffort?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@tier", (object?)captain.Tier?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@state", captain.State.ToString());
                    cmd.Parameters.AddWithValue("@current_mission_id", (object?)captain.CurrentMissionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@current_dock_id", (object?)captain.CurrentDockId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@process_id", captain.ProcessId.HasValue ? (object)captain.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@recovery_attempts", captain.RecoveryAttempts);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", captain.LastHeartbeatUtc.HasValue ? (object)SqlServerDatabaseDriver.ToIso8601(captain.LastHeartbeatUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_process_alive_utc", captain.LastProcessAliveUtc.HasValue ? (object)captain.LastProcessAliveUtc.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_until_utc", captain.QuarantineUntilUtc.HasValue ? (object)SqlServerDatabaseDriver.ToIso8601(captain.QuarantineUntilUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_reason", (object?)captain.QuarantineReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_enabled", (object?)captain.UsageCaptureEnabled ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_interval_seconds", captain.UsageCaptureIntervalSeconds);
                    cmd.Parameters.AddWithValue("@usage_capture_mode", captain.UsageCaptureMode.ToString());
                    cmd.Parameters.AddWithValue("@usage_capture_command", (object?)captain.UsageCaptureCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_harbor_id", (object?)captain.UsageCaptureHarborId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@active", captain.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@update_active", updateActive ? 1 : 0);
                    cmd.Parameters.AddWithValue("@update_concurrency", updateConcurrency ? 1 : 0);
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(captain.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return captain;
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCliPermissionPolicyAsync(string id, CliPermissionPolicyEnum? policy, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE captains SET cli_permission_policy = @policy, last_update_utc = @now WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@policy", (object?)policy?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@now", SqlServerDatabaseDriver.ToIso8601(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> UpdateUsageCaptureHarborAsync(string tenantId, string id, string? harborId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE captains SET usage_capture_harbor_id = @policy, last_update_utc = @now WHERE id = @id AND tenant_id = @tenant;";
                    cmd.Parameters.AddWithValue("@policy", (object?)harborId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@now", SqlServerDatabaseDriver.ToIso8601(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@tenant", tenantId);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    // SQL Server forbids two ON DELETE SET NULL foreign keys from one table to the same parent
                    // (error 1785, "multiple cascade paths"), so the signals->captains FKs are NO ACTION and the
                    // null-on-delete that PostgreSQL does at the DB level is done here in application code.
                    cmd.CommandText =
                        "UPDATE signals SET from_captain_id = NULL WHERE from_captain_id = @id; " +
                        "UPDATE signals SET to_captain_id = NULL WHERE to_captain_id = @id; " +
                        "DELETE FROM captains WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<Captain>> EnumerateAsync(CancellationToken token = default)
        {
            List<Captain> results = new List<Captain>();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains ORDER BY name;";
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<List<Captain>> EnumerateByStateAsync(CaptainStateEnum state, CancellationToken token = default)
        {
            List<Captain> results = new List<Captain>();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE state = @state ORDER BY name;";
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task UpdateStateAsync(string id, CaptainStateEnum state, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET state = CASE WHEN @state='Idle' AND active_mission_count>0 THEN 'Working' ELSE @state END, last_update_utc = @last_update_utc WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(DateTime.UtcNow));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task UpdateHeartbeatAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DateTime now = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET last_heartbeat_utc = @last_heartbeat_utc, last_update_utc = @last_update_utc WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task UpdateProcessAliveAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DateTime now = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    // last_process_alive_utc is a native DATETIME2 column; last_update_utc keeps the
                    // provider's existing ISO-8601 NVARCHAR convention.
                    cmd.CommandText = @"UPDATE captains SET last_process_alive_utc = @last_process_alive_utc, last_update_utc = @last_update_utc WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@last_process_alive_utc", now);
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Captain>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new SqlParameter("@created_after", SqlServerDatabaseDriver.ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new SqlParameter("@created_before", SqlServerDatabaseDriver.ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("state = @state");
                    parameters.Add(new SqlParameter("@state", query.Status));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                // Query
                List<Captain> results = new List<Captain>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }

                return EnumerationResult<Captain>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    int count = Convert.ToInt32(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<Captain?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqlServerDatabaseDriver.CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    // See DeleteAsync(id): null the signals->captains references in application code because
                    // SQL Server cannot express two ON DELETE SET NULL FKs from signals to captains.
                    cmd.CommandText =
                        "UPDATE signals SET from_captain_id = NULL WHERE from_captain_id = @id; " +
                        "UPDATE signals SET to_captain_id = NULL WHERE to_captain_id = @id; " +
                        "DELETE FROM captains WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<Captain>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<Captain> results = new List<Captain>();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<Captain?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqlServerDatabaseDriver.CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<List<Captain>> EnumerateByStateAsync(string tenantId, CaptainStateEnum state, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<Captain> results = new List<Captain>();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND state = @state ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task UpdateStateAsync(string tenantId, string id, CaptainStateEnum state, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET state = CASE WHEN @state='Idle' AND active_mission_count>0 THEN 'Working' ELSE @state END, last_update_utc = @last_update_utc WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(DateTime.UtcNow));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task UpdateHeartbeatAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DateTime now = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET last_heartbeat_utc = @last_heartbeat_utc, last_update_utc = @last_update_utc WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    int count = Convert.ToInt32(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryClaimAsync(string tenantId, string captainId, string missionId, string dockId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (string.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (string.IsNullOrEmpty(dockId)) throw new ArgumentNullException(nameof(dockId));

            DateTime now = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET
                        state = @state,
                        current_mission_id = @current_mission_id,
                        current_dock_id = @current_dock_id,
                        last_heartbeat_utc = @last_heartbeat_utc,
                        last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenantId AND id = @id AND active_mission_count = 0 AND state = 'Idle' AND active = 1;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", captainId);
                    cmd.Parameters.AddWithValue("@state", CaptainStateEnum.Working.ToString());
                    cmd.Parameters.AddWithValue("@current_mission_id", missionId);
                    cmd.Parameters.AddWithValue("@current_dock_id", dockId);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryReserveAsync(string tenantId, string captainId, CaptainStateEnum state, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (state == CaptainStateEnum.Idle) throw new ArgumentException("Reserving a captain requires a non-Idle state.", nameof(state));
            DateTime now = DateTime.UtcNow;
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET
                        state = @state,
                        last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenantId AND id = @id AND active_mission_count = 0 AND state = 'Idle' AND active = 1;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", captainId);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryReleaseAsync(string tenantId, string captainId, CaptainStateEnum state, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            DateTime now = DateTime.UtcNow;
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET
                        state = 'Idle',
                        process_id = NULL,
                        last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenantId AND id = @id AND state = @state;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", captainId);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(now));
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Captain>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) query = new EnumerationQuery();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId" };
                List<SqlParameter> parameters = new List<SqlParameter> { new SqlParameter("@tenantId", tenantId) };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new SqlParameter("@created_after", SqlServerDatabaseDriver.ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new SqlParameter("@created_before", SqlServerDatabaseDriver.ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("state = @state");
                    parameters.Add(new SqlParameter("@state", query.Status));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Captain> results = new List<Captain>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains" + whereClause + " ORDER BY created_utc " + orderDirection + " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }

                return EnumerationResult<Captain>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<Captain?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqlServerDatabaseDriver.CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM captains WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<Captain>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            List<Captain> results = new List<Captain>();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND user_id = @userId ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Captain>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (query == null) query = new EnumerationQuery();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId", "user_id = @userId" };
                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@tenantId", tenantId),
                    new SqlParameter("@userId", userId)
                };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new SqlParameter("@created_after", SqlServerDatabaseDriver.ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new SqlParameter("@created_before", SqlServerDatabaseDriver.ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("state = @state");
                    parameters.Add(new SqlParameter("@state", query.Status));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Captain> results = new List<Captain>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT '[' + STRING_AGG('\"' + STRING_ESCAPE(s.mission_id,'json') + '\"',',') + ']' FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains" + whereClause + " ORDER BY created_utc " + orderDirection + " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqlServerDatabaseDriver.CaptainFromReader(reader));
                    }
                }

                return EnumerationResult<Captain>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryClaimAsync(string captainId, string missionId, string? dockId, CancellationToken token = default, int ceiling = 256)
        {
            return await WithSlotsAsync(store => store.ClaimAsync(captainId, missionId, dockId, token, ceiling), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TryReleaseFromMissionAsync(string captainId, string missionId, CancellationToken token = default, CaptainStateEnum? emptyState = null)
        {
            return await WithSlotsAsync(store => store.ReleaseAsync(captainId, missionId, token, emptyState), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TrySetDockAsync(string captainId, string missionId, string dockId, CancellationToken token = default)
        {
            return await WithSlotsAsync(store => store.SetAssignmentAsync(captainId, missionId, "current_dock_id", dockId, token), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TrySetProcessAsync(string captainId, string missionId, int processId, CancellationToken token = default)
        {
            return await WithSlotsAsync(store => store.SetAssignmentAsync(captainId, missionId, "process_id", processId, token), token).ConfigureAwait(false);
        }

        #endregion
    }
}

