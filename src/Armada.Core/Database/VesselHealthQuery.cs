namespace Armada.Core.Database
{
    using System.Collections.Generic;

    /// <summary>
    /// Provider-neutral SQL fragments for a vessel health enumeration: the FROM/JOIN clause, the WHERE clause, the
    /// ORDER BY clause, and the parameters they reference. Providers append their own paging syntax.
    /// </summary>
    internal class VesselHealthQuery
    {
        #region Internal-Members

        /// <summary>
        /// FROM clause joining vessels (v), vessel_health (h), and fleets (f).
        /// </summary>
        internal string FromClause { get; set; } = string.Empty;

        /// <summary>
        /// WHERE clause including the leading WHERE keyword.
        /// </summary>
        internal string WhereClause { get; set; } = string.Empty;

        /// <summary>
        /// ORDER BY clause including the leading ORDER BY keyword.
        /// </summary>
        internal string OrderByClause { get; set; } = string.Empty;

        /// <summary>
        /// Parameters referenced by the clauses.
        /// </summary>
        internal List<QueryParameter> Parameters { get; } = new List<QueryParameter>();

        #endregion
    }
}
