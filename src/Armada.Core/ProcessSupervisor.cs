namespace Armada.Core
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using Armada.Core.Enums;

    /// <summary>
    /// Cross-platform helpers for supervising launched agent processes. Uses only the .NET
    /// <see cref="Process"/> API, which behaves consistently on Windows, Linux, and macOS, so no
    /// OS-specific process handling is required. Process-tree termination is performed by the agent
    /// runtimes via <c>Process.Kill(entireProcessTree: true)</c>.
    /// <para>
    /// Identity: when the exact start time of a local launch was recorded with its process id, a live process with that
    /// id is the launched one only when its start time matches within <see cref="StartTimeMatchTolerance"/>. Without a
    /// recorded start time (Harbor and in-process launches, and missions launched before it was recorded) the looser
    /// launch-time check applies: a process that started more than <see cref="LaunchTimeTolerance"/> after the launch is a
    /// reused id. A live process whose start time cannot be read is treated as the launched one, so nothing becomes
    /// unstoppable. Thread safe.
    /// </para>
    /// </summary>
    public static class ProcessSupervisor
    {
        #region Public-Members

        /// <summary>
        /// Largest difference between a live process start time and the recorded launch start time that still counts as the
        /// same process. Default 2 seconds, which absorbs how some platforms derive start times from boot time; minimum 0,
        /// maximum 1 minute. A reused process id would need to be reused within this window to be mistaken for the original.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below zero or above one minute.</exception>
        public static TimeSpan StartTimeMatchTolerance
        {
            get => _StartTimeMatchTolerance;
            set
            {
                if (value < TimeSpan.Zero || value > TimeSpan.FromMinutes(1))
                    throw new ArgumentOutOfRangeException(nameof(StartTimeMatchTolerance), "Start time tolerance must be between 0 and 1 minute.");
                _StartTimeMatchTolerance = value;
            }
        }

        /// <summary>
        /// Tolerance applied when only an approximate launch time is known (for example the mission start time), to absorb
        /// clock skew and launch latency. Default 5 minutes.
        /// </summary>
        public static TimeSpan LaunchTimeTolerance { get; } = TimeSpan.FromMinutes(5);

        #endregion

        #region Private-Members

        private static TimeSpan _StartTimeMatchTolerance = TimeSpan.FromSeconds(2);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Determine whether the process with the given identifier is alive AND is plausibly the
        /// originally-launched process rather than a recycled PID. When <paramref name="launchedBeforeUtc"/>
        /// is supplied, a process whose start time is meaningfully later than the launch reference is
        /// treated as a different (recycled) process and reported as not alive, so stale captains are
        /// not left running because the OS reused their PID after a crash.
        /// </summary>
        /// <param name="processId">OS process identifier.</param>
        /// <param name="launchedBeforeUtc">Approximate time the tracked process was launched (e.g. the mission start time), or null to skip identity verification.</param>
        /// <returns>True if the process exists, has not exited, and matches the launch reference.</returns>
        public static bool IsTrackedProcessAlive(int processId, DateTime? launchedBeforeUtc = null)
        {
            return IsTrackedProcessAlive(processId, launchedBeforeUtc, null, null);
        }

        /// <summary>
        /// Determine whether the process is alive and is the launched one, using the exact recorded launch start time when
        /// it was recorded for this process id and the approximate launch time otherwise.
        /// </summary>
        /// <param name="processId">OS process identifier.</param>
        /// <param name="launchedBeforeUtc">Approximate launch time (for example the mission start time), or null.</param>
        /// <param name="recordedProcessId">Process id the exact start time was recorded for, or null.</param>
        /// <param name="recordedStartUtc">Exact start time recorded at launch, or null.</param>
        /// <returns>True when the process is running and is not a reused id.</returns>
        public static bool IsTrackedProcessAlive(int processId, DateTime? launchedBeforeUtc, int? recordedProcessId, DateTime? recordedStartUtc)
        {
            ProcessIdentityEnum identity = CheckIdentity(processId, launchedBeforeUtc, recordedProcessId, recordedStartUtc);
            return identity == ProcessIdentityEnum.Verified || identity == ProcessIdentityEnum.Unverified;
        }

        /// <summary>
        /// Classify the live process with the given id against what is known about its launch.
        /// </summary>
        /// <param name="processId">OS process identifier.</param>
        /// <param name="launchedBeforeUtc">Approximate launch time (for example the mission start time), or null.</param>
        /// <param name="recordedProcessId">Process id the exact start time was recorded for, or null. The exact start
        /// time applies only when this equals <paramref name="processId"/>.</param>
        /// <param name="recordedStartUtc">Exact start time recorded at launch, or null.</param>
        /// <returns>The identity of the live process.</returns>
        public static ProcessIdentityEnum CheckIdentity(int processId, DateTime? launchedBeforeUtc, int? recordedProcessId, DateTime? recordedStartUtc)
        {
            // Non-positive ids are never a real child process. On macOS/Linux, GetProcessById(-1) can succeed because
            // kill(-1, 0) addresses every process the caller may signal.
            if (processId <= 0) return ProcessIdentityEnum.NotRunning;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return ProcessIdentityEnum.NotRunning;
                    if (!TryReadStartTimeUtc(process, out DateTime startedUtc)) return ProcessIdentityEnum.Unverified;
                    return Compare(startedUtc, processId, launchedBeforeUtc, recordedProcessId, recordedStartUtc);
                }
            }
            catch (ArgumentException)
            {
                return ProcessIdentityEnum.NotRunning;
            }
            catch (InvalidOperationException)
            {
                return ProcessIdentityEnum.NotRunning;
            }
            catch (Win32Exception)
            {
                return ProcessIdentityEnum.NotRunning;
            }
        }

        /// <summary>
        /// Compare a live process start time with what is known about the launch. Pure; used by
        /// <see cref="CheckIdentity"/> and by tests.
        /// </summary>
        /// <param name="liveStartUtc">Start time of the live process.</param>
        /// <param name="processId">Process id of the live process.</param>
        /// <param name="launchedBeforeUtc">Approximate launch time, or null.</param>
        /// <param name="recordedProcessId">Process id the exact start time was recorded for, or null.</param>
        /// <param name="recordedStartUtc">Exact start time recorded at launch, or null.</param>
        /// <returns>Verified, Reused or Unverified.</returns>
        public static ProcessIdentityEnum Compare(DateTime liveStartUtc, int processId, DateTime? launchedBeforeUtc, int? recordedProcessId, DateTime? recordedStartUtc)
        {
            DateTime live = ToUtc(liveStartUtc);
            if (recordedStartUtc.HasValue && recordedProcessId.HasValue && recordedProcessId.Value == processId)
            {
                TimeSpan difference = (live - ToUtc(recordedStartUtc.Value)).Duration();
                return difference <= _StartTimeMatchTolerance ? ProcessIdentityEnum.Verified : ProcessIdentityEnum.Reused;
            }

            if (launchedBeforeUtc.HasValue && live > ToUtc(launchedBeforeUtc.Value).Add(LaunchTimeTolerance))
                return ProcessIdentityEnum.Reused;
            return ProcessIdentityEnum.Unverified;
        }

        /// <summary>
        /// Stop a local agent process and its children, but only when a live process with this id started at the recorded
        /// time (RS-4.10). A reused id, an unreadable start time, or a process that already ended is left alone.
        /// </summary>
        /// <param name="processId">OS process identifier recorded at launch.</param>
        /// <param name="recordedStartUtc">Exact start time recorded at launch.</param>
        /// <returns>True when the process was running and was stopped.</returns>
        public static bool StopIfVerified(int processId, DateTime recordedStartUtc)
        {
            if (CheckIdentity(processId, null, processId, recordedStartUtc) != ProcessIdentityEnum.Verified) return false;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Read the start time of a running process.
        /// </summary>
        /// <param name="processId">OS process identifier.</param>
        /// <param name="startedUtc">The UTC start time, when it was read.</param>
        /// <returns>True when the process is running and its start time was read.</returns>
        public static bool TryReadStartTimeUtc(int processId, out DateTime startedUtc)
        {
            startedUtc = default;
            if (processId <= 0) return false;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return false;
                    return TryReadStartTimeUtc(process, out startedUtc);
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private static bool TryReadStartTimeUtc(Process process, out DateTime startedUtc)
        {
            startedUtc = default;
            try
            {
                startedUtc = ToUtc(process.StartTime);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value;
        }

        #endregion
    }
}
