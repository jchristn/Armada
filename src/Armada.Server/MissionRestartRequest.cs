namespace Armada.Server
{
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using SyslogLogging;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
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
    /// Request model for restarting a failed or cancelled mission with optional instruction changes.
    /// </summary>
    public class MissionRestartRequest
    {
        /// <summary>
        /// Optional new title. If null or empty, the original title is preserved.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Optional new description/instructions. If null or empty, the original description is preserved.
        /// </summary>
        public string? Description { get; set; }
    }
}
