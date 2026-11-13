namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of normalized objective persistence.
    /// </summary>
    public class ObjectiveMethods : IObjectiveMethods
    {
        private readonly PostgresqlDatabaseDriver _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectiveMethods"/> class.
        /// </summary>
        public ObjectiveMethods(PostgresqlDatabaseDriver driver, Settings.DatabaseSettings settings, SyslogLogging.LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<Objective> CreateAsync(Objective objective, CancellationToken token = default)
        {
            if (objective == null) throw new ArgumentNullException(nameof(objective));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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
            return await ReadInternalAsync(
                "SELECT * FROM objectives WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Objective?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM objectives WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Objective?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM objectives WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
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
            await ExecuteDeleteAsync(
                "DELETE FROM objectives WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(
                "DELETE FROM objectives WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Objective>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync(
                "SELECT * FROM objectives ORDER BY rank ASC, priority ASC, last_update_utc DESC;",
                null,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Objective>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync(
                "SELECT * FROM objectives WHERE tenant_id = @tenant_id ORDER BY rank ASC, priority ASC, last_update_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Objective>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync(
                "SELECT * FROM objectives WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY rank ASC, priority ASC, last_update_utc DESC;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAnyAsync(CancellationToken token = default)
        {
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM objectives WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Objective>> EnumeratePageAsync(string? tenantId, string? userId, ObjectiveQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            ObjectiveSqlFilter filter = ObjectiveSqlFilter.Build(tenantId, userId, query, false);
            EnumerationQuery page = new EnumerationQuery();
            page.PageNumber = query.PageNumber;
            page.PageSize = query.PageSize;
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await PostgresqlBoundedPage.ReadAsync(conn, filter.FromWhere, "last_update_utc DESC, id DESC",
                    cmd => BindFilter(cmd, filter), ObjectiveFromReader, page, token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<int?> ReadMaxRankAsync(string? tenantId, string? userId, CancellationToken token = default)
        {
            ObjectiveSqlFilter filter = ObjectiveSqlFilter.Build(tenantId, userId, new ObjectiveQuery(), false);
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                object? result = await PostgresqlCommandHelper.ScalarAsync(conn, null, "SELECT MAX(rank) " + filter.FromWhere + ";", cmd => BindFilter(cmd, filter), token).ConfigureAwait(false);
                if (result == null || result == DBNull.Value) return null;
                return Convert.ToInt32(result);
            }
        }

        private static void BindFilter(NpgsqlCommand cmd, ObjectiveSqlFilter filter)
        {
            foreach (SqlFilterParameter parameter in filter.Parameters)
            {
                if (parameter.IsDate) cmd.Parameters.AddWithValue(parameter.Name, ((DateTime)parameter.Value).ToUniversalTime());
                else PostgresqlCommandHelper.Add(cmd, parameter.Name, parameter.Value);
            }
        }

        private async Task<Objective?> ReadInternalAsync(string sql, Action<NpgsqlCommand> parameterize, CancellationToken token)
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
                            return ObjectiveFromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<Objective>> EnumerateInternalAsync(string sql, Action<NpgsqlCommand>? parameterize, CancellationToken token)
        {
            List<Objective> results = new List<Objective>();
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
                            results.Add(ObjectiveFromReader(reader));
                    }
                }
            }

            return results;
        }

        private async Task ExecuteDeleteAsync(string sql, Action<NpgsqlCommand> parameterize, CancellationToken token)
        {
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static void BindObjective(NpgsqlCommand cmd, Objective objective)
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
            cmd.Parameters.AddWithValue("@due_utc", (object?)objective.DueUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parent_objective_id", (object?)objective.ParentObjectiveId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@blocked_by_objective_ids_json", ObjectivePersistenceHelper.Serialize(objective.BlockedByObjectiveIds));
            cmd.Parameters.AddWithValue("@refinement_summary", (object?)objective.RefinementSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@suggested_pipeline_id", (object?)objective.SuggestedPipelineId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@suggested_playbooks_json", ObjectivePersistenceHelper.Serialize(objective.SuggestedPlaybooks));
            cmd.Parameters.AddWithValue("@tags_json", ObjectivePersistenceHelper.Serialize(objective.Tags));
            cmd.Parameters.AddWithValue("@acceptance_criteria_json", ObjectivePersistenceHelper.Serialize(objective.AcceptanceCriteria));
            cmd.Parameters.AddWithValue("@non_goals_json", ObjectivePersistenceHelper.Serialize(objective.NonGoals));
            cmd.Parameters.AddWithValue("@rollout_constraints_json", ObjectivePersistenceHelper.Serialize(objective.RolloutConstraints));
            cmd.Parameters.AddWithValue("@evidence_links_json", ObjectivePersistenceHelper.Serialize(objective.EvidenceLinks));
            cmd.Parameters.AddWithValue("@fleet_ids_json", ObjectivePersistenceHelper.Serialize(objective.FleetIds));
            cmd.Parameters.AddWithValue("@vessel_ids_json", ObjectivePersistenceHelper.Serialize(objective.VesselIds));
            cmd.Parameters.AddWithValue("@planning_session_ids_json", ObjectivePersistenceHelper.Serialize(objective.PlanningSessionIds));
            cmd.Parameters.AddWithValue("@refinement_session_ids_json", ObjectivePersistenceHelper.Serialize(objective.RefinementSessionIds));
            cmd.Parameters.AddWithValue("@voyage_ids_json", ObjectivePersistenceHelper.Serialize(objective.VoyageIds));
            cmd.Parameters.AddWithValue("@mission_ids_json", ObjectivePersistenceHelper.Serialize(objective.MissionIds));
            cmd.Parameters.AddWithValue("@check_run_ids_json", ObjectivePersistenceHelper.Serialize(objective.CheckRunIds));
            cmd.Parameters.AddWithValue("@release_ids_json", ObjectivePersistenceHelper.Serialize(objective.ReleaseIds));
            cmd.Parameters.AddWithValue("@deployment_ids_json", ObjectivePersistenceHelper.Serialize(objective.DeploymentIds));
            cmd.Parameters.AddWithValue("@incident_ids_json", ObjectivePersistenceHelper.Serialize(objective.IncidentIds));
            cmd.Parameters.AddWithValue("@source_provider", (object?)objective.SourceProvider ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_type", (object?)objective.SourceType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_id", (object?)objective.SourceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_url", (object?)objective.SourceUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_updated_utc", (object?)objective.SourceUpdatedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", objective.CreatedUtc);
            cmd.Parameters.AddWithValue("@last_update_utc", objective.LastUpdateUtc);
            cmd.Parameters.AddWithValue("@completed_utc", (object?)objective.CompletedUtc ?? DBNull.Value);
        }

        private static Objective ObjectiveFromReader(NpgsqlDataReader reader)
        {
            Objective objective = new Objective
            {
                Id = reader["id"].ToString()!,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                Title = reader["title"].ToString()!,
                Description = NullableString(reader["description"]),
                Status = ObjectivePersistenceHelper.ParseEnum(reader["status"], ObjectiveStatusEnum.Draft),
                Kind = ObjectivePersistenceHelper.ParseEnum(reader["kind"], ObjectiveKindEnum.Feature),
                Category = NullableString(reader["category"]),
                Priority = ObjectivePersistenceHelper.ParseEnum(reader["priority"], ObjectivePriorityEnum.P2),
                Rank = NullableInt(reader["rank"]) ?? 0,
                BacklogState = ObjectivePersistenceHelper.ParseEnum(reader["backlog_state"], ObjectiveBacklogStateEnum.Inbox),
                Effort = ObjectivePersistenceHelper.ParseEnum(reader["effort"], ObjectiveEffortEnum.M),
                Owner = NullableString(reader["owner"]),
                TargetVersion = NullableString(reader["target_version"]),
                DueUtc = NullableDateTime(reader["due_utc"]),
                ParentObjectiveId = NullableString(reader["parent_objective_id"]),
                RefinementSummary = NullableString(reader["refinement_summary"]),
                SuggestedPipelineId = NullableString(reader["suggested_pipeline_id"]),
                SourceProvider = NullableString(reader["source_provider"]),
                SourceType = NullableString(reader["source_type"]),
                SourceId = NullableString(reader["source_id"]),
                SourceUrl = NullableString(reader["source_url"]),
                SourceUpdatedUtc = NullableDateTime(reader["source_updated_utc"]),
                CreatedUtc = (DateTime)reader["created_utc"],
                LastUpdateUtc = (DateTime)reader["last_update_utc"],
                CompletedUtc = NullableDateTime(reader["completed_utc"])
            };

            objective.BlockedByObjectiveIds = ObjectivePersistenceHelper.DeserializeList(reader["blocked_by_objective_ids_json"]);
            objective.SuggestedPlaybooks = ObjectivePersistenceHelper.DeserializePlaybooks(reader["suggested_playbooks_json"]);
            objective.Tags = ObjectivePersistenceHelper.DeserializeList(reader["tags_json"]);
            objective.AcceptanceCriteria = ObjectivePersistenceHelper.DeserializeList(reader["acceptance_criteria_json"]);
            objective.NonGoals = ObjectivePersistenceHelper.DeserializeList(reader["non_goals_json"]);
            objective.RolloutConstraints = ObjectivePersistenceHelper.DeserializeList(reader["rollout_constraints_json"]);
            objective.EvidenceLinks = ObjectivePersistenceHelper.DeserializeList(reader["evidence_links_json"]);
            objective.FleetIds = ObjectivePersistenceHelper.DeserializeList(reader["fleet_ids_json"]);
            objective.VesselIds = ObjectivePersistenceHelper.DeserializeList(reader["vessel_ids_json"]);
            objective.PlanningSessionIds = ObjectivePersistenceHelper.DeserializeList(reader["planning_session_ids_json"]);
            objective.RefinementSessionIds = ObjectivePersistenceHelper.DeserializeList(reader["refinement_session_ids_json"]);
            objective.VoyageIds = ObjectivePersistenceHelper.DeserializeList(reader["voyage_ids_json"]);
            objective.MissionIds = ObjectivePersistenceHelper.DeserializeList(reader["mission_ids_json"]);
            objective.CheckRunIds = ObjectivePersistenceHelper.DeserializeList(reader["check_run_ids_json"]);
            objective.ReleaseIds = ObjectivePersistenceHelper.DeserializeList(reader["release_ids_json"]);
            objective.DeploymentIds = ObjectivePersistenceHelper.DeserializeList(reader["deployment_ids_json"]);
            objective.IncidentIds = ObjectivePersistenceHelper.DeserializeList(reader["incident_ids_json"]);
            return objective;
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
