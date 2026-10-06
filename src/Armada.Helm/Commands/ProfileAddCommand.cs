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
    /// Add or update a server profile (shared with <c>armada tui</c>) and optionally store its bearer token.
    /// </summary>
    [Description("Add or update an Admiral profile and store its token")]
    public class ProfileAddCommand : AsyncCommand<ProfileAddSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ProfileAddSettings settings, CancellationToken cancellationToken)
        {
            string name = (settings.Name ?? String.Empty).Trim();
            if (String.Equals(name, AdmiralTargetResolver.LocalProfileName, StringComparison.OrdinalIgnoreCase))
                throw new AdmiralTargetException(AdmiralTargetErrorEnum.ReservedProfileName, "'local' is reserved for this machine's Admiral; choose another profile name.", "profile add");
            if (String.IsNullOrWhiteSpace(settings.Server))
                throw new AdmiralTargetException(AdmiralTargetErrorEnum.InvalidServerUrl, "'armada profile add' needs --server <url>.", "profile add");
            string url = AdmiralTargetResolver.NormalizeServerUrl(settings.Server, "--server");

            string? token = String.IsNullOrWhiteSpace(settings.Token) ? null : settings.Token.Trim();
            if (token == null && !settings.NoToken && !Console.IsInputRedirected)
            {
                string entered = AnsiConsole.Prompt(new TextPrompt<string>("Bearer token for " + Markup.Escape(url) + " (empty to skip):").Secret().AllowEmpty());
                token = String.IsNullOrWhiteSpace(entered) ? null : entered.Trim();
            }

            PreferencesService prefs = new PreferencesService();
            prefs.Load();
            ICredentialStore credentials = CredentialStoreFactory.Create();
            string? previousActive = prefs.Current.ActiveProfile;

            ServerProfile? existing = prefs.FindProfile(name);
            if (existing != null && !String.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase))
                await credentials.DeleteAsync(SessionService.CredentialKey(existing), cancellationToken).ConfigureAwait(false);

            ServerProfile profile = prefs.UpsertProfile(name, url);
            profile.FollowsLocalAdmiral = false;
            if (token != null) profile.AuthMethod = "apikey";
            prefs.Current.ActiveProfile = settings.Use ? profile.Name : previousActive;
            if (!prefs.Save())
            {
                AnsiConsole.MarkupLine("[red]Could not save[/] " + Markup.Escape(prefs.FilePath) + ": " + Markup.Escape(prefs.LastError ?? "unknown error"));
                return 1;
            }

            if (token != null)
            {
                bool stored = await credentials.SetAsync(SessionService.CredentialKey(profile), token, cancellationToken).ConfigureAwait(false);
                if (!stored)
                {
                    AnsiConsole.MarkupLine("[red]Could not store the token[/] in the " + Markup.Escape(credentials.Name) + " credential store.");
                    return 1;
                }
            }

            AnsiConsole.MarkupLine("[green]Saved profile[/] [bold]" + Markup.Escape(profile.Name) + "[/] -> " + Markup.Escape(profile.Url)
                + (token != null ? " [dim](token stored in " + Markup.Escape(credentials.Name) + ")[/]" : " [dim](no token stored)[/]"));
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !AdmiralTarget.IsLoopbackHost(new Uri(url).Host))
                AnsiConsole.MarkupLine("[gold1]Warning:[/] this URL is plain HTTP; tokens would cross the network unencrypted. Prefer https:// through a TLS-terminating proxy.");
            AnsiConsole.MarkupLine(settings.Use
                ? "[dim]It is now the active target for the CLI and armada tui.[/]"
                : "[dim]Use it with --profile " + Markup.Escape(profile.Name) + ", or make it the default with 'armada profile use " + Markup.Escape(profile.Name) + "'.[/]");
            return 0;
        }
    }
}
