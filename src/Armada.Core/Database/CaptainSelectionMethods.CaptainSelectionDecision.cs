namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal sealed partial class CaptainSelectionMethods
    {
        private static async Task InsertCaptainSelectionDecisionAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionDecision row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO captain_selection_decisions(id,tenant_id,user_id,created_utc,last_update_utc,active,mission_id,voyage_id,execution_attempt_id,trigger_kind,mode,outcome,selected_captain_id,selected_provider,selected_model,hypothetical_captain_id,hypothetical_outcome,algorithm_version,policy_revision_id,routing_revision,evidence_fingerprint,previous_decision_id,evaluated_utc,next_reconsider_utc,primary_reason_code,override_actor_id,override_reason,candidate_count) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@mission_id,@voyage_id,@execution_attempt_id,@trigger_kind,@mode,@outcome,@selected_captain_id,@selected_provider,@selected_model,@hypothetical_captain_id,@hypothetical_outcome,@algorithm_version,@policy_revision_id,@routing_revision,@evidence_fingerprint,@previous_decision_id,@evaluated_utc,@next_reconsider_utc,@primary_reason_code,@override_actor_id,@override_reason,@candidate_count);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@mission_id", row.MissionId);
            Add(command, "@voyage_id", row.VoyageId);
            Add(command, "@execution_attempt_id", row.ExecutionAttemptId);
            Add(command, "@trigger_kind", row.Trigger);
            Add(command, "@mode", row.Mode.ToString());
            Add(command, "@outcome", row.Outcome);
            Add(command, "@selected_captain_id", row.SelectedCaptainId);
            Add(command, "@selected_provider", row.SelectedProvider);
            Add(command, "@selected_model", row.SelectedModel);
            Add(command, "@hypothetical_captain_id", row.HypotheticalCaptainId);
            Add(command, "@hypothetical_outcome", row.HypotheticalOutcome);
            Add(command, "@algorithm_version", row.AlgorithmVersion);
            Add(command, "@policy_revision_id", row.PolicyRevisionId);
            Add(command, "@routing_revision", row.RoutingRevision);
            Add(command, "@evidence_fingerprint", row.EvidenceFingerprint);
            Add(command, "@previous_decision_id", row.PreviousDecisionId);
            Add(command, "@evaluated_utc", Utc(row.EvaluatedUtc));
            Add(command, "@next_reconsider_utc", row.NextReconsiderUtc.HasValue ? Utc(row.NextReconsiderUtc.Value) : null);
            Add(command, "@primary_reason_code", row.PrimaryReasonCode);
            Add(command, "@override_actor_id", row.OverrideActorId);
            Add(command, "@override_reason", row.OverrideReason);
            Add(command, "@candidate_count", row.CandidateCount);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static CaptainSelectionDecision MapCaptainSelectionDecision(DbDataReader reader)
        {
            return new CaptainSelectionDecision
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                MissionId = Convert.ToString(reader["mission_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                VoyageId = reader["voyage_id"] == DBNull.Value ? null : Convert.ToString(reader["voyage_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                ExecutionAttemptId = Convert.ToString(reader["execution_attempt_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                Trigger = Convert.ToString(reader["trigger_kind"], System.Globalization.CultureInfo.InvariantCulture)!,
                Mode = Enum.Parse<UsageSelectionModeEnum>(Convert.ToString(reader["mode"], System.Globalization.CultureInfo.InvariantCulture)!, false),
                Outcome = Convert.ToString(reader["outcome"], System.Globalization.CultureInfo.InvariantCulture)!,
                SelectedCaptainId = reader["selected_captain_id"] == DBNull.Value ? null : Convert.ToString(reader["selected_captain_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                SelectedProvider = reader["selected_provider"] == DBNull.Value ? null : Convert.ToString(reader["selected_provider"], System.Globalization.CultureInfo.InvariantCulture)!,
                SelectedModel = reader["selected_model"] == DBNull.Value ? null : Convert.ToString(reader["selected_model"], System.Globalization.CultureInfo.InvariantCulture)!,
                HypotheticalCaptainId = reader["hypothetical_captain_id"] == DBNull.Value ? null : Convert.ToString(reader["hypothetical_captain_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                HypotheticalOutcome = reader["hypothetical_outcome"] == DBNull.Value ? null : Convert.ToString(reader["hypothetical_outcome"], System.Globalization.CultureInfo.InvariantCulture)!,
                AlgorithmVersion = Convert.ToString(reader["algorithm_version"], System.Globalization.CultureInfo.InvariantCulture)!,
                PolicyRevisionId = Convert.ToString(reader["policy_revision_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                RoutingRevision = reader["routing_revision"] == DBNull.Value ? null : Convert.ToString(reader["routing_revision"], System.Globalization.CultureInfo.InvariantCulture)!,
                EvidenceFingerprint = Convert.ToString(reader["evidence_fingerprint"], System.Globalization.CultureInfo.InvariantCulture)!,
                PreviousDecisionId = reader["previous_decision_id"] == DBNull.Value ? null : Convert.ToString(reader["previous_decision_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                EvaluatedUtc = ParseUtc(reader["evaluated_utc"]),
                NextReconsiderUtc = reader["next_reconsider_utc"] == DBNull.Value ? null : ParseUtc(reader["next_reconsider_utc"]),
                PrimaryReasonCode = Convert.ToString(reader["primary_reason_code"], System.Globalization.CultureInfo.InvariantCulture)!,
                OverrideActorId = reader["override_actor_id"] == DBNull.Value ? null : Convert.ToString(reader["override_actor_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                OverrideReason = reader["override_reason"] == DBNull.Value ? null : Convert.ToString(reader["override_reason"], System.Globalization.CultureInfo.InvariantCulture)!,
                CandidateCount = Convert.ToInt32(reader["candidate_count"], System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }
}
