namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class MissionMethods
    {
        private const string MissionSummarySelectColumns = @"
id, tenant_id, user_id, voyage_id, vessel_id, captain_id, title, status, priority,
parent_mission_id, branch_name, dock_id, process_id, pr_url, commit_hash,
persona, depends_on_mission_id, failure_reason, failure_kind, requires_review, review_deny_action,
review_comment, reviewed_by_user_id, review_requested_utc, reviewed_utc,
created_utc, started_utc, completed_utc, total_runtime_ms, last_update_utc,
LENGTH(COALESCE(description, '')) AS description_length,
LENGTH(COALESCE(diff_snapshot, '')) AS diff_snapshot_length,
LENGTH(COALESCE(agent_output, '')) AS agent_output_length";

        /// <inheritdoc />
        public async Task<MissionSummary?> ReadSummaryAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + MissionSummarySelectColumns + " FROM missions WHERE id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return MissionSummaryFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<MissionSummary>> EnumerateSummariesAsync(EnumerationQuery query, CancellationToken token = default)
        {
            return await EnumerateSummariesCoreAsync(
                query,
                Array.Empty<string>(),
                Array.Empty<KeyValuePair<string, object?>>(),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionSummary>> EnumerateSummariesByVoyageAsync(string voyageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
            return await EnumerateSummariesByColumnAsync("voyage_id", voyageId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionSummary>> EnumerateSummariesByVesselAsync(string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return await EnumerateSummariesByColumnAsync("vessel_id", vesselId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionSummary>> EnumerateSummariesByCaptainAsync(string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            return await EnumerateSummariesByColumnAsync("captain_id", captainId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Dictionary<MissionStatusEnum, int>> CountByStatusAsync(CancellationToken token = default)
        {
            return await CountByStatusCoreAsync(
                Array.Empty<string>(),
                Array.Empty<KeyValuePair<string, object?>>(),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionHistoryPoint>> EnumerateHistoryPointsAsync(MissionHistoryQuery query, CancellationToken token = default)
        {
            return await EnumerateHistoryPointsCoreAsync(
                query,
                Array.Empty<string>(),
                Array.Empty<KeyValuePair<string, object?>>(),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MissionSummary?> ReadSummaryAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + MissionSummarySelectColumns + " FROM missions WHERE tenant_id = @tenant_id AND id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return MissionSummaryFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<MissionSummary>> EnumerateSummariesAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateSummariesCoreAsync(
                query,
                new[] { "tenant_id = @tenant_id" },
                new[] { new KeyValuePair<string, object?>("@tenant_id", tenantId) },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionSummary>> EnumerateSummariesByVoyageAsync(string tenantId, string voyageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
            return await EnumerateTenantSummariesByColumnAsync(tenantId, "voyage_id", voyageId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionSummary>> EnumerateSummariesByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return await EnumerateTenantSummariesByColumnAsync(tenantId, "vessel_id", vesselId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionSummary>> EnumerateSummariesByCaptainAsync(string tenantId, string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            return await EnumerateTenantSummariesByColumnAsync(tenantId, "captain_id", captainId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Dictionary<MissionStatusEnum, int>> CountByStatusAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await CountByStatusCoreAsync(
                new[] { "tenant_id = @tenant_id" },
                new[] { new KeyValuePair<string, object?>("@tenant_id", tenantId) },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionHistoryPoint>> EnumerateHistoryPointsAsync(string tenantId, MissionHistoryQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateHistoryPointsCoreAsync(
                query,
                new[] { "tenant_id = @tenant_id" },
                new[] { new KeyValuePair<string, object?>("@tenant_id", tenantId) },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MissionSummary?> ReadSummaryAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + MissionSummarySelectColumns + " FROM missions WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return MissionSummaryFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<MissionSummary>> EnumerateSummariesAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateSummariesCoreAsync(
                query,
                new[] { "tenant_id = @tenant_id", "user_id = @user_id" },
                new[]
                {
                    new KeyValuePair<string, object?>("@tenant_id", tenantId),
                    new KeyValuePair<string, object?>("@user_id", userId)
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Dictionary<MissionStatusEnum, int>> CountByStatusAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return await CountByStatusCoreAsync(
                new[] { "tenant_id = @tenant_id", "user_id = @user_id" },
                new[]
                {
                    new KeyValuePair<string, object?>("@tenant_id", tenantId),
                    new KeyValuePair<string, object?>("@user_id", userId)
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<MissionHistoryPoint>> EnumerateHistoryPointsAsync(string tenantId, string userId, MissionHistoryQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateHistoryPointsCoreAsync(
                query,
                new[] { "tenant_id = @tenant_id", "user_id = @user_id" },
                new[]
                {
                    new KeyValuePair<string, object?>("@tenant_id", tenantId),
                    new KeyValuePair<string, object?>("@user_id", userId)
                },
                token).ConfigureAwait(false);
        }

        private async Task<EnumerationResult<MissionSummary>> EnumerateSummariesCoreAsync(
            EnumerationQuery? query,
            IEnumerable<string> baseConditions,
            IEnumerable<KeyValuePair<string, object?>> baseParameters,
            CancellationToken token)
        {
            query ??= new EnumerationQuery();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>(baseConditions);
                using (SqliteCommand countCmd = conn.CreateCommand())
                {
                    AddParameters(countCmd, baseParameters);
                    ApplyEnumerationFilters(query, conditions, countCmd);
                    string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
                    countCmd.CommandText = "SELECT COUNT(*) FROM missions" + whereClause + ";";
                    long totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));

                    using (SqliteCommand selectCmd = conn.CreateCommand())
                    {
                        AddParameters(selectCmd, baseParameters);
                        List<string> selectConditions = new List<string>(baseConditions);
                        ApplyEnumerationFilters(query, selectConditions, selectCmd);
                        string selectWhereClause = selectConditions.Count > 0 ? " WHERE " + String.Join(" AND ", selectConditions) : String.Empty;
                        string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
                        selectCmd.CommandText =
                            "SELECT " + MissionSummarySelectColumns +
                            " FROM missions" + selectWhereClause +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        List<MissionSummary> results = new List<MissionSummary>();
                        using (SqliteDataReader reader = await selectCmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                                results.Add(MissionSummaryFromReader(reader));
                        }

                        return EnumerationResult<MissionSummary>.Create(query, results, totalCount);
                    }
                }
            }
        }

        private async Task<List<MissionSummary>> EnumerateSummariesByColumnAsync(string column, string value, CancellationToken token)
        {
            List<MissionSummary> results = new List<MissionSummary>();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT " + MissionSummarySelectColumns +
                        " FROM missions WHERE " + column + " = @value ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@value", value);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionSummaryFromReader(reader));
                    }
                }
            }

            return results;
        }

        private async Task<List<MissionSummary>> EnumerateTenantSummariesByColumnAsync(string tenantId, string column, string value, CancellationToken token)
        {
            List<MissionSummary> results = new List<MissionSummary>();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT " + MissionSummarySelectColumns +
                        " FROM missions WHERE tenant_id = @tenant_id AND " + column + " = @value ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@value", value);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionSummaryFromReader(reader));
                    }
                }
            }

            return results;
        }

        private async Task<Dictionary<MissionStatusEnum, int>> CountByStatusCoreAsync(
            IEnumerable<string> baseConditions,
            IEnumerable<KeyValuePair<string, object?>> baseParameters,
            CancellationToken token)
        {
            Dictionary<MissionStatusEnum, int> results = new Dictionary<MissionStatusEnum, int>();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    AddParameters(cmd, baseParameters);
                    string whereClause = BuildWhereClause(baseConditions);
                    cmd.CommandText = "SELECT status, COUNT(*) AS count FROM missions" + whereClause + " GROUP BY status;";
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            string statusValue = reader["status"].ToString() ?? String.Empty;
                            if (Enum.TryParse(statusValue, true, out MissionStatusEnum parsed))
                            {
                                results[parsed] = Convert.ToInt32(reader["count"]);
                            }
                        }
                    }
                }
            }

            return results;
        }

        private async Task<List<MissionHistoryPoint>> EnumerateHistoryPointsCoreAsync(
            MissionHistoryQuery? query,
            IEnumerable<string> baseConditions,
            IEnumerable<KeyValuePair<string, object?>> baseParameters,
            CancellationToken token)
        {
            query ??= new MissionHistoryQuery();
            List<MissionHistoryPoint> results = new List<MissionHistoryPoint>();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string>(baseConditions)
                    {
                        "created_utc >= @from_utc",
                        "created_utc < @to_utc"
                    };
                    AddParameters(cmd, baseParameters);
                    cmd.Parameters.AddWithValue("@from_utc", SqliteDatabaseDriver.ToIso8601(query.FromUtc));
                    cmd.Parameters.AddWithValue("@to_utc", SqliteDatabaseDriver.ToIso8601(query.ToUtc));
                    if (!String.IsNullOrEmpty(query.VesselId))
                    {
                        conditions.Add("vessel_id = @vessel_id");
                        cmd.Parameters.AddWithValue("@vessel_id", query.VesselId);
                    }

                    cmd.CommandText =
                        "SELECT created_utc, status, vessel_id FROM missions" +
                        BuildWhereClause(conditions) +
                        " ORDER BY created_utc ASC;";
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            results.Add(new MissionHistoryPoint
                            {
                                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                                Status = Enum.Parse<MissionStatusEnum>(reader["status"].ToString()!),
                                VesselId = SqliteDatabaseDriver.NullableString(reader["vessel_id"])
                            });
                        }
                    }
                }
            }

            return results;
        }

        private static void ApplyEnumerationFilters(EnumerationQuery query, List<string> conditions, SqliteCommand cmd)
        {
            if (query.CreatedAfter.HasValue)
            {
                conditions.Add("created_utc > @created_after");
                cmd.Parameters.AddWithValue("@created_after", SqliteDatabaseDriver.ToIso8601(query.CreatedAfter.Value));
            }
            if (query.CreatedBefore.HasValue)
            {
                conditions.Add("created_utc < @created_before");
                cmd.Parameters.AddWithValue("@created_before", SqliteDatabaseDriver.ToIso8601(query.CreatedBefore.Value));
            }
            if (!String.IsNullOrEmpty(query.Status))
            {
                conditions.Add("status = @status");
                cmd.Parameters.AddWithValue("@status", query.Status);
            }
            if (!String.IsNullOrEmpty(query.VoyageId))
            {
                conditions.Add("voyage_id = @voyage_id");
                cmd.Parameters.AddWithValue("@voyage_id", query.VoyageId);
            }
            if (!String.IsNullOrEmpty(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                cmd.Parameters.AddWithValue("@vessel_id", query.VesselId);
            }
            if (!String.IsNullOrEmpty(query.CaptainId))
            {
                conditions.Add("captain_id = @captain_id");
                cmd.Parameters.AddWithValue("@captain_id", query.CaptainId);
            }
        }

        private static void AddParameters(SqliteCommand cmd, IEnumerable<KeyValuePair<string, object?>> parameters)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                cmd.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
            }
        }

        private static string BuildWhereClause(IEnumerable<string> conditions)
        {
            List<string> conditionList = new List<string>(conditions);
            return conditionList.Count > 0 ? " WHERE " + String.Join(" AND ", conditionList) : String.Empty;
        }

        private static MissionSummary MissionSummaryFromReader(SqliteDataReader reader)
        {
            MissionSummary summary = new MissionSummary
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                VoyageId = SqliteDatabaseDriver.NullableString(reader["voyage_id"]),
                VesselId = SqliteDatabaseDriver.NullableString(reader["vessel_id"]),
                CaptainId = SqliteDatabaseDriver.NullableString(reader["captain_id"]),
                Title = reader["title"].ToString() ?? String.Empty,
                Status = Enum.Parse<MissionStatusEnum>(reader["status"].ToString()!),
                Priority = Convert.ToInt32(reader["priority"]),
                ParentMissionId = SqliteDatabaseDriver.NullableString(reader["parent_mission_id"]),
                BranchName = SqliteDatabaseDriver.NullableString(reader["branch_name"]),
                DockId = SqliteDatabaseDriver.NullableString(reader["dock_id"]),
                ProcessId = reader["process_id"] == DBNull.Value ? null : Convert.ToInt32(reader["process_id"]),
                PrUrl = SqliteDatabaseDriver.NullableString(reader["pr_url"]),
                CommitHash = SqliteDatabaseDriver.NullableString(reader["commit_hash"]),
                Persona = SqliteDatabaseDriver.NullableString(reader["persona"]),
                DependsOnMissionId = SqliteDatabaseDriver.NullableString(reader["depends_on_mission_id"]),
                FailureReason = SqliteDatabaseDriver.NullableString(reader["failure_reason"]),
                FailureKind = MissionFailureKindColumn.FromDbValue(reader["failure_kind"]),
                RequiresReview = reader["requires_review"] != DBNull.Value && Convert.ToInt64(reader["requires_review"]) == 1,
                ReviewComment = SqliteDatabaseDriver.NullableString(reader["review_comment"]),
                ReviewedByUserId = SqliteDatabaseDriver.NullableString(reader["reviewed_by_user_id"]),
                ReviewRequestedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["review_requested_utc"]),
                ReviewedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["reviewed_utc"]),
                DescriptionLength = Convert.ToInt32(reader["description_length"]),
                DiffSnapshotLength = Convert.ToInt32(reader["diff_snapshot_length"]),
                AgentOutputLength = Convert.ToInt32(reader["agent_output_length"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                StartedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["started_utc"]),
                CompletedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["completed_utc"]),
                TotalRuntimeMs = reader["total_runtime_ms"] == DBNull.Value ? null : Convert.ToInt64(reader["total_runtime_ms"]),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };

            string? reviewDenyAction = SqliteDatabaseDriver.NullableString(reader["review_deny_action"]);
            if (!String.IsNullOrEmpty(reviewDenyAction) &&
                Enum.TryParse(reviewDenyAction, true, out ReviewDenyActionEnum parsed))
            {
                summary.ReviewDenyAction = parsed;
            }

            return summary;
        }
    }
}
