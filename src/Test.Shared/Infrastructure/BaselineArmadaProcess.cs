namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// An older Armada Admiral (the upgrade baseline) running out of process through the UpgradeBaselineHost that
    /// scripts/common/run-upgrade-test.sh builds from the baseline source. The process gets explicit settings and a
    /// private HOME / USERPROFILE / ARMADA_DATA_DIR under the work directory, so it never touches ~/.armada. Stop it
    /// with <see cref="StopAsync"/>, which asks the host to shut the Admiral down cleanly.
    /// </summary>
    public sealed class BaselineArmadaProcess : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Environment variable holding the path to the built UpgradeBaselineHost.dll.
        /// </summary>
        public const string HostPathEnvVar = "ARMADA_UPGRADE_BASELINE_HOST";

        /// <summary>
        /// Environment variable naming the baseline git ref (for reporting).
        /// </summary>
        public const string BaselineRefEnvVar = "ARMADA_UPGRADE_BASELINE_REF";

        /// <summary>
        /// HTTP client authenticated with the baseline's API key.
        /// </summary>
        public HttpClient Client { get; }

        /// <summary>
        /// REST base URL.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Combined standard output and error, for diagnostics.
        /// </summary>
        public string Output
        {
            get { lock (_OutputLock) return _Output.ToString(); }
        }

        #endregion

        #region Private-Members

        private readonly Process _Process;
        private readonly object _OutputLock = new object();
        private readonly System.Text.StringBuilder _Output = new System.Text.StringBuilder();
        private readonly TaskCompletionSource<bool> _Ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Constructors-and-Factories

        private BaselineArmadaProcess(Process process, string baseUrl, string apiKey)
        {
            _Process = process;
            BaseUrl = baseUrl;
            Client = new HttpClient();
            Client.BaseAddress = new Uri(baseUrl);
            Client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            Client.Timeout = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// Start the baseline Admiral against a database and wait until its REST API answers.
        /// </summary>
        /// <param name="hostDll">Path to UpgradeBaselineHost.dll.</param>
        /// <param name="workDirectory">Work directory; the data directory and private home live under it.</param>
        /// <param name="database">Database the baseline should use.</param>
        /// <returns>The running baseline.</returns>
        public static async Task<BaselineArmadaProcess> StartAsync(string hostDll, string workDirectory, DatabaseSettings database)
        {
            if (String.IsNullOrEmpty(hostDll)) throw new ArgumentNullException(nameof(hostDll));
            if (!File.Exists(hostDll)) throw new FileNotFoundException("Baseline host not found; build it with scripts/common/run-upgrade-test.sh", hostDll);
            if (database == null) throw new ArgumentNullException(nameof(database));

            string home = Path.Combine(workDirectory, "baseline-home");
            string dataDir = Path.Combine(workDirectory, "data");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(dataDir);

            int restPort = await InProcessArmadaServer.ReservePortAsync().ConfigureAwait(false);
            int mcpPort = await InProcessArmadaServer.ReservePortAsync().ConfigureAwait(false);
            string apiKey = "baseline-" + Guid.NewGuid().ToString("N");

            List<string> args = new List<string>
            {
                hostDll,
                "--data-dir", dataDir,
                "--rest-port", restPort.ToString(),
                "--mcp-port", mcpPort.ToString(),
                "--api-key", apiKey
            };

            switch (database.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    args.AddRange(new[] { "--db-type", "sqlite", "--db-file", database.Filename });
                    break;
                default:
                    args.AddRange(new[]
                    {
                        "--db-type", database.Type.ToString().ToLowerInvariant(),
                        "--db-host", database.Hostname,
                        "--db-port", database.Port.ToString(),
                        "--db-user", database.Username,
                        "--db-pass", database.Password,
                        "--db-name", database.DatabaseName
                    });
                    break;
            }

            ProcessStartInfo info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDirectory
            };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.Environment["HOME"] = home;
            info.Environment["USERPROFILE"] = home;
            info.Environment["ARMADA_DATA_DIR"] = Path.Combine(home, ".armada");

            Process process = new Process { StartInfo = info, EnableRaisingEvents = true };
            BaselineArmadaProcess baseline = new BaselineArmadaProcess(process, "http://127.0.0.1:" + restPort, apiKey);
            process.OutputDataReceived += (sender, e) => baseline.OnOutput(e.Data);
            process.ErrorDataReceived += (sender, e) => baseline.OnOutput(e.Data);
            process.Exited += (sender, e) => baseline._Ready.TrySetResult(false);
            if (!process.Start()) throw new InvalidOperationException("Could not start the baseline host.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Task finished = await Task.WhenAny(baseline._Ready.Task, Task.Delay(TimeSpan.FromSeconds(120))).ConfigureAwait(false);
            if (finished != baseline._Ready.Task || !baseline._Ready.Task.Result)
            {
                string output = baseline.Output;
                baseline.Dispose();
                throw new TimeoutException("Baseline Admiral did not start. Output:\n" + output);
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    HttpResponseMessage health = await baseline.Client.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                    if (health.StatusCode == HttpStatusCode.OK) return baseline;
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(200).ConfigureAwait(false);
            }

            string lastOutput = baseline.Output;
            baseline.Dispose();
            throw new TimeoutException("Baseline Admiral REST API did not answer. Output:\n" + lastOutput);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ask the host to stop the Admiral cleanly and wait for the process to exit (killing it after the timeout).
        /// </summary>
        /// <param name="timeout">How long to wait for a clean exit.</param>
        /// <returns>True when the process exited cleanly.</returns>
        public async Task<bool> StopAsync(TimeSpan timeout)
        {
            if (_Process.HasExited) return _Process.ExitCode == 0;
            try
            {
                await _Process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                await _Process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
            }

            using (CancellationTokenSource cts = new CancellationTokenSource(timeout))
            {
                try
                {
                    await _Process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                    return _Process.ExitCode == 0;
                }
                catch (OperationCanceledException)
                {
                    try { _Process.Kill(true); } catch { }
                    return false;
                }
            }
        }

        /// <summary>
        /// Kill the process if it is still running and dispose resources.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (!_Process.HasExited) _Process.Kill(true);
            }
            catch
            {
            }

            try { _Process.Dispose(); } catch { }
            try { Client.Dispose(); } catch { }
        }

        #endregion

        #region Private-Methods

        private void OnOutput(string? line)
        {
            if (line == null) return;
            lock (_OutputLock)
            {
                if (_Output.Length < 200000) _Output.AppendLine(line);
            }

            if (String.Equals(line.Trim(), "READY", StringComparison.Ordinal)) _Ready.TrySetResult(true);
        }

        #endregion
    }
}
