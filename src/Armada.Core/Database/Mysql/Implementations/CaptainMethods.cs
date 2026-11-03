namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of captain database operations.
    /// </summary>
    public partial class CaptainMethods : ICaptainMethods
    {
        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-dd HH:mm:ss.ffffff";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a MySQL connection string.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public CaptainMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a captain.
        /// </summary>
        /// <param name="captain">Captain to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created captain.</returns>
        public async Task<Captain> CreateAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            captain.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO captains (id, tenant_id, user_id, name, runtime, model, model_endpoint_id, reasoning_effort, tier, system_instructions, allowed_personas, preferred_persona, runtime_options_json, cli_permission_policy, max_concurrency, current_session_id, state, process_id, recovery_attempts, last_heartbeat_utc, last_process_alive_utc, quarantine_until_utc, quarantine_reason, created_utc, last_update_utc, usage_capture_enabled, usage_capture_interval_seconds, usage_capture_mode, usage_capture_command, usage_capture_harbor_id, active)
                        VALUES (@id, @tenant_id, @user_id, @name, @runtime, @model, @model_endpoint_id, @reasoning_effort, @tier, @system_instructions, @allowed_personas, @preferred_persona, @runtime_options_json, @cli_permission_policy, @max_concurrency, @current_session_id, @state, @process_id, @recovery_attempts, @last_heartbeat_utc, @last_process_alive_utc, @quarantine_until_utc, @quarantine_reason, @created_utc, @last_update_utc, @usage_capture_enabled, @usage_capture_interval_seconds, @usage_capture_mode, @usage_capture_command, @usage_capture_harbor_id, @active);";
                    cmd.Parameters.AddWithValue("@id", captain.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)captain.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)captain.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", captain.Name);
                    cmd.Parameters.AddWithValue("@runtime", captain.Runtime.ToString());
                    cmd.Parameters.AddWithValue("@model", (object?)captain.Model ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@model_endpoint_id", (object?)captain.ModelEndpointId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@reasoning_effort", (object?)captain.ReasoningEffort?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@tier", (object?)captain.Tier?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@system_instructions", (object?)captain.SystemInstructions ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@allowed_personas", (object?)captain.AllowedPersonas ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@preferred_persona", (object?)captain.PreferredPersona ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@max_concurrency", captain.MaxConcurrency);
                    cmd.Parameters.AddWithValue("@current_session_id", (object?)captain.CurrentSessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@runtime_options_json", (object?)captain.RuntimeOptionsJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@cli_permission_policy", (object?)captain.CliPermissionPolicy?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@state", captain.State.ToString());
                    cmd.Parameters.AddWithValue("@process_id", captain.CurrentSessionId != null && captain.ProcessId.HasValue ? (object)captain.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@recovery_attempts", captain.RecoveryAttempts);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", captain.LastHeartbeatUtc.HasValue ? (object)ToIso8601(captain.LastHeartbeatUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_process_alive_utc", captain.LastProcessAliveUtc.HasValue ? (object)ToIso8601(captain.LastProcessAliveUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_until_utc", captain.QuarantineUntilUtc.HasValue ? (object)ToIso8601(captain.QuarantineUntilUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_reason", (object?)captain.QuarantineReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(captain.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(captain.LastUpdateUtc));
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

        /// <summary>
        /// Read a captain by identifier.
        /// </summary>
        /// <param name="id">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Captain or null if not found.</returns>
        public async Task<Captain?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Read a captain by name.
        /// </summary>
        /// <param name="name">Captain name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Captain or null if not found.</returns>
        public async Task<Captain?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a captain.
        /// </summary>
        /// <param name="captain">Captain with updated values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated captain.</returns>
        /// <param name="updateConcurrency">Persist an intentional concurrency configuration edit.</param>
        /// <param name="updateActive">True only for an intentional Active configuration edit.</param>
        public async Task<Captain> UpdateAsync(Captain captain, CancellationToken token = default, bool updateActive = false, bool updateConcurrency = false)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            captain.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET
                        tenant_id = @tenant_id,
                            user_id = @user_id,
                        name = @name,
                        runtime = @runtime,
                        model = @model,
                        model_endpoint_id = @model_endpoint_id,
                        reasoning_effort = @reasoning_effort,
                        tier = @tier,
                        system_instructions = @system_instructions,
                        allowed_personas = @allowed_personas,
                        preferred_persona = @preferred_persona,
                        runtime_options_json = @runtime_options_json,
                            max_concurrency = CASE WHEN @update_concurrency = 1 THEN @max_concurrency ELSE max_concurrency END,
                            current_session_id = current_session_id,
                        state = CASE WHEN current_session_id IS NOT NULL THEN state WHEN active_mission_count > 0 AND state IN ('Quarantined','Stopping') AND @state = 'Working' THEN state WHEN active_mission_count > 0 AND @state = 'Idle' THEN CASE WHEN state='Quarantined' AND @quarantine_until_utc IS NULL AND @quarantine_reason IS NULL THEN 'Working' ELSE state END WHEN active_mission_count > 0 AND @state IN ('Planning','Refining','Analyzing') THEN state WHEN active_mission_count > 1 AND @state = 'Stalled' THEN 'Working' ELSE @state END,
                        process_id = CASE WHEN current_session_id IS NOT NULL THEN process_id ELSE NULL END,
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
                        WHERE id = @id AND ((current_session_id IS NULL AND @current_session_id IS NULL) OR current_session_id = @current_session_id);";
                    cmd.Parameters.AddWithValue("@id", captain.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)captain.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)captain.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", captain.Name);
                    cmd.Parameters.AddWithValue("@runtime", captain.Runtime.ToString());
                    cmd.Parameters.AddWithValue("@model", (object?)captain.Model ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@model_endpoint_id", (object?)captain.ModelEndpointId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@reasoning_effort", (object?)captain.ReasoningEffort?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@tier", (object?)captain.Tier?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@system_instructions", (object?)captain.SystemInstructions ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@allowed_personas", (object?)captain.AllowedPersonas ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@preferred_persona", (object?)captain.PreferredPersona ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@max_concurrency", captain.MaxConcurrency);
                    cmd.Parameters.AddWithValue("@current_session_id", (object?)captain.CurrentSessionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@runtime_options_json", (object?)captain.RuntimeOptionsJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@state", captain.State.ToString());
                    cmd.Parameters.AddWithValue("@process_id", captain.ProcessId.HasValue ? (object)captain.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@recovery_attempts", captain.RecoveryAttempts);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", captain.LastHeartbeatUtc.HasValue ? (object)ToIso8601(captain.LastHeartbeatUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_process_alive_utc", captain.LastProcessAliveUtc.HasValue ? (object)ToIso8601(captain.LastProcessAliveUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_until_utc", captain.QuarantineUntilUtc.HasValue ? (object)ToIso8601(captain.QuarantineUntilUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@quarantine_reason", (object?)captain.QuarantineReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_enabled", (object?)captain.UsageCaptureEnabled ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_interval_seconds", captain.UsageCaptureIntervalSeconds);
                    cmd.Parameters.AddWithValue("@usage_capture_mode", captain.UsageCaptureMode.ToString());
                    cmd.Parameters.AddWithValue("@usage_capture_command", (object?)captain.UsageCaptureCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usage_capture_harbor_id", (object?)captain.UsageCaptureHarborId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@active", captain.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@update_active", updateActive ? 1 : 0);
                    cmd.Parameters.AddWithValue("@update_concurrency", updateConcurrency ? 1 : 0);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(captain.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return captain;
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCliPermissionPolicyAsync(string id, CliPermissionPolicyEnum? policy, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE captains SET cli_permission_policy = @policy, last_update_utc = @now WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@policy", (object?)policy?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@now", ToIso8601(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> UpdateUsageCaptureHarborAsync(string tenantId, string id, string? harborId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE captains SET usage_capture_harbor_id = @policy, last_update_utc = @now WHERE id = @id AND tenant_id = @tenant;";
                    cmd.Parameters.AddWithValue("@policy", (object?)harborId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@now", ToIso8601(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@tenant", tenantId);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
                }
            }
        }

        /// <summary>
        /// Delete a captain by identifier.
        /// </summary>
        /// <param name="id">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM captains WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all captains.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all captains.</returns>
        public async Task<List<Captain>> EnumerateAsync(CancellationToken token = default)
        {
            List<Captain> results = new List<Captain>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains ORDER BY name;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate captains with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<Captain>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("state = @state");
                    parameters.Add(new MySqlParameter("@state", query.Status));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                // Query
                List<Captain> results = new List<Captain>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
                    }
                }

                return EnumerationResult<Captain>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Enumerate captains by state.
        /// </summary>
        /// <param name="state">Captain state to filter by.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of captains in the specified state.</returns>
        public async Task<List<Captain>> EnumerateByStateAsync(CaptainStateEnum state, CancellationToken token = default)
        {
            List<Captain> results = new List<Captain>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE state = @state ORDER BY name;";
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Update captain state.
        /// </summary>
        /// <param name="id">Captain identifier.</param>
        /// <param name="state">New captain state.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task UpdateStateAsync(string id, CaptainStateEnum state, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET state = CASE WHEN current_session_id IS NOT NULL AND @state IN ('Idle','Working','Stalled') THEN state WHEN @state='Idle' AND active_mission_count>0 THEN 'Working' ELSE @state END, last_update_utc = @last_update_utc WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(DateTime.UtcNow));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Update captain heartbeat timestamp.
        /// </summary>
        /// <param name="id">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task UpdateHeartbeatAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DateTime now = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET last_heartbeat_utc = @last_heartbeat_utc, last_update_utc = @last_update_utc WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", ToIso8601(now));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(now));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task UpdateProcessAliveAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DateTime now = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET last_process_alive_utc = @last_process_alive_utc, last_update_utc = @last_update_utc WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@last_process_alive_utc", ToIso8601(now));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(now));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Check if a captain exists by identifier.
        /// </summary>
        /// <param name="id">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the captain exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <summary>
        /// Read a captain by tenant and identifier (tenant-scoped).
        /// </summary>
        public async Task<Captain?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return CaptainFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Delete a captain by tenant and identifier (tenant-scoped).
        /// </summary>
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM captains WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all captains in a tenant (tenant-scoped).
        /// </summary>
        public async Task<List<Captain>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<Captain> results = new List<Captain>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate captains with pagination and filtering (tenant-scoped).
        /// </summary>
        public async Task<EnumerationResult<Captain>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) query = new EnumerationQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId" };
                List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@tenantId", tenantId) };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("state = @state");
                    parameters.Add(new MySqlParameter("@state", query.Status));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Captain> results = new List<Captain>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
                    }
                }

                return EnumerationResult<Captain>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<Captain?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return CaptainFromReader(reader);
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND state = @state ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET state = CASE WHEN current_session_id IS NOT NULL AND @state IN ('Idle','Working','Stalled') THEN state WHEN @state='Idle' AND active_mission_count>0 THEN 'Working' ELSE @state END, last_update_utc = @last_update_utc WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(DateTime.UtcNow));
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET last_heartbeat_utc = @last_heartbeat_utc, last_update_utc = @last_update_utc WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@last_heartbeat_utc", ToIso8601(now));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(now));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<Captain?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return CaptainFromReader(reader);
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains WHERE tenant_id = @tenantId AND user_id = @userId ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId", "user_id = @userId" };
                List<MySqlParameter> parameters = new List<MySqlParameter>
                {
                    new MySqlParameter("@tenantId", tenantId),
                    new MySqlParameter("@userId", userId)
                };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("state = @state");
                    parameters.Add(new MySqlParameter("@state", query.Status));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM captains" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Captain> results = new List<Captain>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT captains.*, COALESCE((SELECT JSON_ARRAYAGG(s.mission_id) FROM captain_mission_slots s WHERE s.captain_id=captains.id AND (EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id AND vm.status IN ('Assigned','InProgress')) OR NOT EXISTS (SELECT 1 FROM missions vm WHERE vm.id=s.mission_id))),'[]') AS active_mission_ids FROM captains" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(CaptainFromReader(reader));
                    }
                }

                return EnumerationResult<Captain>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryClaimAsync(string tenantId, string captainId, string missionId, string dockId, CancellationToken token = default)
        {
            if (await ReadAsync(tenantId, captainId, token).ConfigureAwait(false) == null) return false;
            return await TryClaimAsync(captainId, missionId, dockId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TryReserveAsync(string tenantId, string captainId, CaptainStateEnum state, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (state == CaptainStateEnum.Idle) throw new ArgumentException("Reserving a captain requires a non-Idle state.", nameof(state));
            DateTime now = DateTime.UtcNow;
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET
                        state = @state,
                        last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenantId AND id = @id AND active_mission_count = 0 AND current_session_id IS NULL AND state = 'Idle' AND active = 1;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", captainId);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(now));
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE captains SET
                        state = 'Idle',
                        process_id = NULL,
                        last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenantId AND id = @id AND active_mission_count = 0 AND current_session_id IS NULL AND state = @state;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", captainId);
                    cmd.Parameters.AddWithValue("@state", state.ToString());
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(now));
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected > 0;
                }
            }
        }

        /// <summary>
        /// Atomically claim a captain for a mission. Sets state to Working and assigns
        /// mission/dock IDs when a non-exclusive captain has capacity.
        /// Returns true if the claim succeeded, false if no slot is available.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="missionId">Mission identifier to assign.</param>
        /// <param name="dockId">Dock identifier to assign.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="ceiling">Effective server-wide captain concurrency ceiling.</param>
        /// <returns>True if claim succeeded, false otherwise.</returns>
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
            return await WithSlotsAsync(store => store.SetAssignmentAsync(captainId, missionId, "dock_id", dockId, token), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TrySetProcessAsync(string captainId, string missionId, int processId, CancellationToken token = default)
        {
            return await WithSlotsAsync(store => store.SetAssignmentAsync(captainId, missionId, "process_id", processId, token), token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        private static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        private static DateTime? FromIso8601Nullable(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            string str = value.ToString()!;
            if (string.IsNullOrEmpty(str)) return null;
            return FromIso8601(str);
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static int? NullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        private static Captain CaptainFromReader(MySqlDataReader reader)
        {
            Captain captain = new Captain();
            captain.Id = reader["id"].ToString()!;
            captain.TenantId = NullableString(reader["tenant_id"]);
            captain.UserId = NullableString(reader["user_id"]);
            captain.Name = reader["name"].ToString()!;
            captain.Runtime = Enum.Parse<AgentRuntimeEnum>(reader["runtime"].ToString()!);
            captain.MaxConcurrency = Convert.ToInt32(reader["max_concurrency"]);
            captain.ActiveMissionCount = Convert.ToInt32(reader["active_mission_count"]);
            captain.CurrentSessionId = reader["current_session_id"] == DBNull.Value ? null : reader["current_session_id"].ToString();
            try { captain.ActiveMissionIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader["active_mission_ids"].ToString() ?? "[]") ?? new List<string>(); } catch (IndexOutOfRangeException) { }

            try { captain.Model = NullableString(reader["model"]); } catch { }
            try { captain.ModelEndpointId = NullableString(reader["model_endpoint_id"]); } catch { }
            try { captain.ModelProfileId = NullableString(reader["model_profile_id"]); } catch { }
            try
            {
                string? reasoningEffort = NullableString(reader["reasoning_effort"]);
                if (!String.IsNullOrEmpty(reasoningEffort) && Enum.TryParse<ReasoningEffortEnum>(reasoningEffort, out ReasoningEffortEnum parsedEffort))
                    captain.ReasoningEffort = parsedEffort;
            }
            catch { }
            try
            {
                string? tier = NullableString(reader["tier"]);
                if (!String.IsNullOrEmpty(tier) && Enum.TryParse<CaptainTierEnum>(tier, out CaptainTierEnum parsedTier))
                    captain.Tier = parsedTier;
            }
            catch { }
            captain.SystemInstructions = NullableString(reader["system_instructions"]);
            captain.State = Enum.Parse<CaptainStateEnum>(reader["state"].ToString()!);
            captain.ProcessId = NullableInt(reader["process_id"]);
            captain.RecoveryAttempts = Convert.ToInt32(reader["recovery_attempts"]);
            captain.LastHeartbeatUtc = FromIso8601Nullable(reader["last_heartbeat_utc"]);
            try { captain.LastProcessAliveUtc = FromIso8601Nullable(reader["last_process_alive_utc"]); } catch { }
            try { captain.QuarantineUntilUtc = FromIso8601Nullable(reader["quarantine_until_utc"]); } catch { }
            try { captain.QuarantineReason = NullableString(reader["quarantine_reason"]); } catch { }
            captain.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            captain.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            try { captain.AllowedPersonas = NullableString(reader["allowed_personas"]); } catch { }
            try { captain.PreferredPersona = NullableString(reader["preferred_persona"]); } catch { }
            try { captain.RuntimeOptionsJson = NullableString(reader["runtime_options_json"]); } catch { }
            try
            {
                string? cliPolicy = NullableString(reader["cli_permission_policy"]);
                captain.CliPermissionPolicy = !String.IsNullOrEmpty(cliPolicy) && Enum.TryParse<CliPermissionPolicyEnum>(cliPolicy, out CliPermissionPolicyEnum parsedPolicy) ? parsedPolicy : (CliPermissionPolicyEnum?)null;
            }
            catch { captain.CliPermissionPolicy = null; }
            try { captain.UsageCaptureEnabled = reader["usage_capture_enabled"] == DBNull.Value ? null : Convert.ToBoolean(reader["usage_capture_enabled"]); } catch { }
            try { captain.UsageCaptureIntervalSeconds = Convert.ToInt32(reader["usage_capture_interval_seconds"]); } catch { }
            try
            {
                string? captureMode = NullableString(reader["usage_capture_mode"]);
                if (!String.IsNullOrEmpty(captureMode) && Enum.TryParse<UsageCaptureModeEnum>(captureMode, out UsageCaptureModeEnum parsedMode))
                    captain.UsageCaptureMode = parsedMode;
            }
            catch { }
            try { captain.UsageCaptureCommand = NullableString(reader["usage_capture_command"]); } catch { }
            captain.UsageCaptureHarborId = NullableString(reader["usage_capture_harbor_id"]);
            try { captain.Active = Convert.ToBoolean(reader["active"]); } catch { }
            return captain;
        }

        #endregion
    }
}

