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

    internal sealed class MissionOutcomeEventDefinition
    {
        public string EventType { get; set; } = String.Empty;

        public string EventMessage { get; set; } = String.Empty;
    }
}
