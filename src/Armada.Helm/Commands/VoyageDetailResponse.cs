namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Response wrapper for GET /api/v1/voyages/{id}.
    /// </summary>
    public class VoyageDetailResponse
    {
        /// <summary>
        /// Voyage details.
        /// </summary>
        public Voyage? Voyage { get; set; }

        /// <summary>
        /// Missions in this voyage.
        /// </summary>
        public List<Mission>? Missions { get; set; }
    }
}
