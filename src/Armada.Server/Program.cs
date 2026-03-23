namespace Armada.Server
{
    using System;
    using System.IO;
    using System.Runtime.Loader;
    using System.Text.Json;
    using System.Threading;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Settings;

    /// <summary>
    /// Admiral server entry point.
    /// </summary>
    public class Program
    {
        private static ArmadaSettings _Settings = new ArmadaSettings();
        private static LoggingModule _Logging = null!;
        private static ArmadaServer _Server = null!;
        private static bool _ShuttingDown = false;
        private static CancellationTokenSource _TokenSource = new CancellationTokenSource();

        static async Task Main(string[] args)
        {
            Console.WriteLine(@"                        _      ");
            Console.WriteLine(@" __ _ _ _ _ __  __ _ __| |__ _ ");
            Console.WriteLine(@"/ _` | '_| '  \/ _` / _` / _` |");
            Console.WriteLine(@"\__,_|_| |_|_|_\__,_\__,_\__,_|");
            Console.WriteLine();
            Console.WriteLine(Constants.ProductName + " Admiral v" + Constants.ProductVersion);
            Console.WriteLine();

            // Load settings
            string settingsPath = Path.Combine(Constants.DefaultDataDirectory, "settings.json");
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                ArmadaSettings? loaded = JsonSerializer.Deserialize<ArmadaSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded != null) _Settings = loaded;
            }

            // Initialize directories
            _Settings.InitializeDirectories();

            // Initialize logging
            _Logging = new LoggingModule();
            _Logging.Settings.EnableConsole = true;
            _Logging.Settings.EnableColors = true;
            _Logging.Settings.MinimumSeverity = Severity.Debug;
            _Logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            if (!Directory.Exists(_Settings.LogDirectory))
                Directory.CreateDirectory(_Settings.LogDirectory);
            _Logging.Settings.LogFilename = Path.Combine(_Settings.LogDirectory, "admiral.log");

            _Logging.Info("[Program] starting Admiral on port " + _Settings.AdmiralPort);

            // Build and run server
            EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

            _Server = new ArmadaServer(_Logging, _Settings);
            _Server.OnStopping = () => waitHandle.Set();
            await _Server.StartAsync().ConfigureAwait(false);

            Console.WriteLine("Admiral running on port " + _Settings.AdmiralPort);
            Console.WriteLine("MCP server on port " + _Settings.McpPort);
            Console.WriteLine("WebSocket hub on port " + _Settings.WebSocketPort);
            Console.WriteLine("Dashboard: http://localhost:" + _Settings.AdmiralPort + "/dashboard");

            // Wait for shutdown signal (Ctrl+C, SIGTERM, API stop, or assembly unload)
            AssemblyLoadContext.Default.Unloading += (ctx) => waitHandle.Set();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;

                if (!_ShuttingDown)
                {
                    Console.WriteLine(
                        Environment.NewLine +
                        Environment.NewLine +
                        "Shutdown requested" +
                        Environment.NewLine +
                        Environment.NewLine);
                    _TokenSource.Cancel();
                    _ShuttingDown = true;

                    waitHandle.Set();
                }
            };

            bool waitHandleSignal = false;
            do
            {
                waitHandleSignal = waitHandle.WaitOne(1000);
            }
            while (!waitHandleSignal);

            _Server.Stop();
            _Logging.Info("[Program] stopped at " + DateTime.UtcNow.ToString("o"));
        }
    }
}
