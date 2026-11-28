namespace Armada.Core.Database.Mysql.Implementations
{
    using System;

    /// <summary>
    /// Mysql recovery state persistence. The SQL is shared (<see cref="RecoveryStateMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class RecoveryStateMethods : RecoveryStateMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">Mysql connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public RecoveryStateMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
