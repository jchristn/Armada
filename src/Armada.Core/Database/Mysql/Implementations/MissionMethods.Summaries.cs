namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
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
CHAR_LENGTH(COALESCE(description, '')) AS description_length,
CHAR_LENGTH(COALESCE(diff_snapshot, '')) AS diff_snapshot_length,
CHAR_LENGTH(COALESCE(agent_output, '')) AS agent_output_length";

        /// <inheritdoc />
        public async Task<MissionSummary?> ReadSummaryAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + MissionSummarySelectColumns + " FROM missions WHERE id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + MissionSummarySelectColumns + " FROM missions WHERE tenant_id = @tenant_id AND id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT " + MissionSummarySelectColumns + " FROM missions WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> countConditions = new List<string>(baseConditions);
                using (MySqlCommand countCmd = conn.CreateCommand())
                {
                    AddParameters(countCmd, baseParameters);
                    ApplyEnumerationFilters(query, countConditions, countCmd);
                    countCmd.CommandText = "SELECT COUNT(*) FROM missions" + BuildWhereClause(countConditions) + ";";
                    long totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));

                    List<string> selectConditions = new List<string>(baseConditions);
                    using (MySqlCommand selectCmd = conn.CreateCommand())
                    {
                        AddParameters(selectCmd, baseParameters);
                        ApplyEnumerationFilters(query, selectConditions, selectCmd);
                        string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
                        selectCmd.CommandText =
                            "SELECT " + MissionSummarySelectColumns +
                            " FROM missions" + BuildWhereClause(selectConditions) +
                            " ORDER BY created_utc " + orderDirection +
                            " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                        List<MissionSummary> results = new List<MissionSummary>();
                        using (MySqlDataReader reader = await selectCmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT " + MissionSummarySelectColumns +
                        " FROM missions WHERE " + column + " = @value ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@value", value);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT " + MissionSummarySelectColumns +
                        " FROM missions WHERE tenant_id = @tenant_id AND " + column + " = @value ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@value", value);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    AddParameters(cmd, baseParameters);
                    cmd.CommandText = "SELECT status, COUNT(*) AS count FROM missions" + BuildWhereClause(baseConditions) + " GROUP BY status;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string>(baseConditions)
                    {
                        "created_utc >= @from_utc",
                        "created_utc < @to_utc"
                    };
                    AddParameters(cmd, baseParameters);
                    cmd.Parameters.AddWithValue("@from_utc", ToIso8601(query.FromUtc));
                    cmd.Parameters.AddWithValue("@to_utc", ToIso8601(query.ToUtc));
                    if (!String.IsNullOrEmpty(query.VesselId))
                    {
                        conditions.Add("vessel_id = @vessel_id");
                        cmd.Parameters.AddWithValue("@vessel_id", query.VesselId);
                    }

                    cmd.CommandText =
                        "SELECT created_utc, status, vessel_id FROM missions" +
                        BuildWhereClause(conditions) +
                        " ORDER BY created_utc ASC;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            results.Add(new MissionHistoryPoint
                            {
                                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                                Status = Enum.Parse<MissionStatusEnum>(reader["status"].ToString()!),
                                VesselId = NullableString(reader["vessel_id"])
                            });
                        }
                    }
                }
            }

            return results;
        }

        private static void ApplyEnumerationFilters(EnumerationQuery query, List<string> conditions, MySqlCommand cmd)
        {
            if (query.CreatedAfter.HasValue)
            {
                conditions.Add("created_utc > @created_after");
                cmd.Parameters.AddWithValue("@created_after", ToIso8601(query.CreatedAfter.Value));
            }
            if (query.CreatedBefore.HasValue)
            {
                conditions.Add("created_utc < @created_before");
                cmd.Parameters.AddWithValue("@created_before", ToIso8601(query.CreatedBefore.Value));
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

        private static void AddParameters(MySqlCommand cmd, IEnumerable<KeyValuePair<string, object?>> parameters)
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

        private static MissionSummary MissionSummaryFromReader(MySqlDataReader reader)
        {
            MissionSummary summary = new MissionSummary
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                VoyageId = NullableString(reader["voyage_id"]),
                VesselId = NullableString(reader["vessel_id"]),
                CaptainId = NullableString(reader["captain_id"]),
                Title = reader["title"].ToString() ?? String.Empty,
                Status = Enum.Parse<MissionStatusEnum>(reader["status"].ToString()!),
                Priority = Convert.ToInt32(reader["priority"]),
                ParentMissionId = NullableString(reader["parent_mission_id"]),
                BranchName = NullableString(reader["branch_name"]),
                DockId = NullableString(reader["dock_id"]),
                ProcessId = reader["process_id"] == DBNull.Value ? null : Convert.ToInt32(reader["process_id"]),
                PrUrl = NullableString(reader["pr_url"]),
                CommitHash = NullableString(reader["commit_hash"]),
                Persona = NullableString(reader["persona"]),
                DependsOnMissionId = NullableString(reader["depends_on_mission_id"]),
                FailureReason = NullableString(reader["failure_reason"]),
                FailureKind = MissionFailureKindColumn.FromDbValue(reader["failure_kind"]),
                RequiresReview = reader["requires_review"] != DBNull.Value && Convert.ToInt64(reader["requires_review"]) == 1,
                ReviewComment = NullableString(reader["review_comment"]),
                ReviewedByUserId = NullableString(reader["reviewed_by_user_id"]),
                ReviewRequestedUtc = FromIso8601Nullable(reader["review_requested_utc"]),
                ReviewedUtc = FromIso8601Nullable(reader["reviewed_utc"]),
                DescriptionLength = Convert.ToInt32(reader["description_length"]),
                DiffSnapshotLength = Convert.ToInt32(reader["diff_snapshot_length"]),
                AgentOutputLength = Convert.ToInt32(reader["agent_output_length"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                StartedUtc = FromIso8601Nullable(reader["started_utc"]),
                CompletedUtc = FromIso8601Nullable(reader["completed_utc"]),
                TotalRuntimeMs = reader["total_runtime_ms"] == DBNull.Value ? null : Convert.ToInt64(reader["total_runtime_ms"]),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            string? reviewDenyAction = NullableString(reader["review_deny_action"]);
            if (!String.IsNullOrEmpty(reviewDenyAction) &&
                Enum.TryParse(reviewDenyAction, true, out ReviewDenyActionEnum parsed))
            {
                summary.ReviewDenyAction = parsed;
            }

            return summary;
        }
    }
}
