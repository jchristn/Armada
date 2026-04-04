namespace Armada.Proxy
{
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Loader;
    using System.Threading;
    using Armada.Core;
    using Armada.Proxy.Settings;
    using SyslogLogging;

    /// <summary>
    /// Armada.Proxy entry point.
    /// </summary>
    public class Program
    {
        private static ProxySettings _Settings = new ProxySettings();
        private static LoggingModule _Logging = null!;
        private static ArmadaProxyServer _Server = null!;
        private static bool _ShuttingDown = false;
        private static CancellationTokenSource _TokenSource = new CancellationTokenSource();

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception? ex = e.ExceptionObject as Exception;
                string msg = "[Program] FATAL unhandled exception: " + (ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "unknown");
                try { _Logging?.Warn(msg); } catch { }
                Console.Error.WriteLine(msg);
                try { File.AppendAllText(Path.Combine(Constants.DefaultDataDirectory, "proxy-crash.log"), DateTime.UtcNow.ToString("o") + " " + msg + Environment.NewLine); } catch { }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                string msg = "[Program] unobserved task exception: " + e.Exception?.ToString();
                try { _Logging?.Warn(msg); } catch { }
                Console.Error.WriteLine(msg);
                try { File.AppendAllText(Path.Combine(Constants.DefaultDataDirectory, "proxy-crash.log"), DateTime.UtcNow.ToString("o") + " " + msg + Environment.NewLine); } catch { }
                e.SetObserved();
            };

            Console.WriteLine(@"                        _      ");
            Console.WriteLine(@" __ _ _ _ _ __  __ _ __| |__ _ ");
            Console.WriteLine(@"/ _` | '_| '  \/ _` / _` / _` |");
            Console.WriteLine(@"\__,_|_| |_|_|_\__,_\__,_\__,_|");
            Console.WriteLine();
            Console.WriteLine(Constants.ProductName + " Proxy v" + Constants.ProductVersion);
            Console.WriteLine();

            _Settings = ProxySettings.Load(GetExplicitConfigPath(args));

            InitializeDirectories();
            InitializeLogging();

            _Logging.Info("[Program] starting Proxy on port " + _Settings.Port);

            EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

            _Server = new ArmadaProxyServer(_Logging, _Settings);
            _Server.OnStopping = () => waitHandle.Set();
            await _Server.StartAsync().ConfigureAwait(false);

            string displayHost = ResolveDisplayHost(_Settings.Hostname);
            Console.WriteLine("Proxy running on http://" + displayHost + ":" + _Settings.Port);
            Console.WriteLine("Tunnel endpoint: ws://" + displayHost + ":" + _Settings.Port + "/tunnel");
            Console.WriteLine("Dashboard: http://" + displayHost + ":" + _Settings.Port + "/");
            Console.WriteLine("OpenAPI: http://" + displayHost + ":" + _Settings.Port + "/openapi.json");
            Console.WriteLine("Swagger: http://" + displayHost + ":" + _Settings.Port + "/swagger");

            AssemblyLoadContext.Default.Unloading += (ctx) => waitHandle.Set();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;

                if (!_ShuttingDown)
                {
                    Console.WriteLine(Environment.NewLine + Environment.NewLine + "Shutdown requested" + Environment.NewLine + Environment.NewLine);
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

        private static string? GetExplicitConfigPath(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && (i + 1) < args.Length)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static string ResolveDisplayHost(string hostname)
        {
            if (String.IsNullOrWhiteSpace(hostname)) return "localhost";
            if (hostname == "*" || hostname == "+" || hostname == "0.0.0.0") return "localhost";
            return hostname;
        }

        private static void InitializeDirectories()
        {
            Directory.CreateDirectory(Constants.DefaultDataDirectory);

            string logDirectory = Path.Combine(Constants.DefaultDataDirectory, "logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
        }

        private static void InitializeLogging()
        {
            string logDirectory = Path.Combine(Constants.DefaultDataDirectory, "logs");
            List<SyslogServer> syslogServers = _Settings.SyslogServers ?? new List<SyslogServer>();

            _Logging = new LoggingModule(syslogServers, true);
            _Logging.Settings.EnableConsole = true;
            _Logging.Settings.EnableColors = true;
            _Logging.Settings.MinimumSeverity = Severity.Debug;
            _Logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
            _Logging.Settings.LogFilename = Path.Combine(logDirectory, "proxy.log");
        }
    }
}
