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

    internal sealed class WorktreePlaybookLocation
    {
        public string ResolvedPath { get; set; } = String.Empty;

        public string RelativePath { get; set; } = String.Empty;
    }
}
