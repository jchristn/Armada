namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;

    /// <summary>
    /// SqlServer stage submission persistence. The SQL is shared (<see cref="StageSubmissionMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class StageSubmissionMethods : StageSubmissionMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SqlServer driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public StageSubmissionMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
