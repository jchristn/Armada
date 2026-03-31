namespace Armada.Core.Services
{
    using Armada.Core.Models;

    /// <summary>
    /// Resolves entities by ID, name, or substring match.
    /// Used by CLI commands to support name-based lookups.
    /// </summary>
    public static class EntityResolver
    {
        #region Public-Methods

        /// <summary>
        /// Resolve a vessel by ID or name from a list.
        /// </summary>
        /// <param name="vessels">Available vessels.</param>
        /// <param name="identifier">ID, name, or substring to match.</param>
        /// <returns>Matched vessel, or null.</returns>
        public static Vessel? ResolveVessel(List<Vessel> vessels, string identifier)
        {
            if (vessels == null || vessels.Count == 0) return null;

            // Exact ID match
            Vessel? match = vessels.Find(v => v.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Exact name match
            match = vessels.Find(v => v.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Substring match on name
            List<Vessel> partials = vessels.FindAll(v => v.Name.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (partials.Count == 1) return partials[0];

            // Substring match on repo URL
            partials = vessels.FindAll(v => !String.IsNullOrEmpty(v.RepoUrl) && v.RepoUrl.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (partials.Count == 1) return partials[0];

            return null;
        }

        /// <summary>
        /// Resolve a captain by ID or name from a list.
        /// </summary>
        /// <param name="captains">Available captains.</param>
        /// <param name="identifier">ID, name, or substring to match.</param>
        /// <returns>Matched captain, or null.</returns>
        public static Captain? ResolveCaptain(List<Captain> captains, string identifier)
        {
            if (captains == null || captains.Count == 0) return null;

            // Exact ID match
            Captain? match = captains.Find(c => c.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Exact name match
            match = captains.Find(c => c.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Substring match on name
            List<Captain> partials = captains.FindAll(c => c.Name.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (partials.Count == 1) return partials[0];

            return null;
        }

        /// <summary>
        /// Resolve a mission by ID or title substring from a list.
        /// </summary>
        /// <param name="missions">Available missions.</param>
        /// <param name="identifier">ID or title substring to match.</param>
        /// <returns>Matched mission, or null.</returns>
        public static Mission? ResolveMission(List<Mission> missions, string identifier)
        {
            if (missions == null || missions.Count == 0) return null;

            // Exact ID match
            Mission? match = missions.Find(m => m.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Substring match on title
            List<Mission> partials = missions.FindAll(m => m.Title.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (partials.Count == 1) return partials[0];

            return null;
        }

        /// <summary>
        /// Resolve a voyage by ID or title substring from a list.
        /// </summary>
        /// <param name="voyages">Available voyages.</param>
        /// <param name="identifier">ID or title substring to match.</param>
        /// <returns>Matched voyage, or null.</returns>
        public static Voyage? ResolveVoyage(List<Voyage> voyages, string identifier)
        {
            if (voyages == null || voyages.Count == 0) return null;

            // Exact ID match
            Voyage? match = voyages.Find(v => v.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Substring match on title
            List<Voyage> partials = voyages.FindAll(v => v.Title.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (partials.Count == 1) return partials[0];

            return null;
        }

        /// <summary>
        /// Resolve a fleet by ID or name from a list.
        /// </summary>
        /// <param name="fleets">Available fleets.</param>
        /// <param name="identifier">ID or name to match.</param>
        /// <returns>Matched fleet, or null.</returns>
        public static Fleet? ResolveFleet(List<Fleet> fleets, string identifier)
        {
            if (fleets == null || fleets.Count == 0) return null;

            // Exact ID match
            Fleet? match = fleets.Find(f => f.Id.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Exact name match
            match = fleets.Find(f => f.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Substring match on name
            List<Fleet> partials = fleets.FindAll(f => f.Name.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (partials.Count == 1) return partials[0];

            return null;
        }

        /// <summary>
        /// Resolve a vessel by matching its repo URL against a local git remote URL.
        /// </summary>
        /// <param name="vessels">Available vessels.</param>
        /// <param name="remoteUrl">Remote URL from git.</param>
        /// <returns>Matched vessel, or null.</returns>
        public static Vessel? ResolveVesselByRemoteUrl(List<Vessel> vessels, string remoteUrl)
        {
            if (vessels == null || vessels.Count == 0 || string.IsNullOrEmpty(remoteUrl)) return null;

            string normalized = NormalizeGitUrl(remoteUrl);

            return vessels.Find(v => !String.IsNullOrEmpty(v.RepoUrl) && NormalizeGitUrl(v.RepoUrl).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private-Methods

        private static string NormalizeGitUrl(string url)
        {
            // Strip trailing .git
            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(0, url.Length - 4);

            // Strip trailing slash
            url = url.TrimEnd('/');

            // Convert SSH to HTTPS format for comparison
            // git@github.com:user/repo -> github.com/user/repo
            if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(4);
                int colonIndex = url.IndexOf(':');
                if (colonIndex > 0)
                    url = url.Substring(0, colonIndex) + "/" + url.Substring(colonIndex + 1);
            }

            // Strip protocol
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(8);
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(7);

            return url;
        }

        #endregion
    }
}
