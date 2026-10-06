namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Helm.Infrastructure;
    using Armada.Helm.Rendering;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;

    /// <summary>
    /// List the server profiles shared by the CLI and <c>armada tui</c> (stored in <c>tui.json</c>; tokens in the OS
    /// keychain or a 0600 file).
    /// </summary>
    [Description("List saved Admiral profiles (shared with armada tui)")]
    public class ProfileListCommand : AsyncCommand<ProfileListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ProfileListSettings settings, CancellationToken cancellationToken)
        {
            PreferencesService prefs = new PreferencesService();
            prefs.Load();
            ICredentialStore credentials = CredentialStoreFactory.Create();
            ServerProfile? active = prefs.FindProfile(prefs.Current.ActiveProfile);

            List<ProfileListEntry> entries = new List<ProfileListEntry>();
            foreach (ServerProfile profile in prefs.Current.Profiles)
            {
                ProfileListEntry entry = new ProfileListEntry();
                entry.Name = profile.Name;
                entry.Url = profile.Url;
                entry.Active = active != null && ReferenceEquals(active, profile);
                entry.Local = AdmiralTargetResolver.FollowsLocalAdmiral(profile);
                entry.HasToken = !entry.Local && !String.IsNullOrEmpty(await credentials.GetAsync(SessionService.CredentialKey(profile), cancellationToken).ConfigureAwait(false));
                entries.Add(entry);
            }

            bool localActive = active == null || AdmiralTargetResolver.FollowsLocalAdmiral(active);
            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            Table table = TableRenderer.CreateTable("Admiral Profiles", null);
            table.AddColumn("");
            table.AddColumn("Name");
            table.AddColumn("URL");
            table.AddColumn("Kind");
            table.AddColumn("Token");
            if (entries.Count == 0 || entries.TrueForAll(e => !e.Local))
                table.AddRow(localActive ? "[green]*[/]" : "", "local", "[dim]this machine's Admiral[/]", "local", "[dim]local API key[/]");
            foreach (ProfileListEntry entry in entries)
            {
                table.AddRow(
                    entry.Active || (entry.Local && localActive && active == null) ? "[green]*[/]" : "",
                    Markup.Escape(entry.Name),
                    Markup.Escape(entry.Url),
                    entry.Local ? "local" : "remote",
                    entry.Local ? "[dim]local API key[/]" : (entry.HasToken ? "stored" : "[gold1]none[/]"));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[dim]* marks the active target. Switch with 'armada profile use <name>' ('local' for this machine). Store: " + Markup.Escape(TuiPaths.PreferencesFile()) + ", tokens: " + Markup.Escape(credentials.Name) + "[/]");
            return 0;
        }
    }
}
