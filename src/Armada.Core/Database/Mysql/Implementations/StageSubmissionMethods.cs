namespace Armada.Core.Database.Mysql.Implementations
{
    using System;

    /// <summary>
    /// Mysql stage submission persistence. The SQL is shared (<see cref="StageSubmissionMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class StageSubmissionMethods : StageSubmissionMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">Mysql connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public StageSubmissionMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
