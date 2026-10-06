namespace Armada.Helm.Infrastructure
{
    using System;

    /// <summary>
    /// Marks a CLI command that acts only on this machine's Admiral (its process, settings file, or data), with the
    /// reason shown when a remote target is selected.
    /// </summary>
    public class LocalOnlyCommandInfo
    {
        #region Public-Members

        /// <summary>
        /// Command name as typed, for example <c>server start</c>.
        /// </summary>
        public string CommandName { get; }

        /// <summary>
        /// Why the command cannot act on a remote Admiral.
        /// </summary>
        public string Reason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="commandName">Command name.</param>
        /// <param name="reason">Reason.</param>
        public LocalOnlyCommandInfo(string commandName, string reason)
        {
            CommandName = !String.IsNullOrWhiteSpace(commandName) ? commandName : throw new ArgumentNullException(nameof(commandName));
            Reason = !String.IsNullOrWhiteSpace(reason) ? reason : throw new ArgumentNullException(nameof(reason));
        }

        #endregion
    }
}
