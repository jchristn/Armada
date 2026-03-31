namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using Spectre.Console.Cli;
    using SyslogLogging;
    using Voltaic;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server.Mcp;

    /// <summary>
    /// Settings for MCP stdio command.
    /// </summary>
    public class McpStdioSettings : CommandSettings
    {
    }
}
