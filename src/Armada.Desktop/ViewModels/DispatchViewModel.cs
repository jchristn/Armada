namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia.Threading;
    using Armada.Core.Models;
    using Armada.Desktop.Services;

    /// <summary>
    /// Dispatch view model for creating new voyages.
    /// </summary>
    public class DispatchViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private string _Prompt = "";
        private string _VoyageTitle = "";
        private string? _SelectedVesselId;
        private string _StatusMessage = "";
        private bool _IsDispatching;
        private bool _ShowAdvanced;

        #endregion

        #region Public-Members

        /// <summary>Quick dispatch prompt.</summary>
        public string Prompt
        {
            get => _Prompt;
            set
            {
                this.RaiseAndSetIfChanged(ref _Prompt, value);
                UpdateParsedTasks();
            }
        }

        /// <summary>Voyage title (advanced mode).</summary>
        public string VoyageTitle
        {
            get => _VoyageTitle;
            set => this.RaiseAndSetIfChanged(ref _VoyageTitle, value);
        }

        /// <summary>Selected vessel ID.</summary>
        public string? SelectedVesselId
        {
            get => _SelectedVesselId;
            set => this.RaiseAndSetIfChanged(ref _SelectedVesselId, value);
        }

        /// <summary>Status message after dispatch.</summary>
        public string StatusMessage
        {
            get => _StatusMessage;
            set => this.RaiseAndSetIfChanged(ref _StatusMessage, value);
        }

        /// <summary>Whether a dispatch is in progress.</summary>
        public bool IsDispatching
        {
            get => _IsDispatching;
            set => this.RaiseAndSetIfChanged(ref _IsDispatching, value);
        }

        /// <summary>Whether to show advanced mode.</summary>
        public bool ShowAdvanced
        {
            get => _ShowAdvanced;
            set => this.RaiseAndSetIfChanged(ref _ShowAdvanced, value);
        }

        /// <summary>Available vessels.</summary>
        public ObservableCollection<Vessel> Vessels { get; } = new ObservableCollection<Vessel>();

        /// <summary>Parsed tasks from the prompt.</summary>
        public ObservableCollection<string> ParsedTasks { get; } = new ObservableCollection<string>();

        /// <summary>Advanced mode mission entries.</summary>
        public ObservableCollection<MissionEntry> MissionEntries { get; } = new ObservableCollection<MissionEntry>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DispatchViewModel(ArmadaConnectionService connection)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _Connection.DataRefreshed += OnDataRefreshed;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispatch the quick prompt.
        /// </summary>
        public async Task DispatchAsync()
        {
            if (string.IsNullOrWhiteSpace(Prompt)) return;
            if (string.IsNullOrEmpty(SelectedVesselId)) return;

            IsDispatching = true;
            StatusMessage = "";

            try
            {
                List<string> tasks = DetectMultipleTasks(Prompt);

                List<Mission> missions = new List<Mission>();
                foreach (string task in tasks)
                {
                    missions.Add(new Mission(task, task) { VesselId = SelectedVesselId });
                }

                Voyage voyage = new Voyage(tasks.Count > 1 ? Prompt : tasks[0]);
                Voyage? created = await _Connection.GetApiClient().CreateVoyageAsync(voyage).ConfigureAwait(false);

                if (created != null)
                {
                    foreach (Mission m in missions)
                    {
                        m.VoyageId = created.Id;
                        await _Connection.GetApiClient().CreateMissionAsync(m).ConfigureAwait(false);
                    }

                    await _Connection.RefreshAsync().ConfigureAwait(false);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"Dispatched! Voyage {created.Id} with {missions.Count} mission(s).";
                        Prompt = "";
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = "Error: " + ex.Message;
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsDispatching = false);
            }
        }

        /// <summary>
        /// Dispatch from advanced voyage builder.
        /// </summary>
        public async Task DispatchAdvancedAsync()
        {
            if (string.IsNullOrWhiteSpace(VoyageTitle)) return;
            if (MissionEntries.Count == 0) return;
            if (string.IsNullOrEmpty(SelectedVesselId)) return;

            IsDispatching = true;
            StatusMessage = "";

            try
            {
                Voyage voyage = new Voyage(VoyageTitle);
                Voyage? created = await _Connection.GetApiClient().CreateVoyageAsync(voyage).ConfigureAwait(false);

                if (created != null)
                {
                    int count = 0;
                    foreach (MissionEntry entry in MissionEntries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Title)) continue;
                        Mission m = new Mission(entry.Title, entry.Description)
                        {
                            VesselId = SelectedVesselId,
                            VoyageId = created.Id
                        };
                        await _Connection.GetApiClient().CreateMissionAsync(m).ConfigureAwait(false);
                        count++;
                    }

                    await _Connection.RefreshAsync().ConfigureAwait(false);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = $"Launched! Voyage \"{VoyageTitle}\" with {count} mission(s).";
                        VoyageTitle = "";
                        MissionEntries.Clear();
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = "Error: " + ex.Message;
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsDispatching = false);
            }
        }

        /// <summary>
        /// Add a mission entry in advanced mode.
        /// </summary>
        public void AddMissionEntry()
        {
            MissionEntries.Add(new MissionEntry());
        }

        /// <summary>
        /// Remove a mission entry.
        /// </summary>
        public void RemoveMissionEntry(MissionEntry entry)
        {
            MissionEntries.Remove(entry);
        }

        #endregion

        #region Private-Methods

        private void OnDataRefreshed(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                List<Vessel> current = _Connection.Vessels;
                if (current.Count != Vessels.Count || !current.Select(v => v.Id).SequenceEqual(Vessels.Select(v => v.Id)))
                {
                    Vessels.Clear();
                    foreach (Vessel v in current) Vessels.Add(v);

                    if (SelectedVesselId == null && Vessels.Count > 0)
                        SelectedVesselId = Vessels[0].Id;
                }
            });
        }

        private void UpdateParsedTasks()
        {
            ParsedTasks.Clear();
            if (string.IsNullOrWhiteSpace(Prompt)) return;
            foreach (string task in DetectMultipleTasks(Prompt))
            {
                ParsedTasks.Add(task);
            }
        }

        private List<string> DetectMultipleTasks(string prompt)
        {
            List<string> tasks = new List<string>();

            // Numbered list
            MatchCollection numberedMatches = Regex.Matches(prompt, @"(?:^|\s)(\d+)\.\s+(.+?)(?=(?:\s+\d+\.\s)|$)", RegexOptions.Singleline);
            if (numberedMatches.Count >= 2)
            {
                foreach (Match m in numberedMatches)
                {
                    string task = m.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(task)) tasks.Add(task);
                }
                if (tasks.Count >= 2) return tasks;
                tasks.Clear();
            }

            // Semicolons
            string[] semiParts = prompt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (semiParts.Length >= 2)
            {
                foreach (string part in semiParts)
                {
                    if (!string.IsNullOrEmpty(part)) tasks.Add(part);
                }
                return tasks;
            }

            tasks.Add(prompt);
            return tasks;
        }

        #endregion
    }

    /// <summary>
    /// Mission entry for the advanced voyage builder.
    /// </summary>
    public class MissionEntry : ViewModelBase
    {
        private string _Title = "";
        private string _Description = "";

        /// <summary>Mission title.</summary>
        public string Title
        {
            get => _Title;
            set => this.RaiseAndSetIfChanged(ref _Title, value);
        }

        /// <summary>Mission description.</summary>
        public string Description
        {
            get => _Description;
            set => this.RaiseAndSetIfChanged(ref _Description, value);
        }
    }
}
