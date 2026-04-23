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
    /// Request model for creating a voyage via API.
    /// </summary>
    public class VoyageRequest
    {
        /// <summary>
        /// Voyage title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Voyage description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Target vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Pipeline ID to use for this voyage (overrides vessel/fleet default).
        /// </summary>
        public string? PipelineId { get; set; }

        /// <summary>
        /// Pipeline name to use (convenience alias for PipelineId -- resolves by name).
        /// </summary>
        public string? Pipeline { get; set; }

        /// <summary>
        /// List of missions to create.
        /// </summary>
        public List<MissionRequest> Missions { get; set; } = new List<MissionRequest>();

        /// <summary>
        /// Ordered playbooks to apply to every mission in the voyage.
        /// </summary>
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();
    }
}
