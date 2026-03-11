namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Text.Json;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia.Threading;
    using Armada.Desktop.Models;
    using Armada.Desktop.Services;

    /// <summary>
    /// View model for the mission diff viewer window.
    /// </summary>
    public class DiffViewerViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private string _MissionId;
        private string _MissionTitle;
        private bool _IsLoading;
        private string _ErrorMessage = "";
        private string _DiffContent = "";
        private string _ParsedDiffContent = "";
        private bool _HasParsedDiff;
        private ObservableCollection<DiffFile> _ParsedFiles = new ObservableCollection<DiffFile>();
        private DiffFile? _SelectedFile;
        private ObservableCollection<DiffLine> _CurrentLines = new ObservableCollection<DiffLine>();
        private bool _HasParsedFiles;

        #endregion

        #region Public-Members

        /// <summary>Mission title for display.</summary>
        public string MissionTitle
        {
            get => _MissionTitle;
            set => this.RaiseAndSetIfChanged(ref _MissionTitle, value);
        }

        /// <summary>Raw/JSON content for display.</summary>
        public string DiffContent
        {
            get => _DiffContent;
            set => this.RaiseAndSetIfChanged(ref _DiffContent, value);
        }

        /// <summary>Expanded diff output extracted from JSON Diff property.</summary>
        public string ParsedDiffContent
        {
            get => _ParsedDiffContent;
            set => this.RaiseAndSetIfChanged(ref _ParsedDiffContent, value);
        }

        /// <summary>Whether a parsed diff section is available.</summary>
        public bool HasParsedDiff
        {
            get => _HasParsedDiff;
            set => this.RaiseAndSetIfChanged(ref _HasParsedDiff, value);
        }

        /// <summary>Whether the diff is loading.</summary>
        public bool IsLoading
        {
            get => _IsLoading;
            set => this.RaiseAndSetIfChanged(ref _IsLoading, value);
        }

        /// <summary>Error message if diff is unavailable.</summary>
        public string ErrorMessage
        {
            get => _ErrorMessage;
            set => this.RaiseAndSetIfChanged(ref _ErrorMessage, value);
        }

        /// <summary>Parsed diff files for the file navigator.</summary>
        public ObservableCollection<DiffFile> ParsedFiles
        {
            get => _ParsedFiles;
            set => this.RaiseAndSetIfChanged(ref _ParsedFiles, value);
        }

        /// <summary>Currently selected file in the file navigator.</summary>
        public DiffFile? SelectedFile
        {
            get => _SelectedFile;
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedFile, value);
                UpdateCurrentLines();
            }
        }

        /// <summary>Lines for the currently selected file's diff display.</summary>
        public ObservableCollection<DiffLine> CurrentLines
        {
            get => _CurrentLines;
            set => this.RaiseAndSetIfChanged(ref _CurrentLines, value);
        }

        /// <summary>Whether structured parsed files are available for two-pane display.</summary>
        public bool HasParsedFiles
        {
            get => _HasParsedFiles;
            set => this.RaiseAndSetIfChanged(ref _HasParsedFiles, value);
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connection">Connection service.</param>
        /// <param name="missionId">Mission ID to diff.</param>
        /// <param name="missionTitle">Mission title for display.</param>
        public DiffViewerViewModel(ArmadaConnectionService connection, string missionId, string missionTitle)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _MissionId = missionId ?? throw new ArgumentNullException(nameof(missionId));
            _MissionTitle = missionTitle ?? missionId;

            _ = LoadDiffAsync();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load or reload the diff.
        /// </summary>
        public async Task LoadDiffAsync()
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = true;
                ErrorMessage = "";
                DiffContent = "";
                ParsedDiffContent = "";
                HasParsedDiff = false;
                HasParsedFiles = false;
                ParsedFiles.Clear();
                CurrentLines.Clear();
                SelectedFile = null;
            });

            try
            {
                string? diff = await _Connection.GetApiClient().GetMissionDiffAsync(_MissionId).ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    if (string.IsNullOrEmpty(diff))
                    {
                        ErrorMessage = "No diff available. The worktree may have been reclaimed after mission completion.";
                        return;
                    }

                    // Try to parse as JSON
                    string trimmed = diff.TrimStart();
                    if ((trimmed.StartsWith("{") || trimmed.StartsWith("[")) && !trimmed.StartsWith("---") && !trimmed.StartsWith("+++"))
                    {
                        try
                        {
                            JsonElement parsed = JsonSerializer.Deserialize<JsonElement>(diff);
                            string pretty = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
                            DiffContent = pretty;

                            // Extract the Diff property if present and expand escaped newlines
                            if (parsed.ValueKind == JsonValueKind.Object && parsed.TryGetProperty("Diff", out JsonElement diffProp) && diffProp.ValueKind == JsonValueKind.String)
                            {
                                string diffValue = diffProp.GetString() ?? "";
                                if (!string.IsNullOrEmpty(diffValue))
                                {
                                    ParsedDiffContent = diffValue;
                                    HasParsedDiff = true;
                                    ParseStructuredDiff(diffValue);
                                }
                            }

                            return;
                        }
                        catch
                        {
                            // Not valid JSON, fall through to raw display
                        }
                    }

                    // Treat as raw diff text
                    DiffContent = diff;

                    // Try to parse as unified diff directly
                    if (diff.Contains("diff --git") || diff.Contains("---"))
                    {
                        ParsedDiffContent = diff;
                        HasParsedDiff = true;
                        ParseStructuredDiff(diff);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ErrorMessage = "Error loading diff: " + ex.Message);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Parse the diff text into structured file/hunk/line models.
        /// </summary>
        private void ParseStructuredDiff(string diffText)
        {
            List<DiffFile> files = UnifiedDiffParser.Parse(diffText);
            if (files.Count > 0)
            {
                ParsedFiles = new ObservableCollection<DiffFile>(files);
                HasParsedFiles = true;

                // Auto-select first file
                SelectedFile = files[0];
            }
        }

        /// <summary>
        /// Update the CurrentLines collection based on the selected file.
        /// </summary>
        private void UpdateCurrentLines()
        {
            CurrentLines.Clear();
            if (_SelectedFile == null)
                return;

            foreach (DiffHunk hunk in _SelectedFile.Hunks)
            {
                foreach (DiffLine line in hunk.Lines)
                {
                    CurrentLines.Add(line);
                }
            }
        }

        #endregion
    }
}
