namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.BlackBox;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Stage submission persistence shared by the four providers. Thread-safe.
    /// </summary>
    public abstract class StageSubmissionMethodsBase : IStageSubmissionMethods
    {
        private readonly BlackBoxSqlDialect _Sql;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions();

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null.</exception>
        protected StageSubmissionMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when submission is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the mission or attempt id is empty.</exception>
        public async Task<StageSubmission> CreateAsync(StageSubmission submission, CancellationToken token = default)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            if (String.IsNullOrEmpty(submission.MissionId) || String.IsNullOrEmpty(submission.AttemptId))
                throw new ArgumentException("MissionId and AttemptId are required.", nameof(submission));
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx,
                    "INSERT INTO stage_submissions (id, tenant_id, mission_id, attempt_id, persona, kind, source, payload_json, created_utc) "
                    + "VALUES (@id, @tenant_id, @mission_id, @attempt_id, @persona, @kind, @source, @payload_json, @created_utc);",
                    cmd =>
                    {
                        _Sql.Add(cmd, "@id", submission.Id);
                        _Sql.Add(cmd, "@tenant_id", submission.TenantId);
                        _Sql.Add(cmd, "@mission_id", submission.MissionId);
                        _Sql.Add(cmd, "@attempt_id", submission.AttemptId);
                        _Sql.Add(cmd, "@persona", submission.Persona ?? "");
                        _Sql.Add(cmd, "@kind", submission.Kind.ToString());
                        _Sql.Add(cmd, "@source", submission.Source.ToString());
                        _Sql.Add(cmd, "@payload_json", JsonSerializer.Serialize(submission.Submission, _JsonOptions));
                        _Sql.AddDate(cmd, "@created_utc", submission.CreatedUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return submission;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when the mission or attempt id is empty.</exception>
        public async Task<StageSubmission?> ReadLatestForAttemptAsync(string missionId, string attemptId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (String.IsNullOrEmpty(attemptId)) throw new ArgumentNullException(nameof(attemptId));
            List<StageSubmission> rows = await _Sql.QueryAsync(
                "SELECT * FROM stage_submissions WHERE mission_id = @mission_id AND attempt_id = @attempt_id;",
                cmd =>
                {
                    _Sql.Add(cmd, "@mission_id", missionId);
                    _Sql.Add(cmd, "@attempt_id", attemptId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.OrderByDescending(r => r.CreatedUtc).ThenByDescending(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when the mission id is empty.</exception>
        public async Task<List<StageSubmission>> EnumerateByMissionAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            List<StageSubmission> rows = await _Sql.QueryAsync(
                "SELECT * FROM stage_submissions WHERE mission_id = @mission_id;",
                cmd => _Sql.Add(cmd, "@mission_id", missionId), FromReader, token).ConfigureAwait(false);
            return rows.OrderByDescending(r => r.CreatedUtc).ThenByDescending(r => r.Id, StringComparer.Ordinal).ToList();
        }

        private StageSubmission FromReader(DbDataReader reader)
        {
            StageSubmission submission = new StageSubmission();
            submission.Id = reader["id"].ToString() ?? submission.Id;
            submission.TenantId = BlackBoxSqlDialect.ReadString(reader["tenant_id"]);
            submission.MissionId = reader["mission_id"].ToString() ?? "";
            submission.AttemptId = reader["attempt_id"].ToString() ?? "";
            submission.Persona = reader["persona"].ToString() ?? "";
            submission.Kind = BlackBoxSqlDialect.ReadEnum(reader["kind"], StageSubmissionKindEnum.Result);
            submission.Source = BlackBoxSqlDialect.ReadEnum(reader["source"], StageSubmissionSourceEnum.Mcp);
            string payload = reader["payload_json"].ToString() ?? "{}";
            try
            {
                submission.Submission = JsonSerializer.Deserialize<StageSubmissionEnvelope>(payload, _JsonOptions) ?? new StageSubmissionEnvelope { Kind = submission.Kind };
            }
            catch (JsonException)
            {
                submission.Submission = new StageSubmissionEnvelope { Kind = submission.Kind };
            }
            submission.CreatedUtc = _Sql.ReadDate(reader["created_utc"]) ?? DateTime.UtcNow;
            return submission;
        }
    }
}
