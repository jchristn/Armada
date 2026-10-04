namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Settings;

    /// <summary>
    /// Destructively reset Armada back to zero: delete database, logs, docks, and bare repos.
    /// Preserves settings file.
    /// </summary>
    [Description("Destructively reset all Armada data (database, logs, docks, repos)")]
    public class ResetCommand : BaseCommand<ResetSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ResetSettings settings, CancellationToken cancellationToken)
        {
            ArmadaSettings armadaSettings = GetSettings();

            if (!settings.Force)
            {
                AnsiConsole.MarkupLine("[bold red]WARNING: This will permanently delete:[/]");
                AnsiConsole.MarkupLine($"  - Database:  [dim]{Markup.Escape(armadaSettings.DatabasePath)}[/]");
                AnsiConsole.MarkupLine($"  - Logs:      [dim]{Markup.Escape(armadaSettings.LogDirectory)}[/]");
                AnsiConsole.MarkupLine($"  - Docks:     [dim]{Markup.Escape(armadaSettings.DocksDirectory)}[/]");
                AnsiConsole.MarkupLine($"  - Bare repos:[dim] {Markup.Escape(armadaSettings.ReposDirectory)}[/]");
                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("[dim]Settings file will be preserved.[/]");
                AnsiConsole.MarkupLine("");

                if (!AnsiConsole.Confirm("[bold red]Are you sure you want to reset?[/]", defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[dim]Reset cancelled.[/]");
                    return 0;
                }
            }

            // Stop the server if running
            try
            {
                bool healthy = await GetApiClient().HealthCheckAsync().ConfigureAwait(false);
                if (healthy)
                {
                    AnsiConsole.MarkupLine("[dim]Stopping Admiral server...[/]");
                    try { await DeleteAsync("/api/v1/server/shutdown").ConfigureAwait(false); }
                    catch { }
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
            catch { }

            // Kill any captain processes
            try
            {
                System.Diagnostics.Process[] claudes = System.Diagnostics.Process.GetProcessesByName("claude");
                foreach (System.Diagnostics.Process p in claudes)
                {
                    try
                    {
                        // Only kill --print processes (captains), not interactive sessions
                        // We can't easily inspect args, so skip this — the user can kill manually
                    }
                    catch { }
                }
            }
            catch { }

            int errors = 0;

            // Delete database
            if (File.Exists(armadaSettings.DatabasePath))
            {
                try
                {
                    File.Delete(armadaSettings.DatabasePath);
                    AnsiConsole.MarkupLine("[green]Deleted[/] database");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed[/] to delete database: {Markup.Escape(ex.Message)}");
                    errors++;
                }
            }

            // Delete logs directory
            if (Directory.Exists(armadaSettings.LogDirectory))
            {
                try
                {
                    Directory.Delete(armadaSettings.LogDirectory, recursive: true);
                    AnsiConsole.MarkupLine("[green]Deleted[/] logs");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed[/] to delete logs: {Markup.Escape(ex.Message)}");
                    errors++;
                }
            }

            // Delete docks directory (worktrees)
            if (Directory.Exists(armadaSettings.DocksDirectory))
            {
                try
                {
                    ClearReadOnlyAttributes(armadaSettings.DocksDirectory);
                    Directory.Delete(armadaSettings.DocksDirectory, recursive: true);
                    AnsiConsole.MarkupLine("[green]Deleted[/] docks");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed[/] to delete docks: {Markup.Escape(ex.Message)}");
                    errors++;
                }
            }

            // Delete bare repos directory
            if (Directory.Exists(armadaSettings.ReposDirectory))
            {
                try
                {
                    ClearReadOnlyAttributes(armadaSettings.ReposDirectory);
                    Directory.Delete(armadaSettings.ReposDirectory, recursive: true);
                    AnsiConsole.MarkupLine("[green]Deleted[/] bare repos");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed[/] to delete bare repos: {Markup.Escape(ex.Message)}");
                    errors++;
                }
            }

            // Re-create directories
            armadaSettings.InitializeDirectories();

            AnsiConsole.MarkupLine("");
            if (errors == 0)
            {
                AnsiConsole.MarkupLine("[bold green]Reset complete.[/] Armada is back to zero.");
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold yellow]Reset completed with {errors} error(s).[/] Some files may still exist.");
                AnsiConsole.MarkupLine("[dim]If the server is running, stop it first and try again.[/]");
            }

            return errors > 0 ? 1 : 0;
        }

        /// <summary>
        /// Recursively clear read-only attributes on all files in a directory.
        /// Git pack files and index files are often marked read-only on Windows.
        /// </summary>
        private static void ClearReadOnlyAttributes(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    FileAttributes attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    }
                }
                catch { }
            }
        }
    }
}
