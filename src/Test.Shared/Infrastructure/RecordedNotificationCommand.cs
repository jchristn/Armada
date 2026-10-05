namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One notification command captured by <see cref="RecordingNotificationCommandRunner"/>.
    /// </summary>
    public sealed class RecordedNotificationCommand
    {
        #region Public-Members

        /// <summary>
        /// Executable.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Arguments.
        /// </summary>
        public List<string> Arguments { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="fileName">Executable.</param>
        /// <param name="arguments">Arguments.</param>
        public RecordedNotificationCommand(string fileName, List<string> arguments)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        }

        #endregion
    }
}
