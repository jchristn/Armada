namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Armada.Core.Enums;

    /// <summary>
    /// Filter and page for listing background jobs (newest first). Used by <c>GET /api/v1/jobs</c> when the caller passes
    /// paging or filter parameters, by the dashboard's header indicator (active jobs only), and by maintenance that only
    /// needs jobs in particular states.
    /// </summary>
    public class JobQuery
    {
        #region Public-Members

        /// <summary>
        /// Restrict to one tenant. Null for every tenant (global admins and maintenance).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Restrict to one user within the tenant. Null for every user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Restrict to these statuses. Empty for every status.
        /// </summary>
        public List<JobStatusEnum> Statuses
        {
            get { return _Statuses; }
            set { _Statuses = value ?? new List<JobStatusEnum>(); }
        }

        /// <summary>
        /// Restrict to one kind. Null for every kind.
        /// </summary>
        public JobKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// One-based page number. Default 1, minimum 1.
        /// </summary>
        public int PageNumber
        {
            get { return _PageNumber; }
            set { _PageNumber = value < 1 ? 1 : value; }
        }

        /// <summary>
        /// Page size. Default 100, clamped to 1-1000.
        /// </summary>
        public int PageSize
        {
            get { return _PageSize; }
            set { _PageSize = value < 1 ? 1 : (value > 1000 ? 1000 : value); }
        }

        /// <summary>
        /// Rows to skip for the current page.
        /// </summary>
        public int Offset
        {
            get { return (_PageNumber - 1) * _PageSize; }
        }

        #endregion

        #region Private-Members

        private List<JobStatusEnum> _Statuses = new List<JobStatusEnum>();
        private int _PageNumber = 1;
        private int _PageSize = 100;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a query from querystring parameters: pageNumber, pageSize, status (comma-separated JobStatusEnum names,
        /// case-insensitive), and kind (a JobKindEnum name).
        /// </summary>
        /// <param name="getter">Returns a querystring value by name, or null.</param>
        /// <param name="query">The query, or null when no paging or filter parameter was given (callers keep the legacy
        /// unpaged list) or when a value is invalid.</param>
        /// <param name="error">Why a value was rejected, or null.</param>
        /// <returns>True when a query was built.</returns>
        /// <exception cref="ArgumentNullException">getter is null.</exception>
        public static bool TryFromQuerystring(Func<string, string?> getter, out JobQuery? query, out string? error)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            query = null;
            error = null;

            string? pageNumber = getter("pageNumber");
            string? pageSize = getter("pageSize");
            string? status = getter("status");
            string? kind = getter("kind");
            if (String.IsNullOrWhiteSpace(pageNumber) && String.IsNullOrWhiteSpace(pageSize) && String.IsNullOrWhiteSpace(status) && String.IsNullOrWhiteSpace(kind))
                return false;

            JobQuery built = new JobQuery();
            if (!String.IsNullOrWhiteSpace(pageNumber))
            {
                if (!Int32.TryParse(pageNumber, out int number)) { error = "pageNumber must be an integer"; return false; }
                built.PageNumber = number;
            }
            if (!String.IsNullOrWhiteSpace(pageSize))
            {
                if (!Int32.TryParse(pageSize, out int size)) { error = "pageSize must be an integer"; return false; }
                built.PageSize = size;
            }
            if (!String.IsNullOrWhiteSpace(status))
            {
                foreach (string part in status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Enum.TryParse<JobStatusEnum>(part, true, out JobStatusEnum parsed) || Int32.TryParse(part, out int _))
                    {
                        error = "unknown job status '" + part + "'";
                        return false;
                    }
                    if (!built.Statuses.Contains(parsed)) built.Statuses.Add(parsed);
                }
            }
            if (!String.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<JobKindEnum>(kind.Trim(), true, out JobKindEnum parsedKind) || Int32.TryParse(kind, out int _))
                {
                    error = "unknown job kind '" + kind + "'";
                    return false;
                }
                built.Kind = parsedKind;
            }

            query = built;
            return true;
        }

        /// <summary>
        /// Build the WHERE clause (including the leading " WHERE ", or empty) and its named parameters. Parameter names
        /// use the @ prefix, which every supported provider accepts; values are strings.
        /// </summary>
        /// <param name="parameters">Receives the parameters to bind.</param>
        /// <returns>The WHERE clause.</returns>
        public string BuildWhereClause(out List<KeyValuePair<string, object>> parameters)
        {
            parameters = new List<KeyValuePair<string, object>>();
            List<string> clauses = new List<string>();
            if (!String.IsNullOrEmpty(TenantId))
            {
                clauses.Add("tenant_id = @tenant_id");
                parameters.Add(new KeyValuePair<string, object>("@tenant_id", TenantId));
            }
            if (!String.IsNullOrEmpty(UserId))
            {
                clauses.Add("user_id = @user_id");
                parameters.Add(new KeyValuePair<string, object>("@user_id", UserId));
            }
            if (_Statuses.Count > 0)
            {
                StringBuilder inList = new StringBuilder();
                for (int i = 0; i < _Statuses.Count; i++)
                {
                    if (i > 0) inList.Append(", ");
                    inList.Append("@status").Append(i);
                    parameters.Add(new KeyValuePair<string, object>("@status" + i, _Statuses[i].ToString()));
                }
                clauses.Add("status IN (" + inList + ")");
            }
            if (Kind.HasValue)
            {
                clauses.Add("kind = @kind");
                parameters.Add(new KeyValuePair<string, object>("@kind", Kind.Value.ToString()));
            }
            return clauses.Count == 0 ? String.Empty : " WHERE " + String.Join(" AND ", clauses);
        }

        /// <summary>
        /// Wrap a page of rows and the total count in an enumeration result for this query.
        /// </summary>
        /// <param name="page">Rows of the current page.</param>
        /// <param name="total">Total matching rows.</param>
        /// <returns>The result.</returns>
        public EnumerationResult<Job> ToResult(List<Job> page, long total)
        {
            EnumerationResult<Job> result = new EnumerationResult<Job>();
            result.PageNumber = _PageNumber;
            result.PageSize = _PageSize;
            result.TotalRecords = total;
            result.TotalPages = (int)Math.Ceiling((double)total / _PageSize);
            result.Objects = page ?? new List<Job>();
            return result;
        }

        #endregion
    }
}
