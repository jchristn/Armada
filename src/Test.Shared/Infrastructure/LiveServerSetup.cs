namespace Test.Shared.Infrastructure
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Setup helpers for tests against a live in-process server (<see cref="E2EServerFixture"/>) through
    /// <see cref="ArmadaClient"/>: an admin client, vessels backed by a local bare origin and a working checkout (so
    /// MergeAndPush landing runs for real), captains, a deployment pending approval, git queries, and a pump-friendly poll.
    /// </summary>
    public static class LiveServerSetup
    {
        #region Public-Methods

        /// <summary>
        /// Client authenticated with the fixture's admin API key.
        /// </summary>
        /// <param name="fx">Fixture.</param>
        /// <returns>Client (dispose it).</returns>
        public static ArmadaClient Admin(E2EServerFixture fx)
        {
            if (fx == null) throw new ArgumentNullException(nameof(fx));
            ArmadaClientOptions options = new ArmadaClientOptions(fx.BaseUrl);
            options.ApiKey = fx.ApiKey;
            return new ArmadaClient(options);
        }

        /// <summary>
        /// Create a fleet and a vessel whose origin is a fresh local bare repository (branch main) with a working
        /// checkout, landing with MergeAndPush (merge into the checkout, then push to the origin).
        /// </summary>
        /// <param name="admin">Admin client.</param>
        /// <param name="label">Name prefix.</param>
        /// <returns>The setup.</returns>
        public static async Task<VesselSetup> CreateVesselAsync(ArmadaClient admin, string label)
        {
            if (admin == null) throw new ArgumentNullException(nameof(admin));
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
            Fleet fleet = (await admin.CreateFleetAsync(new Fleet { Name = label + "-fleet-" + suffix }).ConfigureAwait(false))!;
            string bare = TestGitRepoHelper.CreateBareRepoCopy();
            string working = Path.Combine(TestTemp.NewDirectory("e2e-checkout"), "work");
            int clone = Git(null, out string cloneOutput, "clone", bare, working);
            if (clone != 0) throw new InvalidOperationException("git clone failed: " + cloneOutput);

            Vessel vessel = new Vessel();
            vessel.Name = label + "-vessel-" + suffix;
            vessel.FleetId = fleet.Id;
            vessel.RepoUrl = bare;
            vessel.DefaultBranch = "main";
            vessel.LandingMode = LandingModeEnum.MergeAndPush;
            vessel.WorkingDirectory = working;
            Vessel created = (await admin.CreateVesselAsync(vessel).ConfigureAwait(false))!;

            VesselSetup setup = new VesselSetup();
            setup.Fleet = fleet;
            setup.Vessel = created;
            setup.BarePath = bare;
            return setup;
        }

        /// <summary>
        /// Create a Claude Code captain (served by <see cref="StubCaptainRuntime"/> once installed).
        /// </summary>
        /// <param name="admin">Admin client.</param>
        /// <param name="name">Name prefix.</param>
        /// <returns>The captain.</returns>
        public static async Task<Captain> CreateCaptainAsync(ArmadaClient admin, string name)
        {
            if (admin == null) throw new ArgumentNullException(nameof(admin));
            Captain captain = new Captain();
            captain.Name = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
            captain.Runtime = AgentRuntimeEnum.ClaudeCode;
            return (await admin.CreateCaptainAsync(captain).ConfigureAwait(false))!;
        }

        /// <summary>
        /// Create a vessel workflow profile with echo commands for a "production" environment that requires approval,
        /// the environment, and an auto-executing deployment to it, which waits in PendingApproval.
        /// </summary>
        /// <param name="admin">Admin client.</param>
        /// <param name="vesselId">Vessel.</param>
        /// <param name="title">Deployment title.</param>
        /// <returns>The pending deployment.</returns>
        public static async Task<Deployment> CreatePendingDeploymentAsync(ArmadaClient admin, string vesselId, string title)
        {
            if (admin == null) throw new ArgumentNullException(nameof(admin));
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
            WorkflowProfile profile = new WorkflowProfile();
            profile.Name = "e2e-workflow-" + suffix;
            profile.Scope = WorkflowProfileScopeEnum.Vessel;
            profile.VesselId = vesselId;
            profile.IsDefault = true;
            WorkflowEnvironmentProfile production = new WorkflowEnvironmentProfile();
            production.EnvironmentName = "production";
            production.DeployCommand = "echo deploy-production";
            production.SmokeTestCommand = "echo smoke-production";
            production.DeploymentVerificationCommand = "echo verify-production";
            production.RollbackCommand = "echo rollback-production";
            production.RollbackVerificationCommand = "echo rollback-verify-production";
            profile.Environments.Add(production);
            WorkflowProfile createdProfile = (await admin.CreateWorkflowProfileAsync(profile).ConfigureAwait(false))!;

            DeploymentEnvironmentUpsertRequest env = new DeploymentEnvironmentUpsertRequest();
            env.VesselId = vesselId;
            env.Name = "production";
            env.Kind = EnvironmentKindEnum.Production;
            env.RequiresApproval = true;
            env.Active = true;
            DeploymentEnvironment environment = (await admin.CreateEnvironmentAsync(env).ConfigureAwait(false))!;

            DeploymentUpsertRequest request = new DeploymentUpsertRequest();
            request.VesselId = vesselId;
            request.WorkflowProfileId = createdProfile.Id;
            request.EnvironmentId = environment.Id;
            request.Title = title;
            request.AutoExecute = true;
            return (await admin.CreateDeploymentAsync(request).ConfigureAwait(false))!;
        }

        /// <summary>
        /// Whether the origin's main branch has a commit by the stub captain.
        /// </summary>
        /// <param name="barePath">Bare repository.</param>
        /// <returns>True when found.</returns>
        public static bool OriginHasStubCommit(string barePath)
        {
            Git(null, out string output, "--git-dir", barePath, "log", "main", "--name-only", "--format=%s");
            return output.Contains("stub-captain-", StringComparison.Ordinal);
        }

        /// <summary>
        /// Run git and capture its combined output.
        /// </summary>
        /// <param name="workingDirectory">Working directory, or null for the current one.</param>
        /// <param name="output">Standard output followed by standard error.</param>
        /// <param name="args">Arguments.</param>
        /// <returns>Exit code.</returns>
        public static int Git(string? workingDirectory, out string output, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git");
            if (workingDirectory != null) info.WorkingDirectory = workingDirectory;
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            using (Process process = Process.Start(info)!)
            {
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(true); }
                    catch (InvalidOperationException) { }
                    return -1;
                }

                output += stderr.Result;
                return process.ExitCode;
            }
        }

        /// <summary>
        /// Pump the TUI while polling the server (at most every 250 ms, one request in flight) until the check passes.
        /// </summary>
        /// <param name="host">TUI host.</param>
        /// <param name="check">Server check.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>True when the check passed in time.</returns>
        public static bool PumpUntilServer(TuiTestHost host, Func<Task<bool>> check, int timeoutMs)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (check == null) throw new ArgumentNullException(nameof(check));
            Stopwatch poll = Stopwatch.StartNew();
            Task<bool>? pending = null;
            bool done = false;
            host.PumpUntil(() =>
            {
                if (pending != null && pending.IsCompleted)
                {
                    done = pending.Status == TaskStatus.RanToCompletion && pending.Result;
                    pending = null;
                    poll.Restart();
                }

                if (!done && pending == null && poll.ElapsedMilliseconds >= 250) pending = check();
                return done;
            }, timeoutMs);
            return done;
        }

        /// <summary>
        /// Poll the server (every 250 ms) until the check passes, without a TUI.
        /// </summary>
        /// <param name="check">Server check.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>True when the check passed in time.</returns>
        public static async Task<bool> WaitUntilAsync(Func<Task<bool>> check, int timeoutMs)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
            while (!deadline.Passed)
            {
                if (await check().ConfigureAwait(false)) return true;
                await Task.Delay(250).ConfigureAwait(false);
            }

            return await check().ConfigureAwait(false);
        }

        #endregion
    }
}
