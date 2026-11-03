namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal sealed partial class CaptainSelectionMethods
    {
        /// <inheritdoc />
        public Task<List<CaptainSelectionDecisionView>> ReadDecisionViewsAsync(string tenantId, IReadOnlyList<string> decisionIds,
            string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); ArgumentNullException.ThrowIfNull(decisionIds);
            if (decisionIds.Count > 1000) throw new ArgumentException("Decision projections are read in batches of at most 1000.", nameof(decisionIds));
            if (decisionIds.Count == 0) return Task.FromResult(new List<CaptainSelectionDecisionView>());
            return RunAsync(async connection =>
            {
                string[] ids = Enumerable.Range(0, decisionIds.Count).Select(i => "@decision" + i).ToArray();
                string visible = candidateOwnerUserId == null ? "" : " AND (c.user_id=@candidateOwner OR c.user_id IS NULL)";
                string proposedVisible = candidateOwnerUserId == null ? "" : " AND (h.user_id=@candidateOwner OR h.user_id IS NULL)";
                string last = connection.GetType().Name == "SqlConnection" ? " OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY" : " LIMIT 1";
                using DbCommand command = Command(connection, null, @"SELECT d.*,p.strategy,c.captain_name AS selected_name,c.captain_id AS visible_selected_id,
                    h.captain_id AS visible_proposed_id," + CurrentCaptainExistsSql(candidateOwnerUserId) + @" AS selected_available,
                    e.actual_model AS reported_model,e.reporting_source AS model_source
                    FROM captain_selection_decisions d JOIN captain_selection_policy_revisions p ON p.tenant_id=d.tenant_id AND p.id=d.policy_revision_id
                    LEFT JOIN captain_selection_candidates c ON c.tenant_id=d.tenant_id AND c.decision_id=d.id AND c.captain_id=d.selected_captain_id" + visible + @"
                    LEFT JOIN captain_selection_candidates h ON h.tenant_id=d.tenant_id AND h.decision_id=d.id AND h.captain_id=d.hypothetical_captain_id" + proposedVisible + @"
                    LEFT JOIN captain_selection_execution_events e ON e.tenant_id=d.tenant_id AND e.id=(SELECT e2.id FROM captain_selection_execution_events e2
                        WHERE e2.tenant_id=d.tenant_id AND e2.decision_id=d.id AND e2.event_kind='ActualModelReported' AND e2.actual_model IS NOT NULL ORDER BY e2.occurred_utc DESC,e2.id DESC" + last + @")
                    WHERE d.tenant_id=@tenant AND d.id IN (" + String.Join(",", ids) + ")" + (ownerUserId == null ? "" : " AND d.user_id=@owner") + ";");
                Add(command, "@tenant", tenantId); if (ownerUserId != null) Add(command, "@owner", ownerUserId); if (candidateOwnerUserId != null) Add(command, "@candidateOwner", candidateOwnerUserId);
                for (int index = 0; index < ids.Length; index++) Add(command, ids[index], decisionIds[index]);
                List<CaptainSelectionDecisionView> rows = new List<CaptainSelectionDecisionView>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                string? Text(string column) => reader[column] == DBNull.Value ? null : reader[column].ToString();
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    bool selected = Text("visible_selected_id") != null;
                    rows.Add(new CaptainSelectionDecisionView
                    {
                        Id = Text("id")!, TenantId = Text("tenant_id")!, MissionId = Text("mission_id")!, Trigger = Text("trigger_kind")!, Outcome = Text("outcome")!,
                        ExecutionAttemptId = Text("outcome") == "Admitted" ? Text("execution_attempt_id") : null,
                        Mode = Enum.Parse<UsageSelectionModeEnum>(Text("mode")!), Strategy = Enum.Parse<CaptainSelectionStrategyEnum>(Text("strategy")!),
                        SelectedCaptainId = Text("visible_selected_id"), SelectedCaptainName = Text("selected_name"), SelectedCaptainAvailable = Convert.ToInt32(reader["selected_available"]) != 0,
                        IntendedModel = selected ? Text("selected_model") : null, ReportedModel = selected ? Text("reported_model") : null, ModelReportingSource = selected ? Text("model_source") : null,
                        HypotheticalCaptainId = Text("visible_proposed_id"), ReasonCode = Text("primary_reason_code")!, AlgorithmVersion = Text("algorithm_version")!, PolicyRevisionId = Text("policy_revision_id")!,
                        EvaluatedUtc = ParseUtc(reader["evaluated_utc"]), NextReconsiderUtc = reader["next_reconsider_utc"] == DBNull.Value ? null : ParseUtc(reader["next_reconsider_utc"])
                    });
                }
                return rows;
            }, token);
        }
    }
}
