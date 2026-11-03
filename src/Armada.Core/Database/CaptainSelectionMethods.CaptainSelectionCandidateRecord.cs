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
        private static async Task InsertCaptainSelectionCandidateRecordAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionCandidateRecord row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO captain_selection_candidates(id,tenant_id,user_id,created_utc,last_update_utc,active,decision_id,captain_id,provider,configured_model,routing_bucket,rank_number,band,primary_reason_code,eligible,occupied_slots,capacity,last_assigned_utc,membership_id,host_id,runtime,account_key,available_percent) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@decision_id,@captain_id,@provider,@configured_model,@routing_bucket,@rank_number,@band,@primary_reason_code,@eligible,@occupied_slots,@capacity,@last_assigned_utc,@membership_id,@host_id,@runtime,@account_key,@available_percent);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@decision_id", row.DecisionId);
            Add(command, "@captain_id", row.CaptainId);
            Add(command, "@provider", row.Provider);
            Add(command, "@configured_model", row.ConfiguredModel);
            Add(command, "@routing_bucket", row.RoutingBucket);
            Add(command, "@rank_number", row.Rank);
            Add(command, "@band", row.Band.ToString());
            Add(command, "@primary_reason_code", row.PrimaryReasonCode);
            Add(command, "@eligible", row.Eligible ? 1 : 0);
            Add(command, "@occupied_slots", row.OccupiedSlots);
            Add(command, "@capacity", row.Capacity);
            Add(command, "@last_assigned_utc", row.LastAssignedUtc.HasValue ? Utc(row.LastAssignedUtc.Value) : null);
            Add(command, "@membership_id", row.MembershipId);
            Add(command, "@host_id", row.HostId);
            Add(command, "@runtime", row.Runtime);
            Add(command, "@account_key", row.AccountKey);
            Add(command, "@available_percent", row.AvailablePercent);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static CaptainSelectionCandidateRecord MapCaptainSelectionCandidateRecord(DbDataReader reader)
        {
            return new CaptainSelectionCandidateRecord
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                DecisionId = Convert.ToString(reader["decision_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CaptainId = Convert.ToString(reader["captain_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                Provider = reader["provider"] == DBNull.Value ? null : Convert.ToString(reader["provider"], System.Globalization.CultureInfo.InvariantCulture)!,
                ConfiguredModel = reader["configured_model"] == DBNull.Value ? null : Convert.ToString(reader["configured_model"], System.Globalization.CultureInfo.InvariantCulture)!,
                RoutingBucket = Convert.ToInt32(reader["routing_bucket"], System.Globalization.CultureInfo.InvariantCulture),
                Rank = Convert.ToInt32(reader["rank_number"], System.Globalization.CultureInfo.InvariantCulture),
                Band = Enum.Parse<UsageSelectionBandEnum>(Convert.ToString(reader["band"], System.Globalization.CultureInfo.InvariantCulture)!, false),
                PrimaryReasonCode = Convert.ToString(reader["primary_reason_code"], System.Globalization.CultureInfo.InvariantCulture)!,
                Eligible = Convert.ToInt32(reader["eligible"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                OccupiedSlots = Convert.ToInt32(reader["occupied_slots"], System.Globalization.CultureInfo.InvariantCulture),
                Capacity = Convert.ToInt32(reader["capacity"], System.Globalization.CultureInfo.InvariantCulture),
                LastAssignedUtc = reader["last_assigned_utc"] == DBNull.Value ? null : ParseUtc(reader["last_assigned_utc"]),
                MembershipId = reader["membership_id"] == DBNull.Value ? null : Convert.ToString(reader["membership_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                HostId = reader["host_id"] == DBNull.Value ? null : Convert.ToString(reader["host_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                Runtime = reader["runtime"] == DBNull.Value ? null : Convert.ToString(reader["runtime"], System.Globalization.CultureInfo.InvariantCulture)!,
                AccountKey = reader["account_key"] == DBNull.Value ? null : Convert.ToString(reader["account_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                AvailablePercent = reader["available_percent"] == DBNull.Value ? null : Convert.ToDouble(reader["available_percent"], System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }
}
