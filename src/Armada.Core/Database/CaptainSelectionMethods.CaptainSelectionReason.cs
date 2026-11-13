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
        private static async Task InsertCaptainSelectionReasonAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionReason row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO captain_selection_candidate_reasons(id,tenant_id,user_id,created_utc,last_update_utc,active,decision_id,candidate_id,ordinal_number,reason_code,required_percent,available_percent,blocker_id) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@decision_id,@candidate_id,@ordinal_number,@reason_code,@required_percent,@available_percent,@blocker_id);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@decision_id", row.DecisionId);
            Add(command, "@candidate_id", row.CandidateId);
            Add(command, "@ordinal_number", row.Ordinal);
            Add(command, "@reason_code", row.ReasonCode);
            Add(command, "@required_percent", row.RequiredPercent);
            Add(command, "@available_percent", row.AvailablePercent);
            Add(command, "@blocker_id", row.BlockerId);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static CaptainSelectionReason MapCaptainSelectionReason(DbDataReader reader)
        {
            return new CaptainSelectionReason
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                DecisionId = Convert.ToString(reader["decision_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CandidateId = Convert.ToString(reader["candidate_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                Ordinal = Convert.ToInt32(reader["ordinal_number"], System.Globalization.CultureInfo.InvariantCulture),
                ReasonCode = Convert.ToString(reader["reason_code"], System.Globalization.CultureInfo.InvariantCulture)!,
                RequiredPercent = reader["required_percent"] == DBNull.Value ? null : Convert.ToDouble(reader["required_percent"], System.Globalization.CultureInfo.InvariantCulture),
                AvailablePercent = reader["available_percent"] == DBNull.Value ? null : Convert.ToDouble(reader["available_percent"], System.Globalization.CultureInfo.InvariantCulture),
                BlockerId = reader["blocker_id"] == DBNull.Value ? null : Convert.ToString(reader["blocker_id"], System.Globalization.CultureInfo.InvariantCulture)!,
            };
        }
    }
}
