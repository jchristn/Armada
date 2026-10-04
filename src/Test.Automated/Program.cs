namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Console/CLI runner for the shared Armada Touchstone test descriptors. Runs every discovered suite via
    /// the Touchstone console runner, printing colored tabular results and returning a non-zero exit code if
    /// any test fails.
    ///
    /// Suites can be narrowed with the ARMADA_TEST_SUITES environment variable (comma-separated suiteId
    /// prefixes, e.g. ARMADA_TEST_SUITES=E2E,Database), or with --suites on the command line.
    ///
    /// The database provider can be selected with --db-type (postgresql | mysql | sqlserver | sqlite) plus
    /// --db-host, --db-port, --db-user, --db-pass, and --db-name. These map onto the ARMADA_TEST_DB_*
    /// environment variables the shared harness reads, so the same suite runs unchanged against a real
    /// server. SQLite (the default) needs no connection arguments.
    ///
    /// <c>--generate-api-surface &lt;dir&gt;</c> runs no tests: it boots a throwaway in-process Admiral and rewrites
    /// api-surface-1.0.json and API_SURFACE_1.0.md in the directory (scripts/common/generate-api-surface.sh).
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point. Accepts <c>--results &lt;path&gt;</c>, <c>--suites &lt;prefixes&gt;</c>, and the
        /// <c>--db-*</c> connection arguments described on the class.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code: 0 when all tests pass, non-zero otherwise.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;
            string? surfaceDirectory = null;

            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i];
                string? next = i + 1 < args.Length ? args[i + 1] : null;

                switch (current)
                {
                    case "--results":
                        if (next != null) resultsPath = next;
                        break;
                    case "--suites":
                        if (next != null) Environment.SetEnvironmentVariable("ARMADA_TEST_SUITES", next);
                        break;
                    case "--db-type":
                        if (next != null) Environment.SetEnvironmentVariable("ARMADA_TEST_DB_TYPE", next);
                        break;
                    case "--db-host":
                        if (next != null) Environment.SetEnvironmentVariable("ARMADA_TEST_DB_HOST", next);
                        break;
                    case "--db-port":
                        if (next != null) Environment.SetEnvironmentVariable("ARMADA_TEST_DB_PORT", next);
                        break;
                    case "--db-user":
                        if (next != null) Environment.SetEnvironmentVariable("ARMADA_TEST_DB_USER", next);
                        break;
                    case "--db-pass":
                        if (next != null) Environment.SetEnvironmentVariable("ARMADA_TEST_DB_PASS", next);
                        break;
                    case "--generate-api-surface":
                        if (next != null) surfaceDirectory = next;
                        break;
                    case "--db-name":
                        if (next != null) Environment.SetEnvironmentVariable("ARMADA_TEST_DB_NAME", next);
                        break;
                    default:
                        break;
                }
            }

            if (surfaceDirectory != null)
            {
                Test.Shared.Infrastructure.ApiSurface.ApiSurfaceDocument surface = await Test.Shared.Infrastructure.ApiSurface.ApiSurfaceFiles.GenerateAsync(surfaceDirectory).ConfigureAwait(false);
                Console.WriteLine("API surface written to " + System.IO.Path.GetFullPath(surfaceDirectory) + ": "
                    + surface.Rest.Count + " REST routes, " + surface.Mcp.Count + " MCP tools, "
                    + surface.WebSocket.Commands.Count + " WebSocket commands, " + surface.WebSocket.Events.Count + " WebSocket events, "
                    + surface.Cli.Count + " CLI commands, " + surface.Settings.Count + " settings keys");
                return 0;
            }

            return await ConsoleRunner.RunAsync(ArmadaTestSuites.All, resultsPath: resultsPath).ConfigureAwait(false);
        }
    }
}
