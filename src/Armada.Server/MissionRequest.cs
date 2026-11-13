namespace Armada.Server
{
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using SyslogLogging;
    using Voltaic;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server.Mcp;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Request model for a mission within a voyage request.
    /// </summary>
    public class MissionRequest
    {
        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Mission description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Optional required/fallback capability tier for dispatch routing. When a preferred captain is set
        /// via <see cref="RequestedCaptainId"/>, this is the fallback tier used when that captain is busy.
        /// </summary>
        public CaptainTierEnum? Tier { get; set; } = null;

        /// <summary>
        /// Optional preferred (dictated) captain identifier for this mission. When set and idle, dispatch
        /// assigns it; when busy, dispatch falls back by <see cref="Tier"/>.
        /// </summary>
        public string? RequestedCaptainId { get; set; } = null;

        /// <summary>
        /// Optional key naming this mission within the request, so a later mission can name it as its ParentKey.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Optional Key of an earlier mission in the same request: this mission becomes its child and starts once the
        /// parent's last stage finishes. An unknown or later key is a 400 and creates nothing.
        /// </summary>
        public string? ParentKey { get; set; } = null;

        /// <summary>
        /// Optional branch relationship to the parent: New, ContinueParent or ForkFromParent. Defaults to New for a root
        /// and to ContinueParent for the first child of a parent.
        /// </summary>
        public MissionBranchStrategyEnum? BranchStrategy { get; set; } = null;

        /// <summary>
        /// Optional files or directories this mission may change; missions that can run at the same time should not
        /// overlap.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? OwnedPaths { get; set; } = null;
    }
}
