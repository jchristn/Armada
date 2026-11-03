namespace Armada.Core.Database
{
    using System.Data.Common;
    using System.Globalization;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal sealed class MissionWaitIntervalMethods : IMissionWaitIntervalMethods
    {
        private readonly Func<DbConnection> _Factory;
        private readonly SqliteWriteGate? _Gate;
        internal MissionWaitIntervalMethods(Func<DbConnection> factory, SqliteWriteGate? gate = null) { _Factory = factory; _Gate = gate; }
        private static object Date(DbConnection connection, DateTime value) => connection.GetType().Name.Contains("Sqlite") || connection.GetType().Name == "SqlConnection"
            ? value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : value.ToUniversalTime();
        private static void Add(DbCommand cmd, string name, object? value) { DbParameter p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value; cmd.Parameters.Add(p); }
        private async Task<T> Run<T>(Func<DbConnection, Task<T>> action, CancellationToken token)
        {
            using IDisposable? lease = _Gate != null ? await _Gate.EnterAsync(token).ConfigureAwait(false) : null;
            using DbConnection connection = _Factory(); await connection.OpenAsync(token).ConfigureAwait(false);
            return await action(connection).ConfigureAwait(false);
        }
        public Task<MissionWaitInterval> CreateAsync(MissionWaitInterval interval, CancellationToken token = default)
        {
            if (interval == null) throw new ArgumentNullException(nameof(interval));
            if (String.IsNullOrWhiteSpace(interval.MissionId)) throw new ArgumentException("MissionId is required", nameof(interval));
            if (interval.EndedUtc < interval.StartedUtc) throw new ArgumentException("EndedUtc precedes StartedUtc", nameof(interval));
            return Run(async c =>
            {
                using DbCommand cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO mission_wait_intervals(id,tenant_id,mission_id,vessel_id,voyage_id,captain_id,gate,started_utc,ended_utc,detail) VALUES(@id,@tenant,@mission,@vessel,@voyage,@captain,@gate,@started,@ended,@detail);";
                Add(cmd, "@id", interval.Id); Add(cmd, "@tenant", interval.TenantId); Add(cmd, "@mission", interval.MissionId); Add(cmd, "@vessel", interval.VesselId); Add(cmd, "@voyage", interval.VoyageId); Add(cmd, "@captain", interval.CaptainId); Add(cmd, "@gate", interval.Gate.ToString()); Add(cmd, "@started", Date(c, interval.StartedUtc)); Add(cmd, "@ended", interval.EndedUtc.HasValue ? Date(c, interval.EndedUtc.Value) : null); Add(cmd, "@detail", interval.Detail);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); return interval;
            }, token);
        }
        public Task<List<MissionWaitInterval>> EnumerateByMissionAsync(string missionId, string? tenantId = null, CancellationToken token = default)
            => Run(async c =>
            {
                using DbCommand cmd = c.CreateCommand(); cmd.CommandText = "SELECT * FROM mission_wait_intervals WHERE mission_id=@mission" + (tenantId == null ? "" : " AND tenant_id=@tenant") + " ORDER BY started_utc,id;";
                Add(cmd, "@mission", missionId); if (tenantId != null) Add(cmd, "@tenant", tenantId);
                using DbDataReader r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false); List<MissionWaitInterval> rows = new();
                string? Text(string key) => r[key] == DBNull.Value ? null : r[key].ToString();
                DateTime Time(string key) => r[key] is DateTime value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : DateTime.Parse((string)r[key], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                while (await r.ReadAsync(token).ConfigureAwait(false)) rows.Add(new MissionWaitInterval { Id = Text("id")!, TenantId = Text("tenant_id"), MissionId = Text("mission_id")!, VesselId = Text("vessel_id"), VoyageId = Text("voyage_id"), CaptainId = Text("captain_id"), Gate = Enum.Parse<MissionWaitGateEnum>(Text("gate")!), StartedUtc = Time("started_utc"), EndedUtc = r["ended_utc"] == DBNull.Value ? null : Time("ended_utc"), Detail = Text("detail") });
                return rows;
            }, token);
        public Task<int> CloseOpenAsync(string missionId, DateTime endedUtc, CancellationToken token = default)
            => Run(async c => { using DbCommand cmd = c.CreateCommand(); cmd.CommandText = "UPDATE mission_wait_intervals SET ended_utc=@ended WHERE mission_id=@mission AND ended_utc IS NULL AND started_utc<=@ended;"; Add(cmd, "@mission", missionId); Add(cmd, "@ended", Date(c, endedUtc)); return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); }, token);
        public Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken token = default)
            => Run(async c => { using DbCommand cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM mission_wait_intervals WHERE ended_utc<@cutoff;"; Add(cmd, "@cutoff", Date(c, cutoffUtc)); return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); }, token);
    }
}
