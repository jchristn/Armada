namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Helm;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Runs the <c>armada</c> CLI in process with the real command model (<see cref="Program.ConfigureCommands"/>),
    /// capturing the Spectre console (no colors, not interactive) and standard output. Host independent: the
    /// ARMADA_SERVER_URL, ARMADA_URL, and ARMADA_TOKEN variables are cleared for the run. The runner executes cases
    /// one at a time, so swapping the process-wide console and environment for the call is safe.
    /// </summary>
    public static class HelmCliHarness
    {
        #region Public-Methods

        /// <summary>
        /// Run the CLI and capture its output.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>The run.</returns>
        public static async Task<HelmCliRun> RunCapturedAsync(params string[] args)
        {
            string[] names = new[] { AdmiralTargetResolver.ServerUrlEnvironmentVariable, AdmiralTargetResolver.LegacyServerUrlEnvironmentVariable, AdmiralTargetResolver.TokenEnvironmentVariable };
            Dictionary<string, string?> saved = new Dictionary<string, string?>();
            foreach (string name in names)
            {
                saved[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }

            IAnsiConsole previousConsole = AnsiConsole.Console;
            TextWriter previousOut = Console.Out;
            StringWriter console = new StringWriter();
            StringWriter stdout = new StringWriter();
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(console)
            });
            Console.SetOut(stdout);
            try
            {
                CommandApp app = new CommandApp(new TypeRegistrar());
                app.Configure(config =>
                {
                    Program.ConfigureCommands(config);
                    config.PropagateExceptions();
                });
                int exit = await app.RunAsync(args).ConfigureAwait(false);
                HelmCliRun run = new HelmCliRun();
                run.ExitCode = exit;
                run.Output = console.ToString();
                run.StandardOutput = stdout.ToString();
                return run;
            }
            finally
            {
                Console.SetOut(previousOut);
                AnsiConsole.Console = previousConsole;
                foreach (KeyValuePair<string, string?> pair in saved) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        #endregion
    }
}
