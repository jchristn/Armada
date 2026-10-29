namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Core.Services;
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
        #region Protected-Members

        /// <summary>
        /// The hint printed on stderr after a 404 (what to list to find a valid id).
        /// </summary>
        protected virtual string NotFoundHint
        {
            get { return "No black box entry was found. List entries with: armada blackbox list"; }
        }

        #endregion

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

        /// <summary>
        /// Resolve a vessel name or id to its id: an id (vsl_ prefix) is read directly, anything else is matched against
        /// the vessel list by name or id.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="nameOrId">Vessel name or id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The vessel, or null when none matches (a message is on stderr).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown when the Admiral call fails (a 404 for an unknown id).</exception>
        protected static async Task<Vessel?> ResolveVesselAsync(ArmadaClient client, string nameOrId, CancellationToken token)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            string wanted = (nameOrId ?? "").Trim();
            if (wanted.Length == 0)
            {
                Status("Error: a vessel name or id is required.");
                return null;
            }

            if (wanted.StartsWith("vsl_", StringComparison.Ordinal)) return await client.GetVesselAsync(wanted, token).ConfigureAwait(false);
            EnumerationResult<Vessel>? list = await client.ListVesselsAsync(new ArmadaPageQuery(1, 9999), token).ConfigureAwait(false);
            Vessel? match = EntityResolver.ResolveVessel(list?.Objects ?? new List<Vessel>(), wanted);
            if (match == null) Status("Error: no vessel named or with id '" + wanted + "'. List vessels with: armada vessel list");
            return match;
        }

        /// <summary>
        /// Read a mission brief from <c>--brief</c> text or a <c>--brief-file</c> path (<c>-</c> reads stdin).
        /// </summary>
        /// <param name="text">The --brief value, or null.</param>
        /// <param name="file">The --brief-file value, or null.</param>
        /// <returns>The brief, or null when neither was given or the file could not be read (a message is on stderr).</returns>
        protected static string? ReadBrief(string? text, string? file)
        {
            if (!String.IsNullOrEmpty(text) && !String.IsNullOrEmpty(file))
            {
                Status("Error: pass --brief or --brief-file, not both.");
                return null;
            }

            if (!String.IsNullOrEmpty(text)) return text;
            if (String.IsNullOrEmpty(file))
            {
                Status("Error: a brief is required: --brief \"<text>\" or --brief-file <path> (- reads stdin).");
                return null;
            }

            try
            {
                if (file == "-") return Console.In.ReadToEnd();
                return File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Status("Error: the brief file could not be read: " + ex.Message);
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private int Fail(ArmadaApiException ex)
        {
            string code = String.IsNullOrEmpty(ex.Code) ? "" : " " + ex.Code;
            string status = ex.StatusCode > 0 ? "HTTP " + ex.StatusCode + code : "Request failed" + code;
            Console.Error.WriteLine("Error: " + status + ": " + ex.Message);
            if (ex.StatusCode == 404)
            {
                Console.Error.WriteLine(NotFoundHint);
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
