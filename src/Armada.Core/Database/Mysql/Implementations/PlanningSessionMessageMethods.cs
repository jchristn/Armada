namespace Armada.Core.Database.Mysql.Implementations
{
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL stub for planning session message operations.
    /// </summary>
    public class PlanningSessionMessageMethods : IPlanningSessionMessageMethods
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">Database connection string.</param>
        public PlanningSessionMessageMethods(string connectionString)
        {
        }

        /// <inheritdoc />
        public Task<PlanningSessionMessage> CreateAsync(PlanningSessionMessage message, CancellationToken token = default) => throw NotSupported();
        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken token = default) => throw NotSupported();
        /// <inheritdoc />
        public Task DeleteBySessionAsync(string planningSessionId, CancellationToken token = default) => throw NotSupported();
        /// <inheritdoc />
        public Task<List<PlanningSessionMessage>> EnumerateBySessionAsync(string planningSessionId, CancellationToken token = default) => throw NotSupported();
        /// <inheritdoc />
        public Task<PlanningSessionMessage?> ReadAsync(string id, CancellationToken token = default) => throw NotSupported();
        /// <inheritdoc />
        public Task<PlanningSessionMessage> UpdateAsync(PlanningSessionMessage message, CancellationToken token = default) => throw NotSupported();

        /// <inheritdoc />
        public Task<EnumerationResult<PlanningSessionMessage>> EnumeratePageBySessionAsync(string planningSessionId, EnumerationQuery query, CancellationToken token = default) => throw NotSupported();
        /// <inheritdoc />
        public Task<long> CountBySessionAsync(string planningSessionId, CancellationToken token = default) => throw NotSupported();

        private static NotSupportedException NotSupported()
        {
            return new NotSupportedException("Planning sessions are currently implemented for SQLite-backed Armada deployments.");
        }
    }
}
