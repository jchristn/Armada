namespace Armada.Harbor
{
    using Armada.Core.Hosting;

    /// <summary>
    /// Carries out the commands in the Harbor menus and reports which are available right now.
    /// </summary>
    public interface IHarborMenuHost
    {
        /// <summary>
        /// One-line link status for the top of the tray menu, for example "Connected to 127.0.0.1:7890".
        /// </summary>
        string StatusLine { get; }

        /// <summary>
        /// True when the command can run now.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <returns>True when enabled.</returns>
        bool CanExecute(HarborMenuCommandEnum command);

        /// <summary>
        /// Why the command is unavailable, shown as the item's tooltip where the platform supports one; null when
        /// there is nothing useful to say.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <returns>Reason, or null.</returns>
        string? DisabledReason(HarborMenuCommandEnum command);

        /// <summary>
        /// Run the command. Does nothing when <see cref="CanExecute"/> is false.
        /// </summary>
        /// <param name="command">Command.</param>
        void Execute(HarborMenuCommandEnum command);
    }
}
