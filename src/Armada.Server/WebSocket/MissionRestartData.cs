namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using SwiftStack;
    using SwiftStack.Websockets;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Data payload for the restart_mission WebSocket command.
    /// </summary>
    public class MissionRestartData
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
