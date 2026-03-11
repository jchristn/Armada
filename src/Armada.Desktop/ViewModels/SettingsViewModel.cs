namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia;
    using Avalonia.Threading;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Desktop.Services;

    /// <summary>
    /// Settings page view model.
    /// </summary>
    public class SettingsViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private int _RefreshInterval = 5;
        private int _AdmiralPort = 7890;
        private int _McpPort = 7891;
        private int _MaxCaptains = 5;
        private int _HeartbeatIntervalSeconds = 30;
        private int _StallThresholdMinutes = 10;
        private int _IdleCaptainTimeoutSeconds = 0;
        private bool _Notifications = true;
        private bool _AutoCreatePr = false;
        private string _ThemeMode = "Dark";
        private bool _IsDarkMode = true;
        private string _StatusMessage = "";
        private string _DefaultRuntime = "";
        private string _ApiKey = "";
        private string _ServerStatus = "";
        private string _McpConfigHttp = "";
        private string _McpConfigStdio = "";

        // Read-only paths
        private string _DataDirectory = "";
        private string _DatabasePath = "";
        private string _LogDirectory = "";
        private string _DocksDirectory = "";
        private string _ReposDirectory = "";

        #endregion

        #region Public-Members

        /// <summary>Refresh interval in seconds.</summary>
        public int RefreshInterval
        {
            get => _RefreshInterval;
            set => this.RaiseAndSetIfChanged(ref _RefreshInterval, value);
        }

        /// <summary>Admiral port.</summary>
        public int AdmiralPort
        {
            get => _AdmiralPort;
            set => this.RaiseAndSetIfChanged(ref _AdmiralPort, value);
        }

        /// <summary>MCP port.</summary>
        public int McpPort
        {
            get => _McpPort;
            set => this.RaiseAndSetIfChanged(ref _McpPort, value);
        }

        /// <summary>Max captains.</summary>
        public int MaxCaptains
        {
            get => _MaxCaptains;
            set => this.RaiseAndSetIfChanged(ref _MaxCaptains, value);
        }

        /// <summary>Heartbeat interval in seconds.</summary>
        public int HeartbeatIntervalSeconds
        {
            get => _HeartbeatIntervalSeconds;
            set => this.RaiseAndSetIfChanged(ref _HeartbeatIntervalSeconds, value);
        }

        /// <summary>Stall threshold in minutes.</summary>
        public int StallThresholdMinutes
        {
            get => _StallThresholdMinutes;
            set => this.RaiseAndSetIfChanged(ref _StallThresholdMinutes, value);
        }

        /// <summary>Idle captain timeout in seconds (0 = disabled).</summary>
        public int IdleCaptainTimeoutSeconds
        {
            get => _IdleCaptainTimeoutSeconds;
            set => this.RaiseAndSetIfChanged(ref _IdleCaptainTimeoutSeconds, value);
        }

        /// <summary>Desktop notifications enabled.</summary>
        public bool Notifications
        {
            get => _Notifications;
            set => this.RaiseAndSetIfChanged(ref _Notifications, value);
        }

        /// <summary>Auto-create PRs.</summary>
        public bool AutoCreatePr
        {
            get => _AutoCreatePr;
            set => this.RaiseAndSetIfChanged(ref _AutoCreatePr, value);
        }

        /// <summary>Available theme modes.</summary>
        public List<string> ThemeModeOptions { get; } = new List<string> { "System", "Light", "Dark" };

        /// <summary>Selected theme mode (System, Light, Dark).</summary>
        public string ThemeMode
        {
            get => _ThemeMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _ThemeMode, value);
                if (Application.Current is App app)
                {
                    app.SetThemeMode(value);
                }
                _IsDarkMode = value == "Dark" || (value == "System" && Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
                this.RaisePropertyChanged(nameof(IsDarkMode));
            }
        }

        /// <summary>Dark mode enabled (read-only, derived from ThemeMode).</summary>
        public bool IsDarkMode
        {
            get => _IsDarkMode;
        }

        /// <summary>Default runtime.</summary>
        public string DefaultRuntime
        {
            get => _DefaultRuntime;
            set => this.RaiseAndSetIfChanged(ref _DefaultRuntime, value);
        }

        /// <summary>API key (masked).</summary>
        public string ApiKey
        {
            get => _ApiKey;
            set => this.RaiseAndSetIfChanged(ref _ApiKey, value);
        }

        /// <summary>Status message.</summary>
        public string StatusMessage
        {
            get => _StatusMessage;
            set => this.RaiseAndSetIfChanged(ref _StatusMessage, value);
        }

        /// <summary>Server status text.</summary>
        public string ServerStatus
        {
            get => _ServerStatus;
            set => this.RaiseAndSetIfChanged(ref _ServerStatus, value);
        }

        /// <summary>MCP HTTP config snippet.</summary>
        public string McpConfigHttp
        {
            get => _McpConfigHttp;
            set => this.RaiseAndSetIfChanged(ref _McpConfigHttp, value);
        }

        /// <summary>MCP stdio config snippet.</summary>
        public string McpConfigStdio
        {
            get => _McpConfigStdio;
            set => this.RaiseAndSetIfChanged(ref _McpConfigStdio, value);
        }

        /// <summary>Data directory (read-only display).</summary>
        public string DataDirectory
        {
            get => _DataDirectory;
            set => this.RaiseAndSetIfChanged(ref _DataDirectory, value);
        }

        /// <summary>Database path (read-only display).</summary>
        public string DatabasePath
        {
            get => _DatabasePath;
            set => this.RaiseAndSetIfChanged(ref _DatabasePath, value);
        }

        /// <summary>Log directory (read-only display).</summary>
        public string LogDirectory
        {
            get => _LogDirectory;
            set => this.RaiseAndSetIfChanged(ref _LogDirectory, value);
        }

        /// <summary>Docks directory (read-only display).</summary>
        public string DocksDirectory
        {
            get => _DocksDirectory;
            set => this.RaiseAndSetIfChanged(ref _DocksDirectory, value);
        }

        /// <summary>Repos directory (read-only display).</summary>
        public string ReposDirectory
        {
            get => _ReposDirectory;
            set => this.RaiseAndSetIfChanged(ref _ReposDirectory, value);
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SettingsViewModel(ArmadaConnectionService connection)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            LoadFromSettings();
            GenerateMcpConfig();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Save settings.
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                ArmadaSettings settings = _Connection.GetSettings();
                settings.AdmiralPort = AdmiralPort;
                settings.McpPort = McpPort;
                settings.MaxCaptains = MaxCaptains;
                settings.HeartbeatIntervalSeconds = HeartbeatIntervalSeconds;
                settings.StallThresholdMinutes = StallThresholdMinutes;
                settings.IdleCaptainTimeoutSeconds = IdleCaptainTimeoutSeconds;
                settings.Notifications = Notifications;
                settings.AutoCreatePullRequests = AutoCreatePr;

                if (!string.IsNullOrWhiteSpace(DefaultRuntime))
                    settings.DefaultRuntime = DefaultRuntime.Trim();
                else
                    settings.DefaultRuntime = null;

                if (!string.IsNullOrWhiteSpace(ApiKey))
                    settings.ApiKey = ApiKey.Trim();
                else
                    settings.ApiKey = null;

                await settings.SaveAsync().ConfigureAwait(false);

                _Connection.PollIntervalMs = RefreshInterval * 1000;

                GenerateMcpConfig();

                Dispatcher.UIThread.Post(() => StatusMessage = "Settings saved.");
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusMessage = "Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Check server health status.
        /// </summary>
        public async Task CheckServerStatusAsync()
        {
            try
            {
                bool healthy = await _Connection.GetApiClient().HealthCheckAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => ServerStatus = healthy ? "Healthy" : "Unhealthy");
            }
            catch
            {
                Dispatcher.UIThread.Post(() => ServerStatus = "Unreachable");
            }
        }

        /// <summary>
        /// Stop the Admiral server.
        /// </summary>
        public async Task StopServerAsync()
        {
            try
            {
                await _Connection.GetApiClient().StopServerAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => ServerStatus = "Stopped");
            }
            catch
            {
                Dispatcher.UIThread.Post(() => ServerStatus = "Error stopping server");
            }
        }

        /// <summary>
        /// Reset Armada to factory state. Deletes database, logs, docks, repos. Preserves settings.
        /// </summary>
        public async Task ResetAsync()
        {
            try
            {
                // Stop server first
                try
                {
                    await _Connection.GetApiClient().StopServerAsync().ConfigureAwait(false);
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                catch { }

                ArmadaSettings settings = _Connection.GetSettings();

                await Task.Run(() =>
                {
                    DeleteDirectoryContents(settings.LogDirectory);
                    DeleteDirectoryContents(settings.DocksDirectory);
                    DeleteDirectoryContents(settings.ReposDirectory);

                    if (File.Exists(settings.DatabasePath))
                        File.Delete(settings.DatabasePath);

                    // Re-create empty directories
                    Directory.CreateDirectory(settings.LogDirectory);
                    Directory.CreateDirectory(settings.DocksDirectory);
                    Directory.CreateDirectory(settings.ReposDirectory);
                }).ConfigureAwait(false);

                Dispatcher.UIThread.Post(() => StatusMessage = "Reset complete. Restart the application.");
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusMessage = "Reset error: " + ex.Message);
            }
        }

        /// <summary>
        /// Install MCP config to Claude Code settings.
        /// </summary>
        public async Task InstallMcpAsync()
        {
            try
            {
                string claudeSettingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude", "settings.json");

                JsonSerializerOptions jsonOpts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };

                ClaudeCodeSettings? codeSettings = null;

                if (File.Exists(claudeSettingsPath))
                {
                    string existingJson = await File.ReadAllTextAsync(claudeSettingsPath).ConfigureAwait(false);
                    codeSettings = JsonSerializer.Deserialize<ClaudeCodeSettings>(existingJson, jsonOpts);
                }

                codeSettings ??= new ClaudeCodeSettings();

                ArmadaSettings settings = _Connection.GetSettings();
                McpServerEntry mcpEntry = new McpServerEntry
                {
                    Type = "http",
                    Url = "http://localhost:" + settings.McpPort
                };

                // Merge into mcpServers
                codeSettings.McpServers ??= new Dictionary<string, McpServerEntry>();
                codeSettings.McpServers["armada"] = mcpEntry;

                Directory.CreateDirectory(Path.GetDirectoryName(claudeSettingsPath)!);
                string json = JsonSerializer.Serialize(codeSettings, jsonOpts);
                await File.WriteAllTextAsync(claudeSettingsPath, json).ConfigureAwait(false);

                Dispatcher.UIThread.Post(() => StatusMessage = "MCP config installed to " + claudeSettingsPath);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusMessage = "MCP install error: " + ex.Message);
            }
        }

        #endregion

        #region Private-Methods

        private void LoadFromSettings()
        {
            ArmadaSettings settings = _Connection.GetSettings();
            AdmiralPort = settings.AdmiralPort;
            McpPort = settings.McpPort;
            MaxCaptains = settings.MaxCaptains;
            HeartbeatIntervalSeconds = settings.HeartbeatIntervalSeconds;
            StallThresholdMinutes = settings.StallThresholdMinutes;
            IdleCaptainTimeoutSeconds = settings.IdleCaptainTimeoutSeconds;
            Notifications = settings.Notifications;
            AutoCreatePr = settings.AutoCreatePullRequests;
            RefreshInterval = _Connection.PollIntervalMs / 1000;
            DefaultRuntime = settings.DefaultRuntime ?? "";
            ApiKey = settings.ApiKey ?? "";

            DataDirectory = settings.DataDirectory;
            DatabasePath = settings.DatabasePath;
            LogDirectory = settings.LogDirectory;
            DocksDirectory = settings.DocksDirectory;
            ReposDirectory = settings.ReposDirectory;

            if (Application.Current is App app)
            {
                _ThemeMode = app.CurrentThemeMode;
                _IsDarkMode = Application.Current.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            }
        }

        private void DeleteDirectoryContents(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                // Handle read-only git files on Windows
                FileAttributes attr = File.GetAttributes(file);
                if ((attr & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                File.Delete(file);
            }
            foreach (string dir in Directory.GetDirectories(path))
            {
                Directory.Delete(dir, true);
            }
        }

        private void GenerateMcpConfig()
        {
            ArmadaSettings settings = _Connection.GetSettings();

            McpConfigHttp = JsonSerializer.Serialize(new
            {
                type = "http",
                url = "http://localhost:" + settings.McpPort
            }, new JsonSerializerOptions { WriteIndented = true });

            string exePath = Environment.ProcessPath ?? "armada";
            McpConfigStdio = JsonSerializer.Serialize(new
            {
                type = "stdio",
                command = exePath,
                args = new[] { "mcp", "stdio" }
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        #endregion
    }
}
