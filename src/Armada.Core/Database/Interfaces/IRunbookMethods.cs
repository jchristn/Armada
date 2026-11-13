namespace Armada.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database reads for runbooks. A runbook is a playbook row (its metadata is stored as a JSON block at the top of the
    /// playbook content), so runbooks are paged and counted in the playbooks table.
    /// </summary>
    public interface IRunbookMethods
    {
        /// <summary>
        /// Enumerate one page of the playbooks behind the runbooks matching the query's filters and scope, ordered by
        /// file name then id, with an exact total from a COUNT over the same filter. WorkflowProfileId, EnvironmentId, and
        /// DefaultCheckType match the runbook metadata block; Search matches the file name, description, and content
        /// (title, steps, and overview), ignoring case.
        /// </summary>
        /// <param name="query">Filters, scope (TenantId, VisibleToUserId), and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of playbooks.</returns>
        Task<EnumerationResult<Playbook>> EnumerateAsync(RunbookQuery query, CancellationToken token = default);
    }
}
