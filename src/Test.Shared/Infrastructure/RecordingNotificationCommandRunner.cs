namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Records notification commands instead of running them, so tests never raise real desktop notifications.
    /// </summary>
    public sealed class RecordingNotificationCommandRunner : INotificationCommandRunner
    {
        #region Public-Members

        /// <summary>
        /// Commands run, in order.
        /// </summary>
        public List<RecordedNotificationCommand> Calls { get; } = new List<RecordedNotificationCommand>();

        /// <summary>
        /// When set, thrown after recording (simulates a notifier that cannot start).
        /// </summary>
        public Exception? Failure { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add(new RecordedNotificationCommand(fileName, arguments.ToList()));
            if (Failure != null) throw Failure;
        }

        #endregion
    }
}
