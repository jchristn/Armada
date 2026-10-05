namespace Armada.Core.Hosting
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Reads the real user id on Linux and macOS without spawning <c>id -u</c>.
    /// </summary>
    public static class UnixIdentity
    {
        #region Public-Methods

        /// <summary>
        /// Real user id of the current process.
        /// </summary>
        /// <returns>The uid, or -1 on Windows or when libc is unavailable.</returns>
        public static int GetUserId()
        {
            if (OperatingSystem.IsWindows()) return -1;
            try
            {
                return (int)getuid();
            }
            catch (DllNotFoundException)
            {
                return -1;
            }
            catch (EntryPointNotFoundException)
            {
                return -1;
            }
        }

        #endregion

        #region Private-Methods

        [DllImport("libc", SetLastError = false)]
        private static extern uint getuid();

        #endregion
    }
}
