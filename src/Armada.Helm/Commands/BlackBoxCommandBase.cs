namespace Armada.Helm.Commands
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Helm.Infrastructure;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// Base for the <c>armada blackbox</c> commands: talks to the target Admiral through the typed
    /// <see cref="ArmadaClient"/> with the target's credential, and turns API failures into a message on stderr and an
    /// exit code (404 is <see cref="BlackBoxCli.NotFoundExitCode"/>, 401 and 403 are the target error code, anything else
    /// 1). In pure-output mode (<c>--brief</c>, <c>--json</c>, <c>cat</c>, a bundle to stdout) every status line goes to
    /// stderr so stdout holds only the payload.
    /// </summary>
    /// <typeparam name="TSettings">Settings.</typeparam>
    public abstract class BlackBoxCommandBase<TSettings> : BaseCommand<TSettings> where TSettings : CommandSettings
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Run a black box call against the target with error mapping.
        /// </summary>
        /// <param name="quiet">Pure-output mode: start-up and status lines go to stderr.</param>
        /// <param name="body">The call; returns the exit code.</param>
        /// <returns>Exit code.</returns>
        protected async Task<int> RunBlackBoxAsync(bool quiet, Func<ArmadaClient, Task<int>> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            IAnsiConsole previous = AnsiConsole.Console;
            if (quiet) AnsiConsole.Console = StderrConsole();
            try
            {
                await EnsureServerAsync().ConfigureAwait(false);
            }
            finally
            {
                if (quiet) AnsiConsole.Console = previous;
            }

            AdmiralTarget target = GetTarget();
            ArmadaClientOptions options = new ArmadaClientOptions(target.BaseUrl);
            // The same headers as AdmiralTarget.ApplyCredentials: a token as Bearer, X-Token, and X-Api-Key; else the local key.
            if (!String.IsNullOrEmpty(target.Token))
            {
                options.BearerToken = target.Token;
                options.Token = target.Token;
                options.ApiKey = target.Token;
            }
            else if (!String.IsNullOrEmpty(target.ApiKey))
            {
                options.ApiKey = target.ApiKey;
            }

            options.UserAgent = "Armada.Helm";
            options.TimeoutMs = 120000;
            using (ArmadaClient client = new ArmadaClient(options))
            {
                try
                {
                    return await body(client).ConfigureAwait(false);
                }
                catch (ArmadaApiException ex)
                {
                    return Fail(ex);
                }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine("Error: " + ex.Message);
                    return 1;
                }
            }
        }

        /// <summary>
        /// Write a status line to stderr (never stdout), so piped output stays clean.
        /// </summary>
        /// <param name="text">Text.</param>
        protected static void Status(string text)
        {
            Console.Error.WriteLine(text);
        }

        /// <summary>
        /// Write a value as indented JSON to stdout (enums as names).
        /// </summary>
        /// <param name="value">Value.</param>
        protected static void WritePureJson(object? value)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(value, _Json));
        }

        /// <summary>
        /// Ask a yes/no question when the console is interactive.
        /// </summary>
        /// <param name="question">Question.</param>
        /// <param name="yes">The --yes option.</param>
        /// <returns>True to go ahead; false when declined or when not interactive without --yes.</returns>
        protected static bool Confirmed(string question, bool yes)
        {
            if (yes) return true;
            if (!AnsiConsole.Profile.Capabilities.Interactive) return false;
            return AnsiConsole.Confirm(question, false);
        }

        #endregion

        #region Private-Methods

        private static int Fail(ArmadaApiException ex)
        {
            string code = String.IsNullOrEmpty(ex.Code) ? "" : " " + ex.Code;
            string status = ex.StatusCode > 0 ? "HTTP " + ex.StatusCode + code : "Request failed" + code;
            Console.Error.WriteLine("Error: " + status + ": " + ex.Message);
            if (ex.StatusCode == 404)
            {
                Console.Error.WriteLine("No black box entry was found. List entries with: armada blackbox list");
                return BlackBoxCli.NotFoundExitCode;
            }

            if (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                Console.Error.WriteLine(ex.StatusCode == 401
                    ? "The Admiral requires a valid credential: pass --token <bearer>, set " + AdmiralTargetResolver.TokenEnvironmentVariable + ", or store one with 'armada profile add'."
                    : "The credential is valid but its user may not do this.");
                return HelmErrorHandler.TargetErrorExitCode;
            }

            return 1;
        }

        private static IAnsiConsole StderrConsole()
        {
            return AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Detect,
                ColorSystem = ColorSystemSupport.Detect,
                Out = new AnsiConsoleOutput(Console.Error)
            });
        }

        #endregion
    }
}
