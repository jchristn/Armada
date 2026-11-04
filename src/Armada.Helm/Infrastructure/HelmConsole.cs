namespace Armada.Helm.Infrastructure
{
    using System;
    using Spectre.Console;

    /// <summary>
    /// Where Helm writes. Results go to the standard <see cref="AnsiConsole"/> (stdout); errors and diagnostics go to
    /// <see cref="Error"/> (stderr), so a script that reads stdout never parses an error as a result.
    /// </summary>
    public static class HelmConsole
    {
        #region Public-Members

        /// <summary>
        /// A console over the current <see cref="Console.Error"/>, created per use so a redirected stderr is honored.
        /// Colors are kept only when stderr is an ANSI terminal.
        /// </summary>
        public static IAnsiConsole Error
        {
            get
            {
                bool terminal = !Console.IsErrorRedirected && AnsiConsole.Profile.Capabilities.Ansi;
                return AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(Console.Error),
                    Ansi = terminal ? AnsiSupport.Detect : AnsiSupport.No,
                    ColorSystem = terminal ? ColorSystemSupport.Detect : ColorSystemSupport.NoColors
                });
            }
        }

        #endregion
    }
}
