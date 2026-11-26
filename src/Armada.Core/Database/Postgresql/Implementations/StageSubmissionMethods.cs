namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;

    /// <summary>
    /// Postgresql stage submission persistence. The SQL is shared (<see cref="StageSubmissionMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class StageSubmissionMethods : StageSubmissionMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Postgresql driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public StageSubmissionMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
