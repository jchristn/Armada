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
        public Task<List<CaptainSelectionReason>> ReadReasonsAsync(string tenantId, string decisionId, string candidateId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId)); Required(candidateId, nameof(candidateId));
            return RunAsync(async connection =>
            {
                using DbCommand command = Command(connection, null, "SELECT r.* FROM captain_selection_candidate_reasons r JOIN captain_selection_candidates c ON c.id=r.candidate_id AND c.tenant_id=r.tenant_id JOIN captain_selection_decisions d ON d.id=c.decision_id WHERE "
                    + CandidateScope(ownerUserId, candidateOwnerUserId) + " AND r.decision_id=c.decision_id AND c.id=@candidate ORDER BY r.ordinal_number,r.id;");
                CandidateParameters(command, tenantId, decisionId, ownerUserId, candidateOwnerUserId); Add(command, "@candidate", candidateId);
                List<CaptainSelectionReason> rows = new List<CaptainSelectionReason>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapCaptainSelectionReason(reader));
                return rows;
            }, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<CaptainSelectionExecutionEvent>> EnumerateExecutionEventsAsync(string tenantId, string decisionId, string? ownerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); Required(decisionId, nameof(decisionId)); Paging(pageNumber, pageSize);
            return RunAsync(async connection =>
            {
                string where = " FROM captain_selection_execution_events e JOIN captain_selection_decisions d ON d.id=e.decision_id AND d.tenant_id=e.tenant_id WHERE e.tenant_id=@tenant AND e.decision_id=@id" + (ownerUserId == null ? "" : " AND d.user_id=@owner");
                using DbCommand command = Command(connection, null, "SELECT COUNT(*)" + where);
                Add(command, "@tenant", tenantId); Add(command, "@id", decisionId); if (ownerUserId != null) Add(command, "@owner", ownerUserId);
                long count = Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                command.CommandText = "SELECT e.*" + where + " ORDER BY e.occurred_utc,e.id" + PageSql(connection);
                Add(command, "@skip", (pageNumber - 1) * pageSize); Add(command, "@take", pageSize);
                List<CaptainSelectionExecutionEvent> rows = new List<CaptainSelectionExecutionEvent>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(MapCaptainSelectionExecutionEvent(reader));
                return EnumerationResult<CaptainSelectionExecutionEvent>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, rows, count);
            }, token);
        }
    }
}
