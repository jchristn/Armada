namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the WHERE clause of an objective page (scope plus every <see cref="ObjectiveQuery"/> filter) so all four
    /// database providers filter in SQL before counting. Text filters are case-insensitive (LOWER on both sides).
    /// Link id and tag filters match one element of the stored JSON array exactly by its JSON-encoded form; the free
    /// text search matches a substring of the text columns and of the JSON list columns. LIKE patterns escape their
    /// wildcards with '!'.
    /// </summary>
    internal class ObjectiveSqlFilter
    {
        #region Internal-Members

        /// <summary>
        /// FROM and WHERE clauses, starting with "FROM objectives".
        /// </summary>
        internal string FromWhere { get; private set; } = "FROM objectives";

        /// <summary>
        /// Parameters referenced by <see cref="FromWhere"/>.
        /// </summary>
        internal List<SqlFilterParameter> Parameters { get; } = new List<SqlFilterParameter>();

        #endregion

        #region Private-Members

        private static readonly string[] _SearchTextColumns = new string[]
        {
            "title", "description", "owner", "category", "target_version", "refinement_summary",
            "source_provider", "source_type", "source_id", "source_url"
        };

        private static readonly string[] _SearchJsonColumns = new string[]
        {
            "tags_json", "acceptance_criteria_json", "non_goals_json", "rollout_constraints_json",
            "evidence_links_json", "blocked_by_objective_ids_json"
        };

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Build the filter for a scope and query.
        /// </summary>
        /// <param name="tenantId">Only this tenant, or null for every tenant.</param>
        /// <param name="userId">Only this user, or null for every user.</param>
        /// <param name="query">Filters.</param>
        /// <param name="escapeBrackets">True for SQL Server, whose LIKE treats '[' as a wildcard.</param>
        /// <returns>The filter.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        internal static ObjectiveSqlFilter Build(string? tenantId, string? userId, ObjectiveQuery query, bool escapeBrackets)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            ObjectiveSqlFilter filter = new ObjectiveSqlFilter();
            List<string> conditions = new List<string>();

            if (!String.IsNullOrEmpty(tenantId)) conditions.Add("tenant_id = " + filter.Add("@tenant_id", tenantId!));
            if (!String.IsNullOrEmpty(userId)) conditions.Add("user_id = " + filter.Add("@user_id", userId!));

            if (!String.IsNullOrWhiteSpace(query.Owner))
                conditions.Add("LOWER(owner) LIKE " + filter.Add("@owner", Contains(query.Owner!.Trim(), escapeBrackets)) + " ESCAPE '!'");
            if (!String.IsNullOrWhiteSpace(query.Category))
                conditions.Add("LOWER(category) LIKE " + filter.Add("@category", Contains(query.Category!.Trim(), escapeBrackets)) + " ESCAPE '!'");
            if (!String.IsNullOrWhiteSpace(query.ParentObjectiveId))
                conditions.Add("LOWER(parent_objective_id) = " + filter.Add("@parent_objective_id", query.ParentObjectiveId!.Trim().ToLowerInvariant()));
            if (!String.IsNullOrWhiteSpace(query.TargetVersion))
                conditions.Add("LOWER(target_version) = " + filter.Add("@target_version", query.TargetVersion!.Trim().ToLowerInvariant()));

            filter.AddElement(conditions, "vessel_ids_json", "@vessel_id", query.VesselId, escapeBrackets);
            filter.AddElement(conditions, "fleet_ids_json", "@fleet_id", query.FleetId, escapeBrackets);
            filter.AddElement(conditions, "planning_session_ids_json", "@planning_session_id", query.PlanningSessionId, escapeBrackets);
            filter.AddElement(conditions, "voyage_ids_json", "@voyage_id", query.VoyageId, escapeBrackets);
            filter.AddElement(conditions, "mission_ids_json", "@mission_id", query.MissionId, escapeBrackets);
            filter.AddElement(conditions, "check_run_ids_json", "@check_run_id", query.CheckRunId, escapeBrackets);
            filter.AddElement(conditions, "release_ids_json", "@release_id", query.ReleaseId, escapeBrackets);
            filter.AddElement(conditions, "deployment_ids_json", "@deployment_id", query.DeploymentId, escapeBrackets);
            filter.AddElement(conditions, "incident_ids_json", "@incident_id", query.IncidentId, escapeBrackets);
            filter.AddElement(conditions, "tags_json", "@tag", query.Tag, escapeBrackets);

            if (query.Status.HasValue) conditions.Add("status = " + filter.Add("@status", query.Status.Value.ToString()));
            if (query.BacklogState.HasValue) conditions.Add("backlog_state = " + filter.Add("@backlog_state", query.BacklogState.Value.ToString()));
            if (query.Kind.HasValue) conditions.Add("kind = " + filter.Add("@kind", query.Kind.Value.ToString()));
            if (query.Priority.HasValue) conditions.Add("priority = " + filter.Add("@priority", query.Priority.Value.ToString()));
            if (query.Effort.HasValue) conditions.Add("effort = " + filter.Add("@effort", query.Effort.Value.ToString()));
            if (query.FromUtc.HasValue) conditions.Add("created_utc >= " + filter.AddDate("@from_utc", query.FromUtc.Value.ToUniversalTime()));
            if (query.ToUtc.HasValue) conditions.Add("created_utc <= " + filter.AddDate("@to_utc", query.ToUtc.Value.ToUniversalTime()));

            string? search = String.IsNullOrWhiteSpace(query.Search) ? null : query.Search!.Trim();
            if (search != null)
            {
                List<string> any = new List<string>();
                string textName = filter.Add("@search", Contains(search, escapeBrackets));
                foreach (string column in _SearchTextColumns) any.Add("LOWER(" + column + ") LIKE " + textName + " ESCAPE '!'");

                // A term made only of JSON array punctuation would match the array syntax itself, not an element.
                if (search.Any(c => c != '[' && c != ']' && c != ','))
                {
                    string jsonName = filter.Add("@search_json", Contains(EncodeInner(search), escapeBrackets));
                    foreach (string column in _SearchJsonColumns) any.Add("LOWER(" + column + ") LIKE " + jsonName + " ESCAPE '!'");
                }

                conditions.Add("(" + String.Join(" OR ", any) + ")");
            }

            if (conditions.Count > 0) filter.FromWhere = "FROM objectives WHERE " + String.Join(" AND ", conditions);
            return filter;
        }

        #endregion

        #region Private-Methods

        private string Add(string name, string value)
        {
            Parameters.Add(new SqlFilterParameter { Name = name, Value = value });
            return name;
        }

        private string AddDate(string name, DateTime value)
        {
            Parameters.Add(new SqlFilterParameter { Name = name, Value = value, IsDate = true });
            return name;
        }

        private void AddElement(List<string> conditions, string column, string name, string? value, bool escapeBrackets)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            string element = ObjectivePersistenceHelper.Serialize(value.Trim()).ToLowerInvariant();
            conditions.Add("LOWER(" + column + ") LIKE " + Add(name, "%" + Escape(element, escapeBrackets) + "%") + " ESCAPE '!'");
        }

        private static string Contains(string value, bool escapeBrackets)
        {
            return "%" + Escape(value.ToLowerInvariant(), escapeBrackets) + "%";
        }

        private static string EncodeInner(string value)
        {
            string encoded = ObjectivePersistenceHelper.Serialize(value);
            return encoded.Length >= 2 ? encoded.Substring(1, encoded.Length - 2) : encoded;
        }

        private static string Escape(string value, bool escapeBrackets)
        {
            StringBuilder sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                if (c == '!' || c == '%' || c == '_' || (escapeBrackets && c == '[')) sb.Append('!');
                sb.Append(c);
            }

            return sb.ToString();
        }

        #endregion
    }
}
