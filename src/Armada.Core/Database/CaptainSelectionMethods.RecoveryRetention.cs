namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed partial class CaptainSelectionMethods
    {
        private static async Task PruneRecoveryAsync(DbConnection connection, DbTransaction transaction, DateTime cutoffUtc, int batchSize, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            using DbCommand expired = Command(connection, transaction, @"SELECT s.execution_key,s.tenant_id FROM usage_execution_sources s WHERE s.created_utc<@cutoff
                AND NOT EXISTS(SELECT 1 FROM missions m WHERE m.tenant_id=s.tenant_id AND m.id=s.mission_id AND m.usage_execution_key=s.execution_key AND m.status IN ('Assigned','InProgress','Testing','Verifying'))
                AND NOT EXISTS(SELECT 1 FROM usage_budget_reservations r WHERE r.tenant_id=s.tenant_id AND r.execution_attempt_id=s.execution_key AND r.state IN ('Active','Unsettled','Uncertain'))
                AND NOT EXISTS(SELECT 1 FROM usage_untracked_executions u WHERE u.tenant_id=s.tenant_id AND u.execution_key=s.execution_key AND u.state IN ('Active','Unsettled','Uncertain'))
                AND NOT EXISTS(SELECT 1 FROM usage_account_cooldowns c WHERE c.tenant_id=s.tenant_id AND c.execution_key=s.execution_key AND c.retry_after_utc>=@now)
                ORDER BY s.created_utc,s.execution_key" + PageSql(connection));
            Add(expired, "@cutoff", Utc(cutoffUtc)); Add(expired, "@now", Utc(now)); Add(expired, "@skip", 0); Add(expired, "@take", batchSize);
            Dictionary<string, string> executions = new Dictionary<string, string>(StringComparer.Ordinal);
            using (DbDataReader reader = await expired.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false)) executions.Add(reader["execution_key"].ToString()!, reader["tenant_id"].ToString()!);
            foreach (KeyValuePair<string, string> entry in executions)
            {
                foreach (string sql in new[] { "DELETE FROM usage_account_cooldowns WHERE tenant_id=@tenant AND execution_key=@execution;", "DELETE FROM usage_execution_sources WHERE tenant_id=@tenant AND execution_key=@execution;" })
                {
                    using DbCommand remove = Command(connection, transaction, sql); Add(remove, "@tenant", entry.Value); Add(remove, "@execution", entry.Key);
                    await remove.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            using DbCommand audit = Command(connection, transaction, @"SELECT r.id,r.tenant_id FROM usage_selection_recovery r WHERE r.created_utc<@cutoff
                AND NOT EXISTS(SELECT 1 FROM missions m WHERE m.tenant_id=r.tenant_id AND m.id=r.mission_id)
                AND NOT EXISTS(SELECT 1 FROM captain_selection_decisions d WHERE d.tenant_id=r.tenant_id AND d.mission_id=r.mission_id)
                ORDER BY r.created_utc,r.id" + PageSql(connection));
            Add(audit, "@cutoff", Utc(cutoffUtc)); Add(audit, "@skip", 0); Add(audit, "@take", batchSize);
            Dictionary<string, string> ids = new Dictionary<string, string>(StringComparer.Ordinal);
            using (DbDataReader reader = await audit.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false)) ids.Add(reader["id"].ToString()!, reader["tenant_id"].ToString()!);
            foreach (KeyValuePair<string, string> entry in ids)
            {
                using DbCommand remove = Command(connection, transaction, "DELETE FROM usage_selection_recovery WHERE tenant_id=@tenant AND id=@id;");
                Add(remove, "@tenant", entry.Value); Add(remove, "@id", entry.Key); await remove.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
    }
}
