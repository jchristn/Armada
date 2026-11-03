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
        private static async Task InsertUsageBudgetReservationAsync(DbConnection connection, DbTransaction transaction, UsageBudgetReservation row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO usage_budget_reservations(id,tenant_id,user_id,created_utc,last_update_utc,active,decision_id,mission_id,execution_attempt_id,captain_id,membership_id,host_id,runtime,account_key,window_key,generation_key,reserved_percent,state,revision,execution_started_utc,execution_ended_utc,settlement_eligible_utc,settled_by_snapshot_id,released_utc,transition_reason) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@decision_id,@mission_id,@execution_attempt_id,@captain_id,@membership_id,@host_id,@runtime,@account_key,@window_key,@generation_key,@reserved_percent,@state,@revision,@execution_started_utc,@execution_ended_utc,@settlement_eligible_utc,@settled_by_snapshot_id,@released_utc,@transition_reason);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@decision_id", row.DecisionId);
            Add(command, "@mission_id", row.MissionId);
            Add(command, "@execution_attempt_id", row.ExecutionAttemptId);
            Add(command, "@captain_id", row.CaptainId);
            Add(command, "@membership_id", row.MembershipId);
            Add(command, "@host_id", row.HostId);
            Add(command, "@runtime", row.Runtime);
            Add(command, "@account_key", row.AccountKey);
            Add(command, "@window_key", row.WindowKey);
            Add(command, "@generation_key", row.Generation);
            Add(command, "@reserved_percent", row.ReservedPercent);
            Add(command, "@state", row.State.ToString());
            Add(command, "@revision", row.Revision);
            Add(command, "@execution_started_utc", row.ExecutionStartedUtc.HasValue ? Utc(row.ExecutionStartedUtc.Value) : null);
            Add(command, "@execution_ended_utc", row.ExecutionEndedUtc.HasValue ? Utc(row.ExecutionEndedUtc.Value) : null);
            Add(command, "@settlement_eligible_utc", row.SettlementEligibleUtc.HasValue ? Utc(row.SettlementEligibleUtc.Value) : null);
            Add(command, "@settled_by_snapshot_id", row.SettledBySnapshotId);
            Add(command, "@released_utc", row.ReleasedUtc.HasValue ? Utc(row.ReleasedUtc.Value) : null);
            Add(command, "@transition_reason", row.TransitionReason);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static UsageBudgetReservation MapUsageBudgetReservation(DbDataReader reader)
        {
            return new UsageBudgetReservation
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                DecisionId = Convert.ToString(reader["decision_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                MissionId = Convert.ToString(reader["mission_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                ExecutionAttemptId = Convert.ToString(reader["execution_attempt_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CaptainId = Convert.ToString(reader["captain_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                MembershipId = Convert.ToString(reader["membership_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                HostId = Convert.ToString(reader["host_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                Runtime = Convert.ToString(reader["runtime"], System.Globalization.CultureInfo.InvariantCulture)!,
                AccountKey = Convert.ToString(reader["account_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                WindowKey = Convert.ToString(reader["window_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                Generation = Convert.ToString(reader["generation_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                ReservedPercent = Convert.ToDouble(reader["reserved_percent"], System.Globalization.CultureInfo.InvariantCulture),
                State = Enum.Parse<UsageReservationStateEnum>(Convert.ToString(reader["state"], System.Globalization.CultureInfo.InvariantCulture)!, false),
                Revision = Convert.ToInt32(reader["revision"], System.Globalization.CultureInfo.InvariantCulture),
                ExecutionStartedUtc = reader["execution_started_utc"] == DBNull.Value ? null : ParseUtc(reader["execution_started_utc"]),
                ExecutionEndedUtc = reader["execution_ended_utc"] == DBNull.Value ? null : ParseUtc(reader["execution_ended_utc"]),
                SettlementEligibleUtc = reader["settlement_eligible_utc"] == DBNull.Value ? null : ParseUtc(reader["settlement_eligible_utc"]),
                SettledBySnapshotId = reader["settled_by_snapshot_id"] == DBNull.Value ? null : Convert.ToString(reader["settled_by_snapshot_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                ReleasedUtc = reader["released_utc"] == DBNull.Value ? null : ParseUtc(reader["released_utc"]),
                TransitionReason = reader["transition_reason"] == DBNull.Value ? null : Convert.ToString(reader["transition_reason"], System.Globalization.CultureInfo.InvariantCulture)!,
            };
        }
    }
}
