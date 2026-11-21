namespace Armada.Server.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;

    /// <summary>
    /// Tells an Ask turn that runs on a Harbor where the conversation owner's vessels are checked out on that machine. The
    /// turn runs in a scratch folder there, and a vessel's LocalPath and WorkingDirectory describe the Admiral's machine,
    /// so without this the captain has no way to find a repository's files.
    /// </summary>
    public static class AskHarborCheckoutContext
    {
        #region Public-Members

        /// <summary>
        /// Most vessels listed for one turn.
        /// </summary>
        public const int MaxVessels = 200;

        /// <summary>
        /// How long a turn waits for vessels the Harbor was not asked about yet; lookups still running then continue in
        /// the background for the next turn.
        /// </summary>
        public static readonly TimeSpan LookupWait = TimeSpan.FromSeconds(10);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the prompt section for a turn on a Harbor: the owner's active vessels that the Harbor has a checkout of.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="checkouts">Where Harbors have vessels checked out (asks the Harbor and remembers its answers).</param>
        /// <param name="threads">Ask thread service (for the owner's role).</param>
        /// <param name="thread">The conversation.</param>
        /// <param name="harborId">The Harbor the turn runs on.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The section, or null when the Harbor has no checkout of any of the owner's vessels.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task<string?> BuildAsync(DatabaseDriver database, HarborCheckoutDirectory checkouts, AskThreadService threads, AskThread thread, string harborId, CancellationToken token)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (checkouts == null) throw new ArgumentNullException(nameof(checkouts));
            if (threads == null) throw new ArgumentNullException(nameof(threads));
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (String.IsNullOrWhiteSpace(harborId) || String.IsNullOrEmpty(thread.TenantId)) return null;

            // The vessels the owner can see, as through the turn's scoped MCP session: the tenant's for an admin, else
            // the owner's own.
            AuthContext? owner = await threads.ReadOwnerContextAsync(thread, token).ConfigureAwait(false);
            bool tenantWide = owner != null && (owner.IsAdmin || owner.IsTenantAdmin);
            List<Vessel> vessels = tenantWide || String.IsNullOrEmpty(thread.UserId)
                ? await database.Vessels.EnumerateAsync(thread.TenantId!, token).ConfigureAwait(false)
                : await database.Vessels.EnumerateAsync(thread.TenantId!, thread.UserId!, token).ConfigureAwait(false);
            List<Vessel> candidates = vessels
                .Where(v => v.Active)
                .OrderByDescending(v => v.LastUpdateUtc)
                .Take(MaxVessels)
                .ToList();
            if (candidates.Count == 0) return null;

            List<HarborVesselCheckout> found = await checkouts.GetAsync(harborId, candidates, LookupWait, token).ConfigureAwait(false);
            string harborName = checkouts.HarborName(harborId);
            return Format(harborName, found);
        }

        /// <summary>
        /// Format the prompt section.
        /// </summary>
        /// <param name="harborName">Harbor name (or description).</param>
        /// <param name="checkouts">Checkouts on the Harbor.</param>
        /// <returns>The section, or null when there are no checkouts.</returns>
        public static string? Format(string harborName, List<HarborVesselCheckout> checkouts)
        {
            if (checkouts == null || checkouts.Count == 0) return null;
            StringBuilder text = new StringBuilder();
            text.AppendLine("## Repository checkouts on this machine");
            text.AppendLine("This turn runs on the Harbor " + harborName + " (see Where you are running for its working directory). "
                + "These vessels are checked out on this machine; read their files at these absolute paths. "
                + "A vessel's LocalPath and WorkingDirectory describe the Armada server's machine, not this one.");
            foreach (HarborVesselCheckout checkout in checkouts.OrderBy(c => c.VesselName, StringComparer.OrdinalIgnoreCase))
                text.AppendLine("- " + checkout.VesselName + " (" + checkout.VesselId + "): " + checkout.Path);
            return text.ToString().TrimEnd();
        }

        #endregion
    }
}
