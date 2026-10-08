namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Templates;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;

    /// <summary>
    /// The Logs tab: browse the Admiral's logs (its own log, mission and captain sessions, diffs, instructions, final
    /// messages, dock logs) and Harbor's log, open a mission's log by ID, and view any of them. The viewer reads only
    /// the end of a file, can follow it as it grows, filter Admiral and Harbor logs by severity, and find text.
    /// </summary>
    public class LogBrowserView : UserControl
    {
        #region Private-Members

        private const string _HarborCategory = "Harbor";
        private const int _MaxViewerLines = 5000;
        private const int _ListLimit = 500;

        private readonly HarborSession _Session;
        private readonly ComboBox _Category = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _NameFilter = new TextBox { Watermark = "Filter by name" };
        private readonly TextBox _MissionId = new TextBox { Watermark = "msn_... or cpt_..." };
        private readonly ListBox _Files = new ListBox();
        private readonly TextBlock _ListNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private readonly TextBlock _FileTitle = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _FileInfo = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly CheckBox _Follow = new CheckBox { Content = "Follow", IsChecked = true };
        private readonly ComboBox _Severity = new ComboBox { ItemsSource = new string[] { "All levels", "Info and up", "Warn and up", "Error and up" }, SelectedIndex = 0, MinWidth = 130 };
        private readonly TextBox _Find = new TextBox { Watermark = "Find", MinWidth = 160 };
        private readonly TextBox _Viewer;
        private readonly DispatcherTimer _FollowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly List<string> _Lines = new List<string>();
        private string _Partial = String.Empty;
        private string? _CurrentPath = null;
        private long _Offset = 0;
        private bool _Reading = false;
        private bool _StartedMidFile = false;
        private bool _Attached = false;
        private string? _ListedLogDirectory = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public LogBrowserView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));

            List<string> categories = new List<string>();
            foreach (LogCategoryEnum category in Enum.GetValues(typeof(LogCategoryEnum))) categories.Add(CategoryLabel(category));
            categories.Add(_HarborCategory);
            _Category.ItemsSource = categories;
            _Category.SelectedIndex = 0;
            _Category.SelectionChanged += (sender, args) => RefreshList();
            _NameFilter.TextChanged += (sender, args) => RefreshList();

            _Files.ItemTemplate = new FuncDataTemplate<LogFileEntry>((entry, scope) =>
            {
                StackPanel item = new StackPanel { Spacing = 1 };
                if (entry == null) return item;
                item.Children.Add(new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                item.Children.Add(HarborUi.Secondary(new TextBlock
                {
                    Text = DirectoryUsage.FormatBytes(entry.SizeBytes) + "  -  " + HarborUi.LocalTime(entry.LastWriteUtc),
                    FontSize = 11
                }));
                return item;
            });
            _Files.SelectionChanged += (sender, args) =>
            {
                // Re-selecting the open file after a list refresh must not reload it.
                if (_Files.SelectedItem is LogFileEntry entry && !String.Equals(entry.Path, _CurrentPath, StringComparison.Ordinal)) Open(entry.Path);
            };

            Button openMission = HarborUi.Button("Open", () => _ = OpenMissionAsync(), "Open the log of a mission (msn_) or captain (cpt_)");
            _MissionId.KeyDown += (sender, args) =>
            {
                if (args.Key == Avalonia.Input.Key.Enter) _ = OpenMissionAsync();
            };
            Grid missionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            missionRow.Children.Add(_MissionId);
            Grid.SetColumn(openMission, 1);
            missionRow.Children.Add(openMission);

            StackPanel leftTop = new StackPanel { Spacing = 6 };
            leftTop.Children.Add(new TextBlock { Text = "Mission or captain", FontWeight = FontWeight.SemiBold });
            leftTop.Children.Add(missionRow);
            leftTop.Children.Add(new TextBlock { Text = "Browse", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            leftTop.Children.Add(_Category);
            leftTop.Children.Add(_NameFilter);
            leftTop.Children.Add(HarborUi.Secondary(_ListNote));

            StackPanel leftBottom = HarborUi.ButtonRow();
            leftBottom.Margin = new Thickness(0, 6, 0, 0);
            leftBottom.Children.Add(HarborUi.Button("Refresh", RefreshList));
            leftBottom.Children.Add(HarborUi.Button("Open Folder", OpenCategoryFolder));

            DockPanel left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(leftTop, Dock.Top);
            DockPanel.SetDock(leftBottom, Dock.Bottom);
            leftTop.Margin = new Thickness(0, 0, 0, 6);
            left.Children.Add(leftTop);
            left.Children.Add(leftBottom);
            left.Children.Add(new Border
            {
                Child = _Files,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            });
            ((Border)left.Children[2]).Bind(Border.BorderBrushProperty, left.GetResourceObservable("HarborBorderBrush"));

            _Viewer = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily(HarborUi.MonospaceFonts),
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Top
            };

            _Follow.IsCheckedChanged += (sender, args) => UpdateTimer();
            _Severity.SelectionChanged += (sender, args) => Render(true);
            _Find.KeyDown += (sender, args) =>
            {
                if (args.Key == Avalonia.Input.Key.Enter) FindNext();
            };

            StackPanel toolbar = HarborUi.ButtonRow();
            toolbar.Children.Add(_Follow);
            toolbar.Children.Add(_Severity);
            toolbar.Children.Add(_Find);
            toolbar.Children.Add(HarborUi.Button("Next", FindNext, "Find the next match (Enter)"));
            toolbar.Children.Add(HarborUi.Button("Copy", () => _ = CopyAsync()));
            toolbar.Children.Add(HarborUi.Button("Open File", OpenExternally, "Open in the default app for the file"));
            toolbar.Children.Add(HarborUi.Button("Show in Folder", RevealCurrent));

            StackPanel rightTop = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) };
            rightTop.Children.Add(_FileTitle);
            rightTop.Children.Add(HarborUi.Secondary(_FileInfo));
            rightTop.Children.Add(new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

            DockPanel right = new DockPanel();
            DockPanel.SetDock(rightTop, Dock.Top);
            right.Children.Add(rightTop);
            right.Children.Add(_Viewer);

            Grid root = new Grid { ColumnDefinitions = new ColumnDefinitions("280,4,*"), Margin = new Thickness(4, 4, 12, 12) };
            root.Children.Add(left);
            GridSplitter splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            root.Children.Add(splitter);
            Grid.SetColumn(right, 2);
            root.Children.Add(right);
            Content = root;

            _FollowTimer.Tick += (sender, args) => _ = PollAsync();
            AttachedToVisualTree += (sender, args) =>
            {
                _Attached = true;
                _Session.Changed += OnSessionChanged;
                RefreshList();
                UpdateTimer();
            };
            DetachedFromVisualTree += (sender, args) =>
            {
                _Attached = false;
                _Session.Changed -= OnSessionChanged;
                _FollowTimer.Stop();
            };
            ShowNothing("Select a log on the left.");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Put the cursor in the mission box (Logs > Mission Log).
        /// </summary>
        public void FocusMissionLookup()
        {
            Dispatcher.UIThread.Post(() => _MissionId.Focus());
        }

        /// <summary>
        /// Show a category, optionally opening its newest file.
        /// </summary>
        /// <param name="harbor">True for Harbor's log, false for the Admiral's.</param>
        /// <param name="openNewest">Open the newest file.</param>
        public void ShowLog(bool harbor, bool openNewest)
        {
            _Category.SelectedItem = harbor ? _HarborCategory : CategoryLabel(LogCategoryEnum.Admiral);
            RefreshList();
            if (openNewest && _Files.ItemCount > 0) _Files.SelectedIndex = 0;
        }

        #endregion

        #region Private-Methods

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            // Link state changes often (every retry); only a new log location changes the list.
            string? logDirectory = _Session.Admiral?.IsLocal == true ? _Session.Admiral.LogDirectory : null;
            if (String.Equals(logDirectory, _ListedLogDirectory, StringComparison.Ordinal)) return;
            RefreshList();
        }

        private static string CategoryLabel(LogCategoryEnum category)
        {
            switch (category)
            {
                case LogCategoryEnum.Admiral: return "Admiral";
                case LogCategoryEnum.Missions: return "Missions";
                case LogCategoryEnum.Captains: return "Captains";
                case LogCategoryEnum.Diffs: return "Diffs";
                case LogCategoryEnum.Instructions: return "Instructions";
                case LogCategoryEnum.FinalMessages: return "Final messages";
                case LogCategoryEnum.Docks: return "Docks";
                default: return category.ToString();
            }
        }

        private bool TryGetCategory(out LogCategoryEnum category)
        {
            category = LogCategoryEnum.Admiral;
            if (_Category.SelectedItem is not string label || label == _HarborCategory) return false;
            foreach (LogCategoryEnum candidate in Enum.GetValues(typeof(LogCategoryEnum)))
            {
                if (CategoryLabel(candidate) == label)
                {
                    category = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool IsHarborSelected
        {
            get { return _Category.SelectedItem is string label && label == _HarborCategory; }
        }

        private void RefreshList()
        {
            string? filter = _NameFilter.Text;
            List<LogFileEntry> entries;
            _ListedLogDirectory = _Session.Admiral?.IsLocal == true ? _Session.Admiral.LogDirectory : null;

            if (IsHarborSelected)
            {
                entries = ListHarborLogs(filter);
                _ListNote.Text = HarborAppSettings.LogDirectory();
            }
            else
            {
                LocalAdmiralInfo? admiral = _Session.Admiral;
                if (admiral == null || !admiral.IsLocal || !TryGetCategory(out LogCategoryEnum category))
                {
                    _Files.ItemsSource = null;
                    _ListNote.Text = admiral == null ? "Looking for the Armada data directory..." : admiral.Reason;
                    return;
                }

                entries = admiral.Logs.List(category, filter, _ListLimit);
                _ListNote.Text = admiral.Logs.DirectoryFor(category) + (entries.Count >= _ListLimit ? "  (newest " + _ListLimit + "; filter to narrow)" : "");
            }

            string? selected = (_Files.SelectedItem as LogFileEntry)?.Path;
            _Files.ItemsSource = entries;
            if (selected != null)
            {
                LogFileEntry? again = entries.FirstOrDefault(e => e.Path == selected);
                if (again != null) _Files.SelectedItem = again;
            }

            if (entries.Count == 0) _ListNote.Text += "\nNo files.";
        }

        private static List<LogFileEntry> ListHarborLogs(string? filter)
        {
            List<LogFileEntry> entries = new List<LogFileEntry>();
            string directory = HarborAppSettings.LogDirectory();
            if (!Directory.Exists(directory)) return entries;
            foreach (string file in Directory.EnumerateFiles(directory, MainWindow.HarborLogBaseName + "*"))
            {
                string name = Path.GetFileName(file);
                if (!String.IsNullOrWhiteSpace(filter) && name.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                FileInfo info = new FileInfo(file);
                entries.Add(new LogFileEntry { Path = file, Name = name, SizeBytes = info.Length, LastWriteUtc = info.LastWriteTimeUtc });
            }

            entries.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
            return entries;
        }

        private async Task OpenMissionAsync()
        {
            string id = (_MissionId.Text ?? String.Empty).Trim();
            LocalAdmiralInfo? admiral = _Session.Admiral;
            if (id.Length == 0 || admiral == null || !admiral.IsLocal) return;

            ArmadaLogPaths logs = admiral.Logs;
            string? path = null;
            if (id.StartsWith("cpt_", StringComparison.OrdinalIgnoreCase))
            {
                path = logs.ResolveCaptainLog(id);
            }
            else
            {
                path = logs.ResolveMissionLog(id);
                if (path == null)
                {
                    // No session log of its own yet: follow the captain running it, which needs the mission record.
                    using (ArmadaClient? client = _Session.CreateClient())
                    {
                        if (client != null)
                        {
                            try
                            {
                                Armada.Core.Models.Mission? mission = await client.GetMissionAsync(id).ConfigureAwait(true);
                                if (mission != null) path = logs.ResolveMissionLog(mission.Id, mission.CaptainId);
                            }
                            catch (ArmadaApiException)
                            {
                                // Unknown mission or no access: report below.
                            }
                        }
                    }
                }
            }

            if (path == null)
            {
                ShowNothing("No log found for " + id + ".");
                return;
            }

            _Files.SelectedItem = null;
            Open(path);
        }

        private void Open(string path)
        {
            _CurrentPath = path;
            _Offset = 0;
            _Lines.Clear();
            _Partial = String.Empty;
            _FileTitle.Text = Path.GetFileName(path);
            _FileInfo.Text = path;
            _Viewer.Text = "Loading...";
            _ = LoadAsync(path);
        }

        private async Task LoadAsync(string path)
        {
            _Reading = true;
            try
            {
                LogReadResult result = await Task.Run(() => LogTailReader.ReadTail(path)).ConfigureAwait(true);
                if (!String.Equals(path, _CurrentPath, StringComparison.Ordinal)) return;
                _StartedMidFile = result.StartedMidFile;
                Apply(result, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (String.Equals(path, _CurrentPath, StringComparison.Ordinal)) ShowNothing("Could not read " + path + ": " + ex.Message);
            }
            finally
            {
                _Reading = false;
            }

            UpdateTimer();
        }

        private async Task PollAsync()
        {
            string? path = _CurrentPath;
            if (path == null || _Reading) return;
            _Reading = true;
            try
            {
                long offset = _Offset;
                LogReadResult result = await Task.Run(() => LogTailReader.ReadFrom(path, offset)).ConfigureAwait(true);
                if (!String.Equals(path, _CurrentPath, StringComparison.Ordinal)) return;
                if (result.WasReset)
                {
                    _Lines.Clear();
                    _StartedMidFile = result.StartedMidFile;
                    Apply(result, true);
                }
                else if (result.Text.Length > 0 || !String.Equals(result.PartialLine, _Partial, StringComparison.Ordinal))
                {
                    if (result.StartedMidFile) _StartedMidFile = true;
                    Apply(result, false);
                }
            }
            catch (FileNotFoundException)
            {
                // Rotated away; keep what is shown.
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _FileInfo.Text = path + "  (read failed: " + ex.Message + ")";
            }
            finally
            {
                _Reading = false;
            }
        }

        private void Apply(LogReadResult result, bool replace)
        {
            if (replace) _Lines.Clear();
            if (result.Text.Length > 0)
            {
                string[] lines = result.Text.Split('\n');
                // Text ends with a newline, so the last element is empty.
                for (int i = 0; i < lines.Length - 1; i++) _Lines.Add(lines[i].TrimEnd('\r'));
            }

            if (_Lines.Count > _MaxViewerLines)
            {
                _Lines.RemoveRange(0, _Lines.Count - _MaxViewerLines);
                _StartedMidFile = true;
            }

            _Partial = result.PartialLine;
            _Offset = result.EndOffset;
            _FileInfo.Text = (_CurrentPath ?? "") + "  -  " + DirectoryUsage.FormatBytes(result.FileLength)
                + (_StartedMidFile ? "  -  showing the end" : "");
            Render(replace);
        }

        private void Render(bool scrollToEnd)
        {
            if (_CurrentPath == null) return;
            bool atEnd = scrollToEnd || _Viewer.CaretIndex >= (_Viewer.Text?.Length ?? 0) - 1;

            List<string> visible = _Lines;
            int minimum = _Severity.SelectedIndex <= 0 ? 0 : _Severity.SelectedIndex;
            if (minimum > 0) visible = LogSeverityFilter.Filter(_Lines, minimum);

            string text = String.Join("\n", visible);
            if (_Partial.Length > 0) text += (text.Length > 0 ? "\n" : "") + _Partial;
            if (text.Length == 0) text = minimum > 0 && _Lines.Count > 0 ? "(no lines at this level)" : "(empty)";
            _Viewer.Text = text;
            if (atEnd || (_Follow.IsChecked == true))
            {
                // After layout has measured the new text; setting the caret earlier can leave the view at the top.
                Dispatcher.UIThread.Post(() =>
                {
                    int length = _Viewer.Text?.Length ?? 0;
                    _Viewer.CaretIndex = length;
                    _Viewer.ScrollToLine(Math.Max(0, _Viewer.GetLineCount() - 1));
                }, DispatcherPriority.Background);
            }
        }

        private void ShowNothing(string message)
        {
            _CurrentPath = null;
            _Lines.Clear();
            _Partial = String.Empty;
            _FileTitle.Text = "No log selected";
            _FileInfo.Text = String.Empty;
            _Viewer.Text = message;
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if (_Follow.IsChecked == true && _CurrentPath != null && _Attached) _FollowTimer.Start();
            else _FollowTimer.Stop();
        }

        private void FindNext()
        {
            string needle = _Find.Text ?? String.Empty;
            string haystack = _Viewer.Text ?? String.Empty;
            if (needle.Length == 0 || haystack.Length == 0) return;

            // Stop following so the match stays in view.
            _Follow.IsChecked = false;
            int from = Math.Max(_Viewer.SelectionEnd, _Viewer.SelectionStart);
            int index = haystack.IndexOf(needle, Math.Min(from, haystack.Length), StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                _FileInfo.Text = "\"" + needle + "\" not found.";
                return;
            }

            _Viewer.Focus();
            _Viewer.CaretIndex = index;
            _Viewer.SelectionStart = index;
            _Viewer.SelectionEnd = index + needle.Length;
        }

        private async Task CopyAsync()
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard == null) return;
            string selected = _Viewer.SelectedText;
            await top.Clipboard.SetTextAsync(String.IsNullOrEmpty(selected) ? _Viewer.Text ?? String.Empty : selected).ConfigureAwait(true);
        }

        private void OpenExternally()
        {
            if (_CurrentPath == null) return;
            if (!PlatformShell.Open(_CurrentPath, out string? error)) _FileInfo.Text = "Could not open: " + error;
        }

        private void RevealCurrent()
        {
            if (_CurrentPath == null) return;
            if (!PlatformShell.Reveal(_CurrentPath, out string? error)) _FileInfo.Text = "Could not show: " + error;
        }

        private void OpenCategoryFolder()
        {
            string? directory = null;
            if (IsHarborSelected)
            {
                directory = HarborAppSettings.LogDirectory();
                Directory.CreateDirectory(directory);
            }
            else if (_Session.Admiral != null && _Session.Admiral.IsLocal && TryGetCategory(out LogCategoryEnum category))
            {
                directory = _Session.Admiral.Logs.DirectoryFor(category);
            }

            if (directory == null || !Directory.Exists(directory)) return;
            if (!PlatformShell.Open(directory, out string? error)) _ListNote.Text = "Could not open: " + error;
        }

        #endregion
    }
}
