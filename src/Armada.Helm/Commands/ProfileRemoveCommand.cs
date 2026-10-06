namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Helm.Infrastructure;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;

    /// <summary>
    /// Remove a profile and its stored token. Removing the active profile makes the local Admiral the target again.
    /// </summary>
    [Description("Remove an Admiral profile and its stored token")]
    public class ProfileRemoveCommand : AsyncCommand<ProfileNameSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ProfileNameSettings settings, CancellationToken cancellationToken)
        {
            string name = (settings.Name ?? String.Empty).Trim();
            if (String.Equals(name, AdmiralTargetResolver.LocalProfileName, StringComparison.OrdinalIgnoreCase))
                throw new AdmiralTargetException(AdmiralTargetErrorEnum.ReservedProfileName, "'local' is this machine's Admiral and cannot be removed.", "profile remove");

            PreferencesService prefs = new PreferencesService();
            prefs.Load();
            ServerProfile? profile = prefs.FindProfile(name);
            if (profile == null)
                throw new AdmiralTargetException(AdmiralTargetErrorEnum.UnknownProfile, "No profile named '" + name + "'. List profiles with 'armada profile list'.", "profile remove");

            ICredentialStore credentials = CredentialStoreFactory.Create();
            await credentials.DeleteAsync(SessionService.CredentialKey(profile), cancellationToken).ConfigureAwait(false);
            prefs.Current.Profiles.Remove(profile);
            bool wasActive = String.Equals(prefs.Current.ActiveProfile, profile.Name, StringComparison.OrdinalIgnoreCase);
            if (wasActive) prefs.Current.ActiveProfile = null;
            if (!prefs.Save())
            {
                AnsiConsole.MarkupLine("[red]Could not save[/] " + Markup.Escape(prefs.FilePath) + ": " + Markup.Escape(prefs.LastError ?? "unknown error"));
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Removed profile[/] " + Markup.Escape(profile.Name) + " and its stored token."
                + (wasActive ? " [dim]The local Admiral is the target again.[/]" : ""));
            return 0;
        }
    }
}
