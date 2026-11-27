namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Parsed mission definition extracted from an architect's output.
    /// </summary>
    internal class ParsedArchitectMission
    {
        /// <summary>
        /// Worker eligibility envelope the Architect set for this mission (structured armada-plan format), or null.
        /// </summary>
        public WorkerEnvelope? WorkerEnvelope { get; set; } = null;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Mission description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Optional dependency reference emitted by the architect (legacy marker format: a "Depends on:" line).
        /// </summary>
        public string? DependsOnReference { get; set; } = null;

        /// <summary>
        /// 1-based index of an earlier parsed mission this one depends on (structured armada-plan format).
        /// </summary>
        public int? DependsOnIndex { get; set; } = null;

        /// <summary>
        /// Whether the mission waits until the other Worker missions in the voyage settle.
        /// </summary>
        public bool WaitForOtherMissions { get; set; } = false;

        /// <summary>Files or directories the mission may change (plan ownedPaths or a legacy "Owns:" line).</summary>
        public List<string> OwnedPaths { get; set; } = new List<string>();

        /// <summary>Context the mission must not change (plan readsPaths).</summary>
        public List<string> ReadsPaths { get; set; } = new List<string>();

        /// <summary>Plan branchStrategy (continue or fork), or null for the default rule.</summary>
        public string? BranchStrategy { get; set; } = null;
    }
}
