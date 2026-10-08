namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// The Backups tab, read-only: the Admiral's database backups (taken before each schema migration, and by backup
    /// commands) and the settings-file backups Harbor's editors keep. Nothing here restores or deletes.
    /// </summary>
    public class BackupsView : UserControl
    {
        #region Private-Members

        private readonly HarborSession _Session;
        private readonly StackPanel _Body = new StackPanel { Spacing = 8 };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public BackupsView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));

            StackPanel root = new StackPanel { Spacing = 8, Margin = new Thickness(4, 4, 12, 12) };
            Grid header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(HarborUi.Heading("Backups"));
            Button refresh = HarborUi.Button("Refresh", () => _ = RefreshAsync());
            Grid.SetColumn(refresh, 1);
            header.Children.Add(refresh);
            root.Children.Add(header);
            root.Children.Add(HarborUi.Note("Read-only: Harbor does not restore or delete database backups. A settings backup can be loaded into its editor on the Armada (File) or TUI tab and saved to restore it."));
            root.Children.Add(_Body);
            Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

            AttachedToVisualTree += (sender, args) => _ = RefreshAsync();
        }

        #endregion

        #region Private-Methods

        private async Task RefreshAsync()
        {
            _Body.Children.Clear();
            LocalAdmiralInfo? admiral = _Session.Admiral;

            if (admiral == null || !admiral.IsLocal)
            {
                _Body.Children.Add(HarborUi.Note(admiral == null ? "Looking for the Armada data directory..." : admiral.Reason));
            }
            else
            {
                _Body.Children.Add(SectionHeader("Database backups", admiral.BackupsDirectory));
                string backupsDir = admiral.BackupsDirectory;
                List<DirectoryUsageEntry> databases = await Task.Run(() => DirectoryUsage.Measure(backupsDir)).ConfigureAwait(true);
                databases.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
                _Body.Children.Add(databases.Count == 0 ? HarborUi.Note("None.") : Table(databases));

                List<DirectoryUsageEntry> settings = new List<DirectoryUsageEntry>();
                AddFileBackups(settings, admiral.SettingsFile);
                AddFileBackups(settings, admiral.TuiPreferencesFile);
                _Body.Children.Add(SectionHeader("Armada settings backups", admiral.DataDirectory));
                _Body.Children.Add(settings.Count == 0 ? HarborUi.Note("None.") : Table(settings));
            }

            List<DirectoryUsageEntry> harbor = new List<DirectoryUsageEntry>();
            AddFileBackups(harbor, HarborAppSettings.DefaultPath());
            _Body.Children.Add(SectionHeader("Harbor settings backups", HarborAppSettings.SettingsDirectory()));
            _Body.Children.Add(harbor.Count == 0 ? HarborUi.Note("None.") : Table(harbor));
        }

        private static void AddFileBackups(List<DirectoryUsageEntry> entries, string file)
        {
            foreach (string backup in SettingsFileStore.ListBackups(file))
            {
                try
                {
                    FileInfo info = new FileInfo(backup);
                    entries.Add(new DirectoryUsageEntry
                    {
                        Name = info.Name,
                        Path = info.FullName,
                        SizeBytes = info.Length,
                        FileCount = 1,
                        LastWriteUtc = info.LastWriteTimeUtc
                    });
                }
                catch (IOException)
                {
                }
            }
        }

        private Control SectionHeader(string title, string directory)
        {
            Grid header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            StackPanel text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
            text.Children.Add(HarborUi.Note(directory));
            header.Children.Add(text);
            Button open = HarborUi.Button("Open Folder", () => OpenFolder(directory));
            open.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(open, 1);
            header.Children.Add(open);
            return header;
        }

        private Control Table(List<DirectoryUsageEntry> entries)
        {
            Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), RowSpacing = 4, ColumnSpacing = 16 };
            AddRow(grid, null, "Name", "Size", "Created", true);
            foreach (DirectoryUsageEntry entry in entries)
                AddRow(grid, entry, entry.Name, DirectoryUsage.FormatBytes(entry.SizeBytes), HarborUi.LocalTime(entry.LastWriteUtc), false);
            return grid;
        }

        private void AddRow(Grid grid, DirectoryUsageEntry? entry, string name, string size, string created, bool header)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            string[] cells = new string[] { name, size, created };
            for (int i = 0; i < cells.Length; i++)
            {
                TextBlock cell = new TextBlock { Text = cells[i], VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                if (header) cell.FontWeight = FontWeight.SemiBold;
                else if (i > 0) HarborUi.Secondary(cell);
                if (i == 1) cell.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }

            if (entry != null)
            {
                Button reveal = HarborUi.Button("Show", () => Reveal(entry.Path), "Show in the file manager");
                reveal.Padding = new Thickness(8, 2);
                Grid.SetRow(reveal, row);
                Grid.SetColumn(reveal, 3);
                grid.Children.Add(reveal);
            }
        }

        private void OpenFolder(string directory)
        {
            if (!Directory.Exists(directory)) return;
            if (!PlatformShell.Open(directory, out string? error))
                _ = HarborDialog.ShowMessageAsync(TopLevel.GetTopLevel(this) as Window, "Could not open the folder", error ?? "Unknown error");
        }

        private void Reveal(string path)
        {
            if (!PlatformShell.Reveal(path, out string? error))
                _ = HarborDialog.ShowMessageAsync(TopLevel.GetTopLevel(this) as Window, "Could not show the backup", error ?? "Unknown error");
        }

        #endregion
    }
}
