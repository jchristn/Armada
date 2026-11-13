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
        public Task<EnumerationResult<CaptainSelectionWaitingItem>> EnumerateActionableWaitsAsync(string? tenantId, string? userId, IReadOnlyList<string> reasonCodes,
            DateTime waitingSinceBeforeUtc, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(reasonCodes);
            Paging(pageNumber, pageSize);
            if (userId != null && tenantId == null) throw new ArgumentException("A user scope requires its tenant.", nameof(userId));
            EnumerationQuery paging = new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize };
            if (reasonCodes.Count == 0) return Task.FromResult(EnumerationResult<CaptainSelectionWaitingItem>.Create(paging, new List<CaptainSelectionWaitingItem>(), 0));
            return RunAsync(async connection =>
            {
                string[] reasons = Enumerable.Range(0, reasonCodes.Count).Select(i => "@reason" + i).ToArray();
                // Only Enforce blocks on usage; Observe waits come from legacy routing. The latest decision per mission (by evaluated time, then id) must itself be the waiting one; a later
                // admission or a different waiting state replaces the item. Waiting decisions are deduplicated by
                // material state, so evaluated_utc is when the current state began.
                string where = " FROM captain_selection_decisions d JOIN missions m ON m.id=d.mission_id AND m.tenant_id=d.tenant_id"
                    + " WHERE m.status='Pending' AND d.outcome='" + CaptainSelectionOutcomes.Waiting + "' AND d.mode='" + UsageSelectionModeEnum.Enforce + "'"
                    + " AND d.primary_reason_code IN (" + String.Join(",", reasons) + ") AND d.evaluated_utc<@cutoff"
                    + (tenantId == null ? "" : " AND d.tenant_id=@tenant") + (userId == null ? "" : " AND m.user_id=@user")
                    + " AND NOT EXISTS (SELECT 1 FROM captain_selection_decisions n WHERE n.tenant_id=d.tenant_id AND n.mission_id=d.mission_id"
                    + " AND (n.evaluated_utc>d.evaluated_utc OR (n.evaluated_utc=d.evaluated_utc AND n.id>d.id)))";
                void Bind(DbCommand command)
                {
                    Add(command, "@cutoff", Utc(waitingSinceBeforeUtc));
                    if (tenantId != null) Add(command, "@tenant", tenantId);
                    if (userId != null) Add(command, "@user", userId);
                    for (int index = 0; index < reasons.Length; index++) Add(command, reasons[index], reasonCodes[index]);
                }

                long total;
                using (DbCommand count = Command(connection, null, "SELECT COUNT(*)" + where + ";"))
                {
                    Bind(count);
                    total = Convert.ToInt64(await count.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                }

                List<CaptainSelectionWaitingItem> items = new List<CaptainSelectionWaitingItem>();
                using (DbCommand select = Command(connection, null, "SELECT d.id,d.tenant_id,d.mission_id,d.primary_reason_code,d.evaluated_utc,m.title,m.user_id"
                    + where + " ORDER BY d.evaluated_utc,d.id" + PageSql(connection) + ";"))
                {
                    Bind(select);
                    Add(select, "@skip", (pageNumber - 1) * pageSize);
                    Add(select, "@take", pageSize);
                    using DbDataReader reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        items.Add(new CaptainSelectionWaitingItem
                        {
                            DecisionId = reader["id"].ToString()!,
                            TenantId = reader["tenant_id"].ToString()!,
                            MissionId = reader["mission_id"].ToString()!,
                            ReasonCode = reader["primary_reason_code"].ToString()!,
                            WaitingSinceUtc = ParseUtc(reader["evaluated_utc"]),
                            MissionTitle = reader["title"] == DBNull.Value ? "" : reader["title"].ToString()!,
                            UserId = reader["user_id"] == DBNull.Value ? null : reader["user_id"].ToString()
                        });
                    }
                }

                return EnumerationResult<CaptainSelectionWaitingItem>.Create(paging, items, total);
            }, token);
        }
    }
}
