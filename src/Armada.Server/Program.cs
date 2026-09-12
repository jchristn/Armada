namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Loader;
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
            // Catch unhandled exceptions so the server doesn't silently die
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception? ex = e.ExceptionObject as Exception;
                string msg = "[Program] FATAL unhandled exception: " + (ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "unknown");
                try { _Logging?.Warn(msg); } catch { }
                Console.Error.WriteLine(msg);
                try { File.AppendAllText(Path.Combine(Constants.DefaultDataDirectory, "crash.log"), DateTime.UtcNow.ToString("o") + " " + msg + Environment.NewLine); } catch { }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                string msg = "[Program] unobserved task exception: " + e.Exception?.ToString();
                try { _Logging?.Warn(msg); } catch { }
                Console.Error.WriteLine(msg);
                try { File.AppendAllText(Path.Combine(Constants.DefaultDataDirectory, "crash.log"), DateTime.UtcNow.ToString("o") + " " + msg + Environment.NewLine); } catch { }
                e.SetObserved(); // Prevent process termination
            };

            Console.WriteLine(@"                        _      ");
            Console.WriteLine(@" __ _ _ _ _ __  __ _ __| |__ _ ");
            Console.WriteLine(@"/ _` | '_| '  \/ _` / _` / _` |");
            Console.WriteLine(@"\__,_|_| |_|_|_\__,_\__,_\__,_|");
            Console.WriteLine();
            Console.WriteLine(Constants.ProductName + " Admiral v" + Constants.ProductVersion);
            Console.WriteLine();

            // Load settings
            string settingsPath = Path.Combine(Constants.DefaultDataDirectory, "settings.json");
            _Settings = await ArmadaSettings.LoadAsync(settingsPath).ConfigureAwait(false);

            // Initialize directories
            _Settings.InitializeDirectories();

            // Initialize logging
            InitializeLogging();

            _Logging.Info("[Program] starting Admiral on port " + _Settings.AdmiralPort);

            // If this instance was launched as the replacement half of an in-place restart, wait for the
            // outgoing Admiral to fully exit before binding so the two never race for the listening port.
            await WaitForPredecessorExitAsync().ConfigureAwait(false);

            // Build and run server
            EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

            _Server = new ArmadaServer(_Logging, _Settings);
            _Server.OnStopping = () => waitHandle.Set();
            await _Server.StartAsync().ConfigureAwait(false);

            Console.WriteLine("Admiral running on port " + _Settings.AdmiralPort);
            Console.WriteLine("MCP server on port " + _Settings.McpPort);
            Console.WriteLine("WebSocket endpoint at /ws");
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

        private static async Task WaitForPredecessorExitAsync()
        {
            string? pidValue = Environment.GetEnvironmentVariable(Constants.RestartWaitPidEnvVar);
            if (string.IsNullOrWhiteSpace(pidValue)) return;
            if (!int.TryParse(pidValue, out int pid) || pid <= 0) return;

            _Logging.Debug("[Program] restart: waiting for predecessor process " + pid + " to exit before binding");

            System.Diagnostics.Process? predecessor = null;
            try
            {
                predecessor = System.Diagnostics.Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                // Predecessor already gone; nothing to wait for.
                return;
            }

            using (predecessor)
            {
                // Bound the wait so a stuck predecessor cannot hang startup indefinitely.
                for (int i = 0; i < 30; i++)
                {
                    try
                    {
                        predecessor.Refresh();
                        if (predecessor.HasExited)
                        {
                            _Logging.Debug("[Program] restart: predecessor process " + pid + " exited");
                            return;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                }

                _Logging.Warn("[Program] restart: predecessor process " + pid + " did not exit within the timeout; binding anyway");
            }
        }

        private static void InitializeLogging()
        {
            List<SyslogServer> syslogServers = _Settings.SyslogServers ?? new List<SyslogServer>();

            _Logging = syslogServers.Count > 0
                ? new LoggingModule(syslogServers, true)
                : new LoggingModule();
            _Logging.Settings.EnableConsole = true;
            _Logging.Settings.EnableColors = true;
            _Logging.Settings.MinimumSeverity = Severity.Debug;
            _Logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            if (!Directory.Exists(_Settings.LogDirectory))
            {
                Directory.CreateDirectory(_Settings.LogDirectory);
            }

            _Logging.Settings.LogFilename = Path.Combine(_Settings.LogDirectory, "admiral.log");
        }
    }
}
