namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;

    /// <summary>
    /// SqlServer recovery state persistence. The SQL is shared (<see cref="RecoveryStateMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class RecoveryStateMethods : RecoveryStateMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SqlServer driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public RecoveryStateMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
