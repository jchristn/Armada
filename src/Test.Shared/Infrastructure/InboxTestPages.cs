namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Reads every inbox item for a test by paging <see cref="InboxService.EnumerateAsync"/> explicitly (the service
    /// has no read-everything method).
    /// </summary>
    public static class InboxTestPages
    {
        #region Public-Methods

        /// <summary>
        /// Every inbox item for the caller, in inbox order, read 100 at a time.
        /// </summary>
        /// <param name="inbox">Inbox service.</param>
        /// <param name="auth">Caller.</param>
        /// <returns>The items.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<List<InboxItem>> AllAsync(InboxService inbox, AuthContext auth)
        {
            if (inbox == null) throw new ArgumentNullException(nameof(inbox));
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            List<InboxItem> items = new List<InboxItem>();
            for (int pageNumber = 1; pageNumber <= 1000; pageNumber++)
            {
                EnumerationResult<InboxItem> page = await inbox.EnumerateAsync(auth, new EnumerationQuery { PageNumber = pageNumber, PageSize = 100 }).ConfigureAwait(false);
                items.AddRange(page.Objects);
                if (page.Objects.Count == 0 || pageNumber >= page.TotalPages) break;
            }

            return items;
        }

        #endregion
    }
}
