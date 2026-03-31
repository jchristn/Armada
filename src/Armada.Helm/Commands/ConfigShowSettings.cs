namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Text.Json;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for config show command.
    /// </summary>
    public class ConfigShowSettings : BaseSettings
    {
    }
}
