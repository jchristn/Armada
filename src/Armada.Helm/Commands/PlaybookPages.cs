namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Reads every playbook the caller can see by walking the bounded pages of GET /api/v1/playbooks, so a lookup by
    /// file name never treats the first page as the whole catalog.
    /// </summary>
    public static class PlaybookPages
    {
        /// <summary>
        /// Read every visible playbook, one page of <see cref="CollectionLimits.MaxPageSize"/> at a time.
        /// </summary>
        /// <param name="get">The command's GET helper.</param>
        /// <returns>Every visible playbook.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="get"/> is null.</exception>
        public static Task<List<Playbook>> ReadAllAsync(Func<string, Task<EnumerationResult<Playbook>?>> get)
        {
            if (get == null) throw new ArgumentNullException(nameof(get));
            return ArmadaPaging.ReadAllAsync<Playbook>((page, ct) => get("/api/v1/playbooks?pageSize=" + CollectionLimits.MaxPageSize + "&pageNumber=" + page));
        }
    }
}
