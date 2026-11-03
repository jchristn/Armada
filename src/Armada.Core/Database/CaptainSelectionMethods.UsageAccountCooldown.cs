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
        private static async Task InsertUsageAccountCooldownAsync(DbConnection connection, DbTransaction transaction, UsageAccountCooldown row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO usage_account_cooldowns(id,tenant_id,user_id,created_utc,last_update_utc,active,host_id,runtime,account_key,window_key,revision,observed_utc,retry_after_utc,classification) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@host_id,@runtime,@account_key,@window_key,@revision,@observed_utc,@retry_after_utc,@classification);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@host_id", row.HostId);
            Add(command, "@runtime", row.Runtime);
            Add(command, "@account_key", row.AccountKey);
            Add(command, "@window_key", row.WindowKey);
            Add(command, "@revision", row.Revision);
            Add(command, "@observed_utc", Utc(row.ObservedUtc));
            Add(command, "@retry_after_utc", Utc(row.RetryAfterUtc));
            Add(command, "@classification", row.Classification);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static UsageAccountCooldown MapUsageAccountCooldown(DbDataReader reader)
        {
            return new UsageAccountCooldown
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                HostId = Convert.ToString(reader["host_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                Runtime = Convert.ToString(reader["runtime"], System.Globalization.CultureInfo.InvariantCulture)!,
                AccountKey = Convert.ToString(reader["account_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                WindowKey = Convert.ToString(reader["window_key"], System.Globalization.CultureInfo.InvariantCulture)!,
                Revision = Convert.ToInt32(reader["revision"], System.Globalization.CultureInfo.InvariantCulture),
                ObservedUtc = ParseUtc(reader["observed_utc"]),
                RetryAfterUtc = ParseUtc(reader["retry_after_utc"]),
                Classification = Convert.ToString(reader["classification"], System.Globalization.CultureInfo.InvariantCulture)!,
            };
        }
    }
}
