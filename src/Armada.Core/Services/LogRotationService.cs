namespace Armada.Core.Services
{
    using SyslogLogging;

    /// <summary>
    /// Rotates log files when they exceed a configured maximum size.
    /// </summary>
    public class LogRotationService
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[LogRotationService] ";
        private LoggingModule _Logging;
        private long _MaxFileSizeBytes;
        private int _MaxFileCount;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="maxFileSizeBytes">Maximum log file size in bytes before rotation.</param>
        /// <param name="maxFileCount">Maximum number of rotated files to keep.</param>
        public LogRotationService(LoggingModule logging, long maxFileSizeBytes, int maxFileCount)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MaxFileSizeBytes = maxFileSizeBytes;
            _MaxFileCount = maxFileCount;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check and rotate a single log file if it exceeds the maximum size.
        /// </summary>
        /// <param name="filePath">Path to the log file.</param>
        public void RotateIfNeeded(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) return;
            if (!File.Exists(filePath)) return;

            FileInfo fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < _MaxFileSizeBytes) return;

            _Logging.Info(_Header + "rotating log file: " + filePath + " (" + fileInfo.Length + " bytes)");

            // Shift existing rotated files: .4 -> .5, .3 -> .4, etc.
            for (int i = _MaxFileCount - 1; i >= 1; i--)
            {
                string source = filePath + "." + i;
                string destination = filePath + "." + (i + 1);

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                if (File.Exists(source))
                {
                    File.Move(source, destination);
                }
            }

            // Rotate current file to .1
            string rotatedPath = filePath + ".1";
            if (File.Exists(rotatedPath))
            {
                File.Delete(rotatedPath);
            }

            File.Move(filePath, rotatedPath);

            // Delete overflow files beyond max count
            for (int i = _MaxFileCount + 1; i <= _MaxFileCount + 5; i++)
            {
                string overflow = filePath + "." + i;
                if (File.Exists(overflow))
                {
                    File.Delete(overflow);
                }
            }
        }

        /// <summary>
        /// Scan a directory and rotate all log files that exceed the maximum size.
        /// </summary>
        /// <param name="directoryPath">Directory containing log files.</param>
        /// <param name="searchPattern">File search pattern (default: *.log).</param>
        public void RotateAllInDirectory(string directoryPath, string searchPattern = "*.log")
        {
            if (String.IsNullOrEmpty(directoryPath)) return;
            if (!Directory.Exists(directoryPath)) return;

            string[] logFiles = Directory.GetFiles(directoryPath, searchPattern);
            foreach (string logFile in logFiles)
            {
                try
                {
                    RotateIfNeeded(logFile);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error rotating " + logFile + ": " + ex.Message);
                }
            }
        }

        #endregion
    }
}
