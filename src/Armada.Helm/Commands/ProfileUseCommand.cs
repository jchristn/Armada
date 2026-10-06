namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;
    using Armada.Tui.Services;

    /// <summary>
    /// Make a profile the active target for the CLI and <c>armada tui</c>; <c>local</c> returns to this machine's Admiral.
    /// </summary>
    [Description("Make a profile the active target ('local' for this machine's Admiral)")]
    public class ProfileUseCommand : AsyncCommand<ProfileNameSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ProfileNameSettings settings, CancellationToken cancellationToken)
        {
            string name = (settings.Name ?? String.Empty).Trim();
            PreferencesService prefs = new PreferencesService();
            prefs.Load();

            ServerProfile? chosen;
            if (String.Equals(name, AdmiralTargetResolver.LocalProfileName, StringComparison.OrdinalIgnoreCase))
            {
                chosen = prefs.Current.Profiles.Find(p => AdmiralTargetResolver.FollowsLocalAdmiral(p));
                if (chosen == null)
                {
                    // Same shape as the profile the TUI creates for the local Admiral.
                    ArmadaSettings local = await ArmadaSettings.LoadAsync().ConfigureAwait(false);
                    string defaultName = prefs.FindProfile("default") == null ? "default" : "local-admiral";
                    chosen = prefs.UpsertProfile(defaultName, AdmiralTargetResolver.LocalBaseUrl(local.AdmiralPort));
                    chosen.FollowsLocalAdmiral = true;
                }
            }
            else
            {
                chosen = prefs.FindProfile(name);
                if (chosen == null)
                    throw new AdmiralTargetException(AdmiralTargetErrorEnum.UnknownProfile, "No profile named '" + name + "'. List profiles with 'armada profile list'.", "profile use");
            }

            prefs.Current.ActiveProfile = chosen.Name;
            if (!prefs.Save())
            {
                AnsiConsole.MarkupLine("[red]Could not save[/] " + Markup.Escape(prefs.FilePath) + ": " + Markup.Escape(prefs.LastError ?? "unknown error"));
                return 1;
            }

            bool isLocal = AdmiralTargetResolver.FollowsLocalAdmiral(chosen);
            AnsiConsole.MarkupLine("[green]Active target:[/] " + (isLocal ? "this machine's Admiral" : "profile [bold]" + Markup.Escape(chosen.Name) + "[/]") + " (" + Markup.Escape(chosen.Url) + ")");
            AnsiConsole.MarkupLine("[dim]ARMADA_SERVER_URL and --server/--profile still take precedence when set.[/]");
            return 0;
        }
    }
}
