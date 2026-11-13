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
        private static async Task InsertCaptainSelectionExecutionEventAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionExecutionEvent row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO captain_selection_execution_events(id,tenant_id,user_id,created_utc,last_update_utc,active,decision_id,execution_attempt_id,event_kind,actual_provider,actual_model,reporting_source,occurred_utc,failure_classification) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@decision_id,@execution_attempt_id,@event_kind,@actual_provider,@actual_model,@reporting_source,@occurred_utc,@failure_classification);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@decision_id", row.DecisionId);
            Add(command, "@execution_attempt_id", row.ExecutionAttemptId);
            Add(command, "@event_kind", row.EventKind);
            Add(command, "@actual_provider", row.ActualProvider);
            Add(command, "@actual_model", row.ActualModel);
            Add(command, "@reporting_source", row.ReportingSource);
            Add(command, "@occurred_utc", Utc(row.OccurredUtc));
            Add(command, "@failure_classification", row.FailureClassification);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static CaptainSelectionExecutionEvent MapCaptainSelectionExecutionEvent(DbDataReader reader)
        {
            return new CaptainSelectionExecutionEvent
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                DecisionId = Convert.ToString(reader["decision_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                ExecutionAttemptId = Convert.ToString(reader["execution_attempt_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                EventKind = Convert.ToString(reader["event_kind"], System.Globalization.CultureInfo.InvariantCulture)!,
                ActualProvider = reader["actual_provider"] == DBNull.Value ? null : Convert.ToString(reader["actual_provider"], System.Globalization.CultureInfo.InvariantCulture)!,
                ActualModel = reader["actual_model"] == DBNull.Value ? null : Convert.ToString(reader["actual_model"], System.Globalization.CultureInfo.InvariantCulture)!,
                ReportingSource = reader["reporting_source"] == DBNull.Value ? null : Convert.ToString(reader["reporting_source"], System.Globalization.CultureInfo.InvariantCulture)!,
                OccurredUtc = ParseUtc(reader["occurred_utc"]),
                FailureClassification = reader["failure_classification"] == DBNull.Value ? null : Convert.ToString(reader["failure_classification"], System.Globalization.CultureInfo.InvariantCulture)!,
            };
        }
    }
}
