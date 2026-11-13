namespace Armada.Core.Services
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Read one page of a source that pages itself (a database enumeration or an external API that takes a page number
    /// and size). The result's <see cref="EnumerationResult{T}.TotalRecords"/> must be the source's exact total.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="pageSize">Items per page.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The page.</returns>
    public delegate Task<EnumerationResult<T>> PageFetchDelegate<T>(int pageNumber, int pageSize, CancellationToken token);
}
