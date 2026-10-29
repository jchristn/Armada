namespace Armada.Core
{
    using System.Collections.Concurrent;
    using System.ComponentModel;
    using System.Diagnostics;
    using Armada.Core.Enums;

    /// <summary>
    /// Owns the decision of whether a process id still belongs to the agent that was launched.
    /// Two reads of the same process start time match. A start time one second away is a different
    /// process. A missing or unreadable start time is not a match.
    /// </summary>
    public static class ProcessSupervisor
    {
        #region Private-Members

        private static readonly TimeSpan _StartTimeTolerance = TimeSpan.FromMilliseconds(500);

        private static readonly ConcurrentDictionary<int, DateTime> _LaunchStarts = new ConcurrentDictionary<int, DateTime>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the start time captured immediately after a local process launch.
        /// </summary>
        /// <param name="processId">Operating system process id.</param>
        /// <param name="startedUtc">Process start time in UTC.</param>
        public static void RecordLaunch(int processId, DateTime startedUtc)
        {
            if (processId <= 0) return;
            _LaunchStarts[processId] = DateTime.SpecifyKind(startedUtc, DateTimeKind.Utc);
        }

        /// <summary>
        /// Restore a start time loaded from storage. A start time already recorded in this process wins.
        /// </summary>
        /// <param name="processId">Operating system process id.</param>
        /// <param name="startedUtc">Stored process start time in UTC.</param>
        /// <returns>True when this call stored the value.</returns>
        public static bool RestoreLaunch(int processId, DateTime startedUtc)
        {
            if (processId <= 0) return false;
            return _LaunchStarts.TryAdd(processId, DateTime.SpecifyKind(startedUtc, DateTimeKind.Utc));
        }

        /// <summary>
        /// Drop a recorded launch, for example after the process id is cleared.
        /// </summary>
        /// <param name="processId">Operating system process id.</param>
        public static void ForgetLaunch(int processId)
        {
            DateTime ignored;
            _LaunchStarts.TryRemove(processId, out ignored);
        }

        /// <summary>
        /// Read the start time recorded for a process id in this process.
        /// </summary>
        /// <param name="processId">Operating system process id.</param>
        /// <returns>The recorded UTC start time, or null.</returns>
        public static DateTime? GetLaunchStartUtc(int processId)
        {
            DateTime startedUtc;
            if (_LaunchStarts.TryGetValue(processId, out startedUtc)) return startedUtc;
            return null;
        }

        /// <summary>
        /// Whether the live process is the recorded launch. A null start time is not a match.
        /// </summary>
        /// <param name="processId">Operating system process id.</param>
        /// <param name="recordedStartUtc">Stored start time used when this process has no in-memory record.</param>
        /// <returns>True only when the live start time matches.</returns>
        public static bool IsTrackedProcessAlive(int processId, DateTime? recordedStartUtc = null)
        {
            return Classify(processId, recordedStartUtc) == LaunchedProcessIdentityEnum.Verified;
        }

        /// <summary>
        /// Compare a live process with the launch recorded for its id.
        /// </summary>
        /// <param name="processId">Operating system process id.</param>
        /// <param name="recordedStartUtc">Stored start time used when this process has no in-memory record.</param>
        /// <returns>The identity of the live process.</returns>
        public static LaunchedProcessIdentityEnum Classify(int processId, DateTime? recordedStartUtc = null)
        {
            if (processId <= 0) return LaunchedProcessIdentityEnum.NotRunning;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return Classify(process, processId, recordedStartUtc);
                }
            }
            catch (ArgumentException)
            {
                return LaunchedProcessIdentityEnum.NotRunning;
            }
            catch (InvalidOperationException)
            {
                return LaunchedProcessIdentityEnum.NotRunning;
            }
        }

        /// <summary>
        /// Compare an already opened process with the launch recorded for its id.
        /// </summary>
        /// <param name="process">Opened process.</param>
        /// <param name="processId">Operating system process id.</param>
        /// <param name="recordedStartUtc">Stored start time used when this process has no in-memory record.</param>
        /// <returns>The identity of the live process.</returns>
        public static LaunchedProcessIdentityEnum Classify(Process process, int processId, DateTime? recordedStartUtc = null)
        {
            if (process == null) return LaunchedProcessIdentityEnum.NotRunning;
            try
            {
                if (process.HasExited) return LaunchedProcessIdentityEnum.NotRunning;
            }
            catch (InvalidOperationException)
            {
                return LaunchedProcessIdentityEnum.NotRunning;
            }

            DateTime observedUtc;
            try
            {
                observedUtc = process.StartTime.ToUniversalTime();
            }
            catch (InvalidOperationException)
            {
                return LaunchedProcessIdentityEnum.Unverified;
            }
            catch (Win32Exception)
            {
                return LaunchedProcessIdentityEnum.Unverified;
            }
            catch (NotSupportedException)
            {
                return LaunchedProcessIdentityEnum.Unverified;
            }

            DateTime? expectedUtc = GetLaunchStartUtc(processId) ?? recordedStartUtc;
            if (!expectedUtc.HasValue) return LaunchedProcessIdentityEnum.Unverified;
            TimeSpan difference = (observedUtc - expectedUtc.Value.ToUniversalTime()).Duration();
            if (difference > _StartTimeTolerance) return LaunchedProcessIdentityEnum.Reused;
            return LaunchedProcessIdentityEnum.Verified;
        }

        #endregion
    }
}
