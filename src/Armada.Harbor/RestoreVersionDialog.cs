namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Templates;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// Lists the kept previous versions of a settings file (newest first, with when each was replaced) and returns the
    /// one the operator picks. The caller confirms and restores it.
    /// </summary>
    public class RestoreVersionDialog : Window
    {
        #region Private-Members

        private readonly ListBox _List = new ListBox { MinHeight = 160, MaxHeight = 320 };
        private readonly Button _Restore;
        private SettingsBackupEntry? _Chosen = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parameterless constructor for the Avalonia runtime loader / previewer.
        /// </summary>
        public RestoreVersionDialog() : this("settings.json", new List<SettingsBackupEntry>())
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="filePath">The settings file.</param>
        /// <param name="backups">Its backups, newest first.</param>
        public RestoreVersionDialog(string filePath, List<SettingsBackupEntry> backups)
        {
            if (backups == null) throw new ArgumentNullException(nameof(backups));
            Title = "Restore previous version";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (Application.Current is App app && app.WindowIcon != null) Icon = app.WindowIcon;

            _List.ItemTemplate = new FuncDataTemplate<SettingsBackupEntry>((entry, scope) =>
            {
                StackPanel item = new StackPanel { Spacing = 1, Margin = new Thickness(0, 2) };
                if (entry == null) return item;
                item.Children.Add(new TextBlock { Text = "Saved before " + HarborUi.LocalTime(entry.TakenUtc), FontWeight = FontWeight.SemiBold });
                item.Children.Add(HarborUi.Secondary(new TextBlock
                {
                    Text = DescribeAge(DateTime.UtcNow - entry.TakenUtc) + "  -  " + DirectoryUsage.FormatBytes(entry.SizeBytes) + "  -  " + entry.Name,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }));
                return item;
            });
            _List.ItemsSource = backups;
            _List.SelectionChanged += (sender, args) => _Restore!.IsEnabled = _List.SelectedItem is SettingsBackupEntry;
            _List.DoubleTapped += (sender, args) => Choose();

            Button cancel = new Button { Content = "Cancel", IsCancel = true };
            cancel.Click += (sender, args) => Close();
            _Restore = new Button { Content = "Restore...", IsDefault = true, IsEnabled = false };
            _Restore.Classes.Add("accent");
            _Restore.Click += (sender, args) => Choose();
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(_Restore);

            StackPanel root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            TextBlock heading = new TextBlock { Text = "Restore a previous version of " + Path.GetFileName(filePath) };
            heading.Classes.Add("title");
            root.Children.Add(heading);
            root.Children.Add(HarborUi.Note(backups.Count == 0
                ? "There are no previous versions yet. Each save keeps the version it replaces."
                : "Each save keeps the version it replaces. Restoring keeps the current version too, so you can switch back."));
            if (backups.Count > 0)
            {
                Border listBorder = new Border { Child = _List, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
                listBorder.Bind(Border.BorderBrushProperty, listBorder.GetResourceObservable("HarborBorderBrush"));
                root.Children.Add(listBorder);
                _List.SelectedIndex = 0;
            }

            root.Children.Add(buttons);
            Content = root;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the backups of a file and return the one chosen.
        /// </summary>
        /// <param name="owner">Owner window, or null.</param>
        /// <param name="filePath">The settings file.</param>
        /// <returns>The chosen backup, or null when cancelled.</returns>
        public static async Task<SettingsBackupEntry?> ChooseAsync(Window? owner, string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            RestoreVersionDialog dialog = new RestoreVersionDialog(filePath, SettingsFileStore.ListBackupEntries(filePath));
            if (owner != null && owner.IsVisible)
            {
                await dialog.ShowDialog(owner).ConfigureAwait(true);
            }
            else
            {
                TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
                dialog.Closed += (sender, args) => closed.TrySetResult(true);
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Show();
                await closed.Task.ConfigureAwait(true);
            }

            return dialog._Chosen;
        }

        /// <summary>
        /// "just now", "5 minutes ago", "3 hours ago", "2 days ago".
        /// </summary>
        /// <param name="age">Age.</param>
        /// <returns>Text.</returns>
        public static string DescribeAge(TimeSpan age)
        {
            if (age < TimeSpan.FromMinutes(1)) return "just now";
            if (age < TimeSpan.FromHours(1)) return Plural((int)age.TotalMinutes, "minute") + " ago";
            if (age < TimeSpan.FromDays(1)) return Plural((int)age.TotalHours, "hour") + " ago";
            return Plural((int)age.TotalDays, "day") + " ago";
        }

        #endregion

        #region Private-Methods

        private void Choose()
        {
            if (_List.SelectedItem is not SettingsBackupEntry entry) return;
            _Chosen = entry;
            Close();
        }

        private static string Plural(int count, string unit)
        {
            return count + " " + unit + (count == 1 ? "" : "s");
        }

        #endregion
    }
}
