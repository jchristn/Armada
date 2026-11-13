namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// SQL Server implementation of normalized objective persistence.
    /// </summary>
    public class ObjectiveMethods : IObjectiveMethods
    {
        private readonly SqlServerDatabaseDriver _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectiveMethods"/> class.
        /// </summary>
        public ObjectiveMethods(SqlServerDatabaseDriver driver, Settings.DatabaseSettings settings, SyslogLogging.LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<Objective> CreateAsync(Objective objective, CancellationToken token = default)
        {
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
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
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
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
                "SELECT TOP 1 * FROM objectives WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Objective?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT TOP 1 * FROM objectives WHERE tenant_id = @tenant_id AND id = @id;",
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
                "SELECT TOP 1 * FROM objectives WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
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
            await ClearParentObjectiveReferencesAsync(id, null, token).ConfigureAwait(false);
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
            await ClearParentObjectiveReferencesAsync(id, tenantId, token).ConfigureAwait(false);
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
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 1 FROM objectives;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    return result != null && result != DBNull.Value;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
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
            ObjectiveSqlFilter filter = ObjectiveSqlFilter.Build(tenantId, userId, query, true);
            EnumerationQuery page = new EnumerationQuery();
            page.PageNumber = query.PageNumber;
            page.PageSize = query.PageSize;
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await SqlServerBoundedPage.ReadAsync(conn, filter.FromWhere, "last_update_utc DESC, id DESC",
                    cmd => BindFilter(cmd, filter), ObjectiveFromReader, page, token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<int?> ReadMaxRankAsync(string? tenantId, string? userId, CancellationToken token = default)
        {
            ObjectiveSqlFilter filter = ObjectiveSqlFilter.Build(tenantId, userId, new ObjectiveQuery(), true);
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                object? result = await SqlServerCommandHelper.ScalarAsync(conn, null, "SELECT MAX(rank) " + filter.FromWhere + ";", cmd => BindFilter(cmd, filter), token).ConfigureAwait(false);
                if (result == null || result == DBNull.Value) return null;
                return Convert.ToInt32(result);
            }
        }

        private static void BindFilter(SqlCommand cmd, ObjectiveSqlFilter filter)
        {
            foreach (SqlFilterParameter parameter in filter.Parameters)
            {
                if (parameter.IsDate) cmd.Parameters.AddWithValue(parameter.Name, SqlServerDatabaseDriver.ToIso8601(((DateTime)parameter.Value).ToUniversalTime()));
                else SqlServerCommandHelper.Add(cmd, parameter.Name, parameter.Value);
            }
        }

        private async Task<Objective?> ReadInternalAsync(string sql, Action<SqlCommand> parameterize, CancellationToken token)
        {
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return ObjectiveFromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<Objective>> EnumerateInternalAsync(string sql, Action<SqlCommand>? parameterize, CancellationToken token)
        {
            List<Objective> results = new List<Objective>();
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize?.Invoke(cmd);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(ObjectiveFromReader(reader));
                    }
                }
            }

            return results;
        }

        private async Task ClearParentObjectiveReferencesAsync(string id, string? tenantId, CancellationToken token)
        {
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    if (String.IsNullOrWhiteSpace(tenantId))
                    {
                        cmd.CommandText = "UPDATE objectives SET parent_objective_id = NULL WHERE parent_objective_id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                    }
                    else
                    {
                        cmd.CommandText = "UPDATE objectives SET parent_objective_id = NULL WHERE tenant_id = @tenant_id AND parent_objective_id = @id;";
                        cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                        cmd.Parameters.AddWithValue("@id", id);
                    }

                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private async Task ExecuteDeleteAsync(string sql, Action<SqlCommand> parameterize, CancellationToken token)
        {
            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static void BindObjective(SqlCommand cmd, Objective objective)
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
            cmd.Parameters.AddWithValue("@due_utc", objective.DueUtc.HasValue ? (object)SqlServerDatabaseDriver.ToIso8601(objective.DueUtc.Value) : DBNull.Value);
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
            cmd.Parameters.AddWithValue("@source_updated_utc", objective.SourceUpdatedUtc.HasValue ? (object)SqlServerDatabaseDriver.ToIso8601(objective.SourceUpdatedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", SqlServerDatabaseDriver.ToIso8601(objective.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(objective.LastUpdateUtc));
            cmd.Parameters.AddWithValue("@completed_utc", objective.CompletedUtc.HasValue ? (object)SqlServerDatabaseDriver.ToIso8601(objective.CompletedUtc.Value) : DBNull.Value);
        }

        private static Objective ObjectiveFromReader(SqlDataReader reader)
        {
            Objective objective = new Objective
            {
                Id = reader["id"].ToString()!,
                TenantId = SqlServerDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqlServerDatabaseDriver.NullableString(reader["user_id"]),
                Title = reader["title"].ToString()!,
                Description = SqlServerDatabaseDriver.NullableString(reader["description"]),
                Status = ObjectivePersistenceHelper.ParseEnum(reader["status"], ObjectiveStatusEnum.Draft),
                Kind = ObjectivePersistenceHelper.ParseEnum(reader["kind"], ObjectiveKindEnum.Feature),
                Category = SqlServerDatabaseDriver.NullableString(reader["category"]),
                Priority = ObjectivePersistenceHelper.ParseEnum(reader["priority"], ObjectivePriorityEnum.P2),
                Rank = SqlServerDatabaseDriver.NullableInt(reader["rank"]) ?? 0,
                BacklogState = ObjectivePersistenceHelper.ParseEnum(reader["backlog_state"], ObjectiveBacklogStateEnum.Inbox),
                Effort = ObjectivePersistenceHelper.ParseEnum(reader["effort"], ObjectiveEffortEnum.M),
                Owner = SqlServerDatabaseDriver.NullableString(reader["owner"]),
                TargetVersion = SqlServerDatabaseDriver.NullableString(reader["target_version"]),
                DueUtc = SqlServerDatabaseDriver.FromIso8601Nullable(reader["due_utc"]),
                ParentObjectiveId = SqlServerDatabaseDriver.NullableString(reader["parent_objective_id"]),
                RefinementSummary = SqlServerDatabaseDriver.NullableString(reader["refinement_summary"]),
                SuggestedPipelineId = SqlServerDatabaseDriver.NullableString(reader["suggested_pipeline_id"]),
                SourceProvider = SqlServerDatabaseDriver.NullableString(reader["source_provider"]),
                SourceType = SqlServerDatabaseDriver.NullableString(reader["source_type"]),
                SourceId = SqlServerDatabaseDriver.NullableString(reader["source_id"]),
                SourceUrl = SqlServerDatabaseDriver.NullableString(reader["source_url"]),
                SourceUpdatedUtc = SqlServerDatabaseDriver.FromIso8601Nullable(reader["source_updated_utc"]),
                CreatedUtc = SqlServerDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqlServerDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!),
                CompletedUtc = SqlServerDatabaseDriver.FromIso8601Nullable(reader["completed_utc"])
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
    }
}
