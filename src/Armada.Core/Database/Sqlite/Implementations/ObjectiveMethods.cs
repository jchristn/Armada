namespace Armada.Core.Database.Sqlite.Implementations
{
    using System.Text.Json;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of normalized objective persistence.
    /// </summary>
    public class ObjectiveMethods : IObjectiveMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectiveMethods"/> class.
        /// </summary>
        public ObjectiveMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<Objective> CreateAsync(Objective objective, CancellationToken token = default)
        {
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO objectives
                        (id, tenant_id, user_id, title, description, status, kind, category, priority, rank, backlog_state, effort, owner, target_version, due_utc, parent_objective_id, blocked_by_objective_ids_json, refinement_summary, suggested_pipeline_id, suggested_playbooks_json, tags_json, acceptance_criteria_json, non_goals_json, rollout_constraints_json, evidence_links_json, fleet_ids_json, vessel_ids_json, planning_session_ids_json, refinement_session_ids_json, voyage_ids_json, mission_ids_json, check_run_ids_json, release_ids_json, deployment_ids_json, incident_ids_json, source_provider, source_type, source_id, source_url, source_updated_utc, created_utc, last_update_utc, completed_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @title, @description, @status, @kind, @category, @priority, @rank, @backlog_state, @effort, @owner, @target_version, @due_utc, @parent_objective_id, @blocked_by_objective_ids_json, @refinement_summary, @suggested_pipeline_id, @suggested_playbooks_json, @tags_json, @acceptance_criteria_json, @non_goals_json, @rollout_constraints_json, @evidence_links_json, @fleet_ids_json, @vessel_ids_json, @planning_session_ids_json, @refinement_session_ids_json, @voyage_ids_json, @mission_ids_json, @check_run_ids_json, @release_ids_json, @deployment_ids_json, @incident_ids_json, @source_provider, @source_type, @source_id, @source_url, @source_updated_utc, @created_utc, @last_update_utc, @completed_utc);";
                    BindObjective(cmd, objective);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return objective;
        }

        /// <inheritdoc />
        public async Task<Objective> UpdateAsync(Objective objective, CancellationToken token = default)
        {
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE objectives SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        title = @title,
                        description = @description,
                        status = @status,
                        kind = @kind,
                        category = @category,
                        priority = @priority,
                        rank = @rank,
                        backlog_state = @backlog_state,
                        effort = @effort,
                        owner = @owner,
                        target_version = @target_version,
                        due_utc = @due_utc,
                        parent_objective_id = @parent_objective_id,
                        blocked_by_objective_ids_json = @blocked_by_objective_ids_json,
                        refinement_summary = @refinement_summary,
                        suggested_pipeline_id = @suggested_pipeline_id,
                        suggested_playbooks_json = @suggested_playbooks_json,
                        tags_json = @tags_json,
                        acceptance_criteria_json = @acceptance_criteria_json,
                        non_goals_json = @non_goals_json,
                        rollout_constraints_json = @rollout_constraints_json,
                        evidence_links_json = @evidence_links_json,
                        fleet_ids_json = @fleet_ids_json,
                        vessel_ids_json = @vessel_ids_json,
                        planning_session_ids_json = @planning_session_ids_json,
                        refinement_session_ids_json = @refinement_session_ids_json,
                        voyage_ids_json = @voyage_ids_json,
                        mission_ids_json = @mission_ids_json,
                        check_run_ids_json = @check_run_ids_json,
                        release_ids_json = @release_ids_json,
                        deployment_ids_json = @deployment_ids_json,
                        incident_ids_json = @incident_ids_json,
                        source_provider = @source_provider,
                        source_type = @source_type,
                        source_id = @source_id,
                        source_url = @source_url,
                        source_updated_utc = @source_updated_utc,
                        created_utc = @created_utc,
                        last_update_utc = @last_update_utc,
                        completed_utc = @completed_utc
                        WHERE id = @id;";
                    BindObjective(cmd, objective);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return objective;
        }

        /// <inheritdoc />
        public async Task<Objective?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM objectives WHERE id = @id;", cmd => cmd.Parameters.AddWithValue("@id", id), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Objective?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM objectives WHERE tenant_id = @tenant_id AND id = @id;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@id", id);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Objective?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM objectives WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;", cmd =>
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
            await ExecuteDeleteAsync("DELETE FROM objectives WHERE id = @id;", cmd => cmd.Parameters.AddWithValue("@id", id), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync("DELETE FROM objectives WHERE tenant_id = @tenant_id AND id = @id;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@id", id);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Objective>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync("SELECT * FROM objectives ORDER BY rank ASC, priority ASC, last_update_utc DESC;", null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Objective>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync("SELECT * FROM objectives WHERE tenant_id = @tenant_id ORDER BY rank ASC, priority ASC, last_update_utc DESC;", cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Objective>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync("SELECT * FROM objectives WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY rank ASC, priority ASC, last_update_utc DESC;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@user_id", userId);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAnyAsync(CancellationToken token = default)
        {
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM objectives LIMIT 1;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    return result != null && result != DBNull.Value;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM objectives WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        private async Task<Objective?> ReadInternalAsync(string sql, Action<SqliteCommand> parameterize, CancellationToken token)
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
                            return ObjectiveFromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<Objective>> EnumerateInternalAsync(string sql, Action<SqliteCommand>? parameterize, CancellationToken token)
        {
            List<Objective> results = new List<Objective>();
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
                            results.Add(ObjectiveFromReader(reader));
                    }
                }
            }

            return results;
        }

        private async Task ExecuteDeleteAsync(string sql, Action<SqliteCommand> parameterize, CancellationToken token)
        {
            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static void BindObjective(SqliteCommand cmd, Objective objective)
        {
            cmd.Parameters.AddWithValue("@id", objective.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)objective.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)objective.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@title", objective.Title);
            cmd.Parameters.AddWithValue("@description", (object?)objective.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", objective.Status.ToString());
            cmd.Parameters.AddWithValue("@kind", objective.Kind.ToString());
            cmd.Parameters.AddWithValue("@category", (object?)objective.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@priority", objective.Priority.ToString());
            cmd.Parameters.AddWithValue("@rank", objective.Rank);
            cmd.Parameters.AddWithValue("@backlog_state", objective.BacklogState.ToString());
            cmd.Parameters.AddWithValue("@effort", objective.Effort.ToString());
            cmd.Parameters.AddWithValue("@owner", (object?)objective.Owner ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@target_version", (object?)objective.TargetVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@due_utc", objective.DueUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(objective.DueUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@parent_objective_id", (object?)objective.ParentObjectiveId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@blocked_by_objective_ids_json", Serialize(objective.BlockedByObjectiveIds));
            cmd.Parameters.AddWithValue("@refinement_summary", (object?)objective.RefinementSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@suggested_pipeline_id", (object?)objective.SuggestedPipelineId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@suggested_playbooks_json", Serialize(objective.SuggestedPlaybooks));
            cmd.Parameters.AddWithValue("@tags_json", Serialize(objective.Tags));
            cmd.Parameters.AddWithValue("@acceptance_criteria_json", Serialize(objective.AcceptanceCriteria));
            cmd.Parameters.AddWithValue("@non_goals_json", Serialize(objective.NonGoals));
            cmd.Parameters.AddWithValue("@rollout_constraints_json", Serialize(objective.RolloutConstraints));
            cmd.Parameters.AddWithValue("@evidence_links_json", Serialize(objective.EvidenceLinks));
            cmd.Parameters.AddWithValue("@fleet_ids_json", Serialize(objective.FleetIds));
            cmd.Parameters.AddWithValue("@vessel_ids_json", Serialize(objective.VesselIds));
            cmd.Parameters.AddWithValue("@planning_session_ids_json", Serialize(objective.PlanningSessionIds));
            cmd.Parameters.AddWithValue("@refinement_session_ids_json", Serialize(objective.RefinementSessionIds));
            cmd.Parameters.AddWithValue("@voyage_ids_json", Serialize(objective.VoyageIds));
            cmd.Parameters.AddWithValue("@mission_ids_json", Serialize(objective.MissionIds));
            cmd.Parameters.AddWithValue("@check_run_ids_json", Serialize(objective.CheckRunIds));
            cmd.Parameters.AddWithValue("@release_ids_json", Serialize(objective.ReleaseIds));
            cmd.Parameters.AddWithValue("@deployment_ids_json", Serialize(objective.DeploymentIds));
            cmd.Parameters.AddWithValue("@incident_ids_json", Serialize(objective.IncidentIds));
            cmd.Parameters.AddWithValue("@source_provider", (object?)objective.SourceProvider ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_type", (object?)objective.SourceType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_id", (object?)objective.SourceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_url", (object?)objective.SourceUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_updated_utc", objective.SourceUpdatedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(objective.SourceUpdatedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(objective.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(objective.LastUpdateUtc));
            cmd.Parameters.AddWithValue("@completed_utc", objective.CompletedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(objective.CompletedUtc.Value) : DBNull.Value);
        }

        private static Objective ObjectiveFromReader(SqliteDataReader reader)
        {
            Objective objective = new Objective
            {
                Id = reader["id"].ToString()!,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                Title = reader["title"].ToString()!,
                Description = SqliteDatabaseDriver.NullableString(reader["description"]),
                Status = ParseEnum(reader["status"], ObjectiveStatusEnum.Draft),
                Kind = ParseEnum(reader["kind"], ObjectiveKindEnum.Feature),
                Category = SqliteDatabaseDriver.NullableString(reader["category"]),
                Priority = ParseEnum(reader["priority"], ObjectivePriorityEnum.P2),
                Rank = SqliteDatabaseDriver.NullableInt(reader["rank"]) ?? 0,
                BacklogState = ParseEnum(reader["backlog_state"], ObjectiveBacklogStateEnum.Inbox),
                Effort = ParseEnum(reader["effort"], ObjectiveEffortEnum.M),
                Owner = SqliteDatabaseDriver.NullableString(reader["owner"]),
                TargetVersion = SqliteDatabaseDriver.NullableString(reader["target_version"]),
                DueUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["due_utc"]),
                ParentObjectiveId = SqliteDatabaseDriver.NullableString(reader["parent_objective_id"]),
                RefinementSummary = SqliteDatabaseDriver.NullableString(reader["refinement_summary"]),
                SuggestedPipelineId = SqliteDatabaseDriver.NullableString(reader["suggested_pipeline_id"]),
                SourceProvider = SqliteDatabaseDriver.NullableString(reader["source_provider"]),
                SourceType = SqliteDatabaseDriver.NullableString(reader["source_type"]),
                SourceId = SqliteDatabaseDriver.NullableString(reader["source_id"]),
                SourceUrl = SqliteDatabaseDriver.NullableString(reader["source_url"]),
                SourceUpdatedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["source_updated_utc"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!),
                CompletedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["completed_utc"])
            };

            objective.BlockedByObjectiveIds = DeserializeList(reader["blocked_by_objective_ids_json"]);
            objective.SuggestedPlaybooks = DeserializePlaybooks(reader["suggested_playbooks_json"]);
            objective.Tags = DeserializeList(reader["tags_json"]);
            objective.AcceptanceCriteria = DeserializeList(reader["acceptance_criteria_json"]);
            objective.NonGoals = DeserializeList(reader["non_goals_json"]);
            objective.RolloutConstraints = DeserializeList(reader["rollout_constraints_json"]);
            objective.EvidenceLinks = DeserializeList(reader["evidence_links_json"]);
            objective.FleetIds = DeserializeList(reader["fleet_ids_json"]);
            objective.VesselIds = DeserializeList(reader["vessel_ids_json"]);
            objective.PlanningSessionIds = DeserializeList(reader["planning_session_ids_json"]);
            objective.RefinementSessionIds = DeserializeList(reader["refinement_session_ids_json"]);
            objective.VoyageIds = DeserializeList(reader["voyage_ids_json"]);
            objective.MissionIds = DeserializeList(reader["mission_ids_json"]);
            objective.CheckRunIds = DeserializeList(reader["check_run_ids_json"]);
            objective.ReleaseIds = DeserializeList(reader["release_ids_json"]);
            objective.DeploymentIds = DeserializeList(reader["deployment_ids_json"]);
            objective.IncidentIds = DeserializeList(reader["incident_ids_json"]);
            return objective;
        }

        private static TEnum ParseEnum<TEnum>(object value, TEnum fallback) where TEnum : struct
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Enum.TryParse<TEnum>(value.ToString(), true, out TEnum parsed) ? parsed : fallback;
        }

        private static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _JsonOptions);
        }

        private static List<string> DeserializeList(object value)
        {
            string? json = SqliteDatabaseDriver.NullableString(value);
            if (String.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json, _JsonOptions) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static List<SelectedPlaybook> DeserializePlaybooks(object value)
        {
            string? json = SqliteDatabaseDriver.NullableString(value);
            if (String.IsNullOrWhiteSpace(json)) return new List<SelectedPlaybook>();
            try
            {
                return JsonSerializer.Deserialize<List<SelectedPlaybook>>(json, _JsonOptions) ?? new List<SelectedPlaybook>();
            }
            catch
            {
                return new List<SelectedPlaybook>();
            }
        }
    }
}
