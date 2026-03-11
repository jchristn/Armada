namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia.Data.Converters;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Armada.Desktop.Services;

    /// <summary>
    /// System health check view model (doctor).
    /// </summary>
    public class DoctorViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private bool _IsRunning;

        #endregion

        #region Public-Members

        /// <summary>Health check results.</summary>
        public ObservableCollection<DoctorCheckResult> Results { get; } = new ObservableCollection<DoctorCheckResult>();

        /// <summary>Whether checks are currently running.</summary>
        public bool IsRunning
        {
            get => _IsRunning;
            set => this.RaiseAndSetIfChanged(ref _IsRunning, value);
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DoctorViewModel(ArmadaConnectionService connection)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _ = RunChecksAsync();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all health checks.
        /// </summary>
        public async Task RunChecksAsync()
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = true;
                Results.Clear();
            });

            await Task.Run(async () =>
            {
                // 1. Settings file
                CheckSettings();

                // 2. Git available
                CheckGit();

                // 3. Database
                CheckDatabase();

                // 4. Admiral server
                await CheckAdmiralAsync().ConfigureAwait(false);

                // 5. Stalled captains
                CheckStalledCaptains();

                // 6. Failed missions
                CheckFailedMissions();

                // 7. Agent runtimes
                CheckAgentRuntimes();
            }).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() => IsRunning = false);
        }

        #endregion

        #region Private-Methods

        private void AddResult(string name, DoctorStatus status, string message)
        {
            Dispatcher.UIThread.Post(() => Results.Add(new DoctorCheckResult
            {
                Name = name,
                Status = status,
                Message = message
            }));
        }

        private void CheckSettings()
        {
            try
            {
                ArmadaSettings settings = _Connection.GetSettings();
                if (settings != null)
                    AddResult("Settings", DoctorStatus.Pass, "Settings loaded from " + ArmadaSettings.DefaultSettingsPath);
                else
                    AddResult("Settings", DoctorStatus.Fail, "Settings file not found");
            }
            catch (Exception ex)
            {
                AddResult("Settings", DoctorStatus.Fail, "Error loading settings: " + ex.Message);
            }
        }

        private void CheckGit()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("git", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process? proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd().Trim();
                        proc.WaitForExit(5000);
                        AddResult("Git", DoctorStatus.Pass, output);
                    }
                    else
                    {
                        AddResult("Git", DoctorStatus.Fail, "Could not start git process");
                    }
                }
            }
            catch
            {
                AddResult("Git", DoctorStatus.Fail, "Git not found on PATH. Install git to use Armada.");
            }
        }

        private void CheckDatabase()
        {
            try
            {
                ArmadaSettings settings = _Connection.GetSettings();
                if (File.Exists(settings.DatabasePath))
                {
                    FileInfo fi = new FileInfo(settings.DatabasePath);
                    AddResult("Database", DoctorStatus.Pass, $"Database exists ({fi.Length / 1024} KB) at {settings.DatabasePath}");
                }
                else
                {
                    AddResult("Database", DoctorStatus.Warn, "Database not found at " + settings.DatabasePath + ". It will be created on first use.");
                }
            }
            catch (Exception ex)
            {
                AddResult("Database", DoctorStatus.Fail, "Error checking database: " + ex.Message);
            }
        }

        private async Task CheckAdmiralAsync()
        {
            try
            {
                bool healthy = await _Connection.GetApiClient().HealthCheckAsync().ConfigureAwait(false);
                if (healthy)
                    AddResult("Admiral Server", DoctorStatus.Pass, "Server is healthy at " + _Connection.GetBaseUrl());
                else
                    AddResult("Admiral Server", DoctorStatus.Fail, "Server health check failed");
            }
            catch
            {
                AddResult("Admiral Server", DoctorStatus.Fail, "Cannot reach Admiral at " + _Connection.GetBaseUrl());
            }
        }

        private void CheckStalledCaptains()
        {
            int stalledCount = _Connection.Captains.Count(c => c.State == CaptainStateEnum.Stalled);
            if (stalledCount == 0)
                AddResult("Stalled Captains", DoctorStatus.Pass, "No stalled captains");
            else
                AddResult("Stalled Captains", DoctorStatus.Warn, $"{stalledCount} captain(s) are stalled. Check Fleet > Captains.");
        }

        private void CheckFailedMissions()
        {
            int failedCount = _Connection.Missions.Count(m => m.Status == MissionStatusEnum.Failed);
            if (failedCount == 0)
                AddResult("Failed Missions", DoctorStatus.Pass, "No failed missions");
            else
                AddResult("Failed Missions", DoctorStatus.Warn, $"{failedCount} mission(s) have failed. Check Missions page to retry.");
        }

        private void CheckAgentRuntimes()
        {
            try
            {
                // Check Claude Code
                CheckRuntime("claude", "Claude Code");
            }
            catch { }

            try
            {
                // Check Codex
                CheckRuntime("codex", "Codex");
            }
            catch { }
        }

        private void CheckRuntime(string command, string displayName)
        {
            try
            {
                // Use "where" on Windows / "which" on Unix to locate the executable
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows);
                string whichCmd = isWindows ? "where" : "which";

                ProcessStartInfo psi = new ProcessStartInfo(whichCmd, command)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process? proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd().Trim();
                        proc.WaitForExit(5000);
                        if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                        {
                            string path = output.Split('\n')[0].Trim();
                            AddResult(displayName, DoctorStatus.Pass, displayName + " found at " + path);
                        }
                        else
                        {
                            AddResult(displayName, DoctorStatus.Warn, displayName + " not found on PATH (optional)");
                        }
                    }
                    else
                    {
                        AddResult(displayName, DoctorStatus.Warn, displayName + " not found (optional)");
                    }
                }
            }
            catch
            {
                AddResult(displayName, DoctorStatus.Warn, displayName + " not found (optional)");
            }
        }

        #endregion
    }

    /// <summary>
    /// A single doctor check result.
    /// </summary>
    public class DoctorCheckResult
    {
        /// <summary>Check name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Status.</summary>
        public DoctorStatus Status { get; set; } = DoctorStatus.Pass;

        /// <summary>Status display text.</summary>
        public string StatusText => Status switch
        {
            DoctorStatus.Pass => "PASS",
            DoctorStatus.Warn => "WARN",
            DoctorStatus.Fail => "FAIL",
            _ => "?"
        };

        /// <summary>Message.</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Doctor check status.
    /// </summary>
    public enum DoctorStatus
    {
        /// <summary>Check passed.</summary>
        Pass,

        /// <summary>Warning.</summary>
        Warn,

        /// <summary>Check failed.</summary>
        Fail
    }

    /// <summary>
    /// Converts DoctorStatus to a background color brush.
    /// </summary>
    public class DoctorStatusColorConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush _Green = SolidColorBrush.Parse("#22C55E");
        private static readonly SolidColorBrush _Gold = SolidColorBrush.Parse("#EAB308");
        private static readonly SolidColorBrush _Red = SolidColorBrush.Parse("#EF4444");

        /// <inheritdoc />
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count > 0 && values[0] is DoctorStatus status)
            {
                return status switch
                {
                    DoctorStatus.Pass => _Green,
                    DoctorStatus.Warn => _Gold,
                    DoctorStatus.Fail => _Red,
                    _ => _Green
                };
            }
            return _Green;
        }
    }
}
