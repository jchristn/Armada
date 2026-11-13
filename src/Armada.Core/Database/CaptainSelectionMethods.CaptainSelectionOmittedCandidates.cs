namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    internal sealed partial class CaptainSelectionMethods
    {
        /// <inheritdoc />
        public Task<List<CaptainSelectionOmittedReason>> ReadOmittedCandidatesAsync(string tenantId, string decisionId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId));
            return RunAsync(async connection =>
            {
                // Hidden peers' counts are excluded the same way their candidate rows are.
                using DbCommand command = Command(connection, null, "SELECT o.reason_code,SUM(o.candidate_count) AS omitted FROM captain_selection_omitted_candidates o JOIN captain_selection_decisions d ON d.id=o.decision_id AND d.tenant_id=o.tenant_id"
                    + " WHERE o.tenant_id=@tenant AND o.decision_id=@decision" + (ownerUserId == null ? "" : " AND d.user_id=@owner")
                    + (candidateOwnerUserId == null ? "" : " AND (o.user_id=@candidateOwner OR o.user_id IS NULL)")
                    + " GROUP BY o.reason_code ORDER BY omitted DESC,o.reason_code;");
                CandidateParameters(command, tenantId, decisionId, ownerUserId, candidateOwnerUserId);
                List<CaptainSelectionOmittedReason> rows = new List<CaptainSelectionOmittedReason>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    rows.Add(new CaptainSelectionOmittedReason
                    {
                        ReasonCode = Convert.ToString(reader["reason_code"], CultureInfo.InvariantCulture)!,
                        Count = Convert.ToInt32(reader["omitted"], CultureInfo.InvariantCulture)
                    });
                return rows;
            }, token);
        }

        private static async Task InsertCaptainSelectionOmittedCandidatesAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionOmittedCandidates row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO captain_selection_omitted_candidates(id,tenant_id,user_id,created_utc,last_update_utc,active,decision_id,reason_code,candidate_count) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@decision_id,@reason_code,@candidate_count);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@decision_id", row.DecisionId);
            Add(command, "@reason_code", row.ReasonCode);
            Add(command, "@candidate_count", row.CandidateCount);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }
}
