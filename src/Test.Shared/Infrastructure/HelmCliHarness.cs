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
    /// capturing the Spectre console (no colors, not interactive), standard output, and standard error. Host independent: the
    /// ARMADA_SERVER_URL, ARMADA_URL, and ARMADA_TOKEN variables are cleared for the run. The runner executes cases
    /// one at a time, so swapping the process-wide console and environment for the call is safe. Parse errors are rendered
    /// by <see cref="HelmErrorHandler"/> and returned as a non-zero exit, as in the shipped CLI.
    /// </summary>
    public static class HelmCliHarness
    {
        #region Public-Methods

        /// <summary>
        /// Run the CLI and capture its output.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>The run.</returns>
        public static Task<HelmCliRun> RunCapturedAsync(params string[] args) => RunAsync(false, args);

        /// <summary>
        /// Run the CLI and capture its output, rendering command failures through <see cref="HelmErrorHandler"/> the way the
        /// shipped CLI does (stderr, non-zero exit) instead of propagating them to the test.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>The run.</returns>
        public static Task<HelmCliRun> RunHandledAsync(params string[] args) => RunAsync(true, args);

        #endregion

        #region Private-Methods

        private static async Task<HelmCliRun> RunAsync(bool handleFailures, string[] args)
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
            TextWriter previousError = Console.Error;
            StringWriter console = new StringWriter();
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(console)
            });
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                CommandApp app = new CommandApp(new TypeRegistrar());
                app.Configure(config =>
                {
                    Program.ConfigureCommands(config);
                    config.PropagateExceptions();
                });
                int exit;
                try
                {
                    exit = await app.RunAsync(args).ConfigureAwait(false);
                }
                catch (CommandParseException parse)
                {
                    // Parse errors (strict parsing, unknown commands) go through the production handler so the
                    // captured run matches what a user sees; command failures still propagate to the test.
                    exit = HelmErrorHandler.Handle(parse, Console.Error);
                }
                catch (Exception failure) when (handleFailures)
                {
                    exit = HelmErrorHandler.Handle(failure, Console.Error);
                }

                HelmCliRun run = new HelmCliRun();
                run.ExitCode = exit;
                run.Output = console.ToString();
                run.StandardOutput = stdout.ToString();
                run.StandardError = stderr.ToString();
                return run;
            }
            finally
            {
                Console.SetOut(previousOut);
                Console.SetError(previousError);
                AnsiConsole.Console = previousConsole;
                foreach (KeyValuePair<string, string?> pair in saved) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        #endregion
    }
}
