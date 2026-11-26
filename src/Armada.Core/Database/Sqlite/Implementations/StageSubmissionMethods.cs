namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;

    /// <summary>
    /// Sqlite stage submission persistence. The SQL is shared (<see cref="StageSubmissionMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class StageSubmissionMethods : StageSubmissionMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Sqlite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public StageSubmissionMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
