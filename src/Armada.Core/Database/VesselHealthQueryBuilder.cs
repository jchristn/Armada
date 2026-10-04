namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the provider-neutral filter and sort SQL for vessel health enumeration. Sort columns come only from
    /// the <see cref="VesselHealthSortEnum"/> whitelist; every user-supplied value is bound as a parameter. Status
    /// columns sort by rank (Fail 3, Warn 2, Pass 1, Unknown and NotApplicable 0) and null measurements always sort
    /// last, so results are identical across providers.
    /// </summary>
    internal static class VesselHealthQueryBuilder
    {
        #region Internal-Members

        /// <summary>
        /// Columns selected for every vessel health row: the health row plus vessel and fleet projections.
        /// </summary>
        internal static readonly string SelectColumns =
            "h.*, v.id AS v_id, v.tenant_id AS v_tenant_id, v.name AS v_name, v.fleet_id AS v_fleet_id, f.name AS f_name";

        /// <summary>
        /// FROM clause: every vessel, left-joined to its health row and fleet.
        /// </summary>
        internal static readonly string FromClause =
            " FROM vessels v LEFT JOIN vessel_health h ON h.vessel_id = v.id LEFT JOIN fleets f ON f.id = v.fleet_id";

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Build the WHERE and ORDER BY clauses for a tenant-scoped vessel health enumeration.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Filter, sort, and paging request.</param>
        /// <returns>The query fragments and parameters.</returns>
        internal static VesselHealthQuery Build(string tenantId, VesselHealthEnumerateRequest request)
        {
            VesselHealthQuery query = new VesselHealthQuery();
            query.FromClause = FromClause;

            List<string> conditions = new List<string>();
            conditions.Add("v.tenant_id = @tenant_id");
            query.Parameters.Add(new QueryParameter("@tenant_id", tenantId));

            if (!request.IncludeInactive)
            {
                conditions.Add("v.active = @v_active");
                query.Parameters.Add(new QueryParameter("@v_active", true));
            }

            if (!String.IsNullOrWhiteSpace(request.NameContains))
            {
                conditions.Add("LOWER(v.name) LIKE @name_contains ESCAPE '!'");
                query.Parameters.Add(new QueryParameter("@name_contains", ContainsPattern(request.NameContains!)));
            }

            if (!String.IsNullOrWhiteSpace(request.FleetId))
            {
                conditions.Add("v.fleet_id = @fleet_id");
                query.Parameters.Add(new QueryParameter("@fleet_id", request.FleetId!.Trim()));
            }

            if (!String.IsNullOrWhiteSpace(request.PrimaryLanguage))
            {
                conditions.Add("LOWER(h.primary_language) = @primary_language");
                query.Parameters.Add(new QueryParameter("@primary_language", request.PrimaryLanguage!.Trim().ToLowerInvariant()));
            }

            if (!String.IsNullOrWhiteSpace(request.CurrentBranchContains))
            {
                conditions.Add("LOWER(h.current_branch) LIKE @branch_contains ESCAPE '!'");
                query.Parameters.Add(new QueryParameter("@branch_contains", ContainsPattern(request.CurrentBranchContains!)));
            }

            AddStatusFilter(conditions, query.Parameters, "h.overall_status", "os", request.OverallStatus);
            AddStatusFilter(conditions, query.Parameters, "h.dependency_status", "ds", request.DependencyStatus);
            AddStatusFilter(conditions, query.Parameters, "h.test_infra_status", "ts", request.TestInfraStatus);

            if (request.IsDirty.HasValue)
            {
                conditions.Add("h.is_dirty = @is_dirty");
                query.Parameters.Add(new QueryParameter("@is_dirty", request.IsDirty.Value));
            }

            if (request.HasCiConfig.HasValue)
            {
                conditions.Add("h.has_ci_config = @has_ci_config");
                query.Parameters.Add(new QueryParameter("@has_ci_config", request.HasCiConfig.Value));
            }

            if (request.Divergence.HasValue)
            {
                switch (request.Divergence.Value)
                {
                    case VesselDivergenceFilterEnum.Ahead:
                        conditions.Add("(h.ahead_of_default > 0 AND COALESCE(h.behind_default, 0) = 0)");
                        break;
                    case VesselDivergenceFilterEnum.Behind:
                        conditions.Add("(h.behind_default > 0 AND COALESCE(h.ahead_of_default, 0) = 0)");
                        break;
                    case VesselDivergenceFilterEnum.Diverged:
                        conditions.Add("(h.ahead_of_default > 0 AND h.behind_default > 0)");
                        break;
                    case VesselDivergenceFilterEnum.Even:
                        conditions.Add("(h.ahead_of_default = 0 AND h.behind_default = 0)");
                        break;
                }
            }

            if (request.MinBranchCount.HasValue)
            {
                conditions.Add("h.branch_count >= @min_branch_count");
                query.Parameters.Add(new QueryParameter("@min_branch_count", request.MinBranchCount.Value));
            }

            if (request.MaxBranchCount.HasValue)
            {
                conditions.Add("h.branch_count <= @max_branch_count");
                query.Parameters.Add(new QueryParameter("@max_branch_count", request.MaxBranchCount.Value));
            }

            if (request.LastCommitAfterUtc.HasValue)
            {
                conditions.Add("h.last_commit_utc > @last_commit_after");
                query.Parameters.Add(new QueryParameter("@last_commit_after", request.LastCommitAfterUtc.Value));
            }

            if (request.LastCommitBeforeUtc.HasValue)
            {
                conditions.Add("h.last_commit_utc < @last_commit_before");
                query.Parameters.Add(new QueryParameter("@last_commit_before", request.LastCommitBeforeUtc.Value));
            }

            query.WhereClause = " WHERE " + String.Join(" AND ", conditions);
            query.OrderByClause = BuildOrderBy(request.SortBy, request.SortDescending);
            return query;
        }

        /// <summary>
        /// Escape a user-supplied substring for a LIKE pattern using '!' as the escape character, lower-case it,
        /// and wrap it in wildcards.
        /// </summary>
        /// <param name="value">Raw substring.</param>
        /// <returns>The pattern.</returns>
        internal static string ContainsPattern(string value)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('%');
            foreach (char c in value.Trim().ToLowerInvariant())
            {
                if (c == '!' || c == '%' || c == '_' || c == '[') sb.Append('!');
                sb.Append(c);
            }

            sb.Append('%');
            return sb.ToString();
        }

        /// <summary>
        /// SQL expression ranking a status column: Fail 3, Warn 2, Pass 1, anything else (Unknown, NotApplicable,
        /// or null) 0.
        /// </summary>
        /// <param name="column">Qualified column name.</param>
        /// <returns>The CASE expression.</returns>
        internal static string StatusRank(string column)
        {
            return "(CASE " + column + " WHEN 'Fail' THEN 3 WHEN 'Warn' THEN 2 WHEN 'Pass' THEN 1 ELSE 0 END)";
        }

        #endregion

        #region Private-Methods

        private static void AddStatusFilter(List<string> conditions, List<QueryParameter> parameters, string column, string prefix, List<VesselHealthStatusEnum> statuses)
        {
            if (statuses == null || statuses.Count < 1) return;

            List<string> names = new List<string>();
            List<VesselHealthStatusEnum> distinct = new List<VesselHealthStatusEnum>();
            foreach (VesselHealthStatusEnum status in statuses)
            {
                if (!distinct.Contains(status)) distinct.Add(status);
            }

            for (int i = 0; i < distinct.Count; i++)
            {
                string name = "@" + prefix + i;
                names.Add(name);
                parameters.Add(new QueryParameter(name, distinct[i].ToString()));
            }

            // A vessel that has never been evaluated has no health row; treat its statuses as Unknown.
            conditions.Add("COALESCE(" + column + ", 'Unknown') IN (" + String.Join(", ", names) + ")");
        }

        private static string BuildOrderBy(VesselHealthSortEnum sortBy, bool descending)
        {
            string direction = descending ? " DESC" : " ASC";
            string? expression = null;
            string? nullCheck = null;

            switch (sortBy)
            {
                case VesselHealthSortEnum.VesselName:
                    expression = "v.name";
                    break;
                case VesselHealthSortEnum.FleetName:
                    expression = "f.name";
                    nullCheck = "f.name";
                    break;
                case VesselHealthSortEnum.OverallStatus:
                    expression = StatusRank("h.overall_status");
                    break;
                case VesselHealthSortEnum.Divergence:
                    expression = "(COALESCE(h.ahead_of_default, 0) + COALESCE(h.behind_default, 0))";
                    nullCheck = "h.ahead_of_default IS NULL AND h.behind_default";
                    break;
                case VesselHealthSortEnum.AheadOfDefault:
                    expression = "h.ahead_of_default";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.BehindDefault:
                    expression = "h.behind_default";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.IsDirty:
                    expression = "h.is_dirty";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.BranchCount:
                    expression = "h.branch_count";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.StaleBranchCount:
                    expression = "h.stale_branch_count";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.OutdatedCount:
                    expression = "h.outdated_count";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.OutdatedMajorCount:
                    expression = "h.outdated_major_count";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.VulnerableCount:
                    expression = "h.vulnerable_count";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.DependencyStatus:
                    expression = StatusRank("h.dependency_status");
                    break;
                case VesselHealthSortEnum.TestInfraStatus:
                    expression = StatusRank("h.test_infra_status");
                    break;
                case VesselHealthSortEnum.CiStatus:
                    expression = StatusRank("h.ci_status");
                    break;
                case VesselHealthSortEnum.LastCommitUtc:
                    expression = "h.last_commit_utc";
                    nullCheck = expression;
                    break;
                case VesselHealthSortEnum.EvaluatedUtc:
                    expression = "h.evaluated_utc";
                    nullCheck = expression;
                    break;
                default:
                    expression = "v.name";
                    break;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(" ORDER BY ");
            if (nullCheck != null)
                sb.Append("(CASE WHEN " + nullCheck + " IS NULL THEN 1 ELSE 0 END) ASC, ");
            sb.Append(expression + direction);
            if (sortBy != VesselHealthSortEnum.VesselName)
                sb.Append(", v.name ASC");
            sb.Append(", v.id ASC");
            return sb.ToString();
        }

        #endregion
    }
}
