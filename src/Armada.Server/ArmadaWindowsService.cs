namespace Armada.Server
{
    using System;
    using System.Runtime.Versioning;
    using System.ServiceProcess;
    using System.Threading.Tasks;

    /// <summary>
    /// Windows Service host for <c>--run-service</c>. The service control manager starts the Admiral through this
    /// class; it runs the same server loop as a console launch and stops it when the service is stopped. When the
    /// server stops on its own (for example the stop_server API), the service reports itself stopped so the SCM state
    /// matches, and a startup failure exits with a non-zero service exit code so the configured restart policy applies.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ArmadaWindowsService : ServiceBase
    {
        #region Private-Members

        private Task? _RunTask;
        private volatile bool _StopRequested = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="serviceName">Service name registered with the SCM.</param>
        public ArmadaWindowsService(string serviceName)
        {
            if (String.IsNullOrWhiteSpace(serviceName)) throw new ArgumentNullException(nameof(serviceName));
            ServiceName = serviceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Start the server on a background task and return promptly, as the SCM requires.
        /// </summary>
        /// <param name="args">Start parameters (unused).</param>
        protected override void OnStart(string[] args)
        {
            _RunTask = Task.Run(() => Program.RunServerAsync(true));
            _RunTask.ContinueWith(OnServerExited, TaskScheduler.Default);
        }

        /// <summary>
        /// Signal the server to stop and wait for it, bounded so the SCM is not held indefinitely.
        /// </summary>
        protected override void OnStop()
        {
            _StopRequested = true;
            Program.RequestStop();
            try
            {
                _RunTask?.Wait(TimeSpan.FromSeconds(60));
            }
            catch (AggregateException)
            {
                // Already reported by OnServerExited.
            }
        }

        /// <summary>
        /// Treat a system shutdown like a service stop.
        /// </summary>
        protected override void OnShutdown()
        {
            OnStop();
        }

        private void OnServerExited(Task task)
        {
            if (_StopRequested) return;
            if (task.IsFaulted)
            {
                ExitCode = 1;
                try { EventLog.WriteEntry("Armada Admiral stopped after an error: " + task.Exception?.GetBaseException().Message, System.Diagnostics.EventLogEntryType.Error); } catch (Exception) { }
            }
            _StopRequested = true;
            Stop();
        }

        #endregion
    }
}
