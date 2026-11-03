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
        private static async Task InsertCaptainSelectionWindowRecordAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionWindowRecord row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO captain_selection_windows(id,tenant_id,user_id,created_utc,last_update_utc,active,decision_id,candidate_id,snapshot_id,captured_utc,probe_started_utc,source,status,configuration_revision,window_key,window_name,model,scope_name,generation_key,used_percent,used_amount,limit_amount,unit,resets_utc,applicable,active_reservation_percent,unsettled_percent,reserve_percent,normalization_code) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@decision_id,@candidate_id,@snapshot_id,@captured_utc,@probe_started_utc,@source,@status,@configuration_revision,@window_key,@window_name,@model,@scope_name,@generation_key,@used_percent,@used_amount,@limit_amount,@unit,@resets_utc,@applicable,@active_reservation_percent,@unsettled_percent,@reserve_percent,@normalization_code);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@decision_id", row.DecisionId);
            Add(command, "@candidate_id", row.CandidateId);
            Add(command, "@snapshot_id", row.SnapshotId);
            Add(command, "@captured_utc", row.CapturedUtc.HasValue ? Utc(row.CapturedUtc.Value) : null);
            Add(command, "@probe_started_utc", row.ProbeStartedUtc.HasValue ? Utc(row.ProbeStartedUtc.Value) : null);
            Add(command, "@source", row.Source);
            Add(command, "@status", row.Status);
            Add(command, "@configuration_revision", row.ConfigurationRevision);
            Add(command, "@window_key", row.WindowKey);
            Add(command, "@window_name", row.WindowName);
            Add(command, "@model", row.Model);
            Add(command, "@scope_name", row.Scope);
            Add(command, "@generation_key", row.Generation);
            Add(command, "@used_percent", row.UsedPercent);
            Add(command, "@used_amount", row.Used);
            Add(command, "@limit_amount", row.Limit);
            Add(command, "@unit", row.Unit);
            Add(command, "@resets_utc", row.ResetsUtc.HasValue ? Utc(row.ResetsUtc.Value) : null);
            Add(command, "@applicable", row.Applicable ? 1 : 0);
            Add(command, "@active_reservation_percent", row.ActiveReservationPercent);
            Add(command, "@unsettled_percent", row.UnsettledPercent);
            Add(command, "@reserve_percent", row.ReservePercent);
            Add(command, "@normalization_code", row.NormalizationCode);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static CaptainSelectionWindowRecord MapCaptainSelectionWindowRecord(DbDataReader reader)
        {
            return new CaptainSelectionWindowRecord
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                DecisionId = Convert.ToString(reader["decision_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CandidateId = Convert.ToString(reader["candidate_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                SnapshotId = reader["snapshot_id"] == DBNull.Value ? null : Convert.ToString(reader["snapshot_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CapturedUtc = reader["captured_utc"] == DBNull.Value ? null : ParseUtc(reader["captured_utc"]),
                ProbeStartedUtc = reader["probe_started_utc"] == DBNull.Value ? null : ParseUtc(reader["probe_started_utc"]),
                Source = reader["source"] == DBNull.Value ? null : Convert.ToString(reader["source"], System.Globalization.CultureInfo.InvariantCulture)!,
                Status = reader["status"] == DBNull.Value ? null : Convert.ToString(reader["status"], System.Globalization.CultureInfo.InvariantCulture)!,
                ConfigurationRevision = reader["configuration_revision"] == DBNull.Value ? null : Convert.ToString(reader["configuration_revision"], System.Globalization.CultureInfo.InvariantCulture)!,
                WindowKey = Convert.ToString(reader["window_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                WindowName = reader["window_name"] == DBNull.Value ? null : Convert.ToString(reader["window_name"], System.Globalization.CultureInfo.InvariantCulture)!,
                Model = reader["model"] == DBNull.Value ? null : Convert.ToString(reader["model"], System.Globalization.CultureInfo.InvariantCulture)!,
                Scope = reader["scope_name"] == DBNull.Value ? null : Convert.ToString(reader["scope_name"], System.Globalization.CultureInfo.InvariantCulture)!,
                Generation = reader["generation_key"] == DBNull.Value ? null : Convert.ToString(reader["generation_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                UsedPercent = reader["used_percent"] == DBNull.Value ? null : Convert.ToDouble(reader["used_percent"], System.Globalization.CultureInfo.InvariantCulture),
                Used = reader["used_amount"] == DBNull.Value ? null : Convert.ToInt64(reader["used_amount"], System.Globalization.CultureInfo.InvariantCulture),
                Limit = reader["limit_amount"] == DBNull.Value ? null : Convert.ToInt64(reader["limit_amount"], System.Globalization.CultureInfo.InvariantCulture),
                Unit = reader["unit"] == DBNull.Value ? null : Convert.ToString(reader["unit"], System.Globalization.CultureInfo.InvariantCulture)!,
                ResetsUtc = reader["resets_utc"] == DBNull.Value ? null : ParseUtc(reader["resets_utc"]),
                Applicable = Convert.ToInt32(reader["applicable"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                ActiveReservationPercent = Convert.ToDouble(reader["active_reservation_percent"], System.Globalization.CultureInfo.InvariantCulture),
                UnsettledPercent = Convert.ToDouble(reader["unsettled_percent"], System.Globalization.CultureInfo.InvariantCulture),
                ReservePercent = Convert.ToDouble(reader["reserve_percent"], System.Globalization.CultureInfo.InvariantCulture),
                NormalizationCode = reader["normalization_code"] == DBNull.Value ? null : Convert.ToString(reader["normalization_code"], System.Globalization.CultureInfo.InvariantCulture)!,
            };
        }
    }
}
