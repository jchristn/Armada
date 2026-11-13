namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Owns each pack's fixture repository and fixture vessel. The fixture is a local bare repository (the vessel's
    /// remote) built from the pack's fixture folder. Before each task the fixture is reset: a new commit on the default
    /// branch whose tree is exactly the pack's fixture plus the task's setup files. Resets only move forward, so every
    /// clone of the fixture picks them up with a plain fetch.
    /// </summary>
    public class BatteryFixtureService
    {
        #region Public-Members

        /// <summary>
        /// Name of the built-in pipeline battery missions run under.
        /// </summary>
        public const string PipelineName = "ModelEvaluation";

        /// <summary>
        /// Prefix of fixture vessel names (followed by the pack identifier).
        /// </summary>
        public const string VesselNamePrefix = "battery-";

        #endregion

        #region Private-Members

        private readonly string _Header = "[BatteryFixtureService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly string _Root;
        private readonly IHostCommandExecutor _Commands;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _Locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="root">Directory that holds the fixture repositories (for example DATA/battery).</param>
        /// <param name="commands">Runs git on the Admiral's machine, or null for the local executor.</param>
        /// <exception cref="ArgumentNullException">Thrown when database, logging or root is null.</exception>
        public BatteryFixtureService(DatabaseDriver database, LoggingModule logging, string root, IHostCommandExecutor? commands = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentNullException(nameof(root));
            _Root = Path.GetFullPath(root);
            _Commands = commands ?? new LocalHostCommandExecutor();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Name of a pack's fixture vessel in a tenant. Vessel names are unique across tenants, so every tenant but the
        /// default one gets its tenant identifier appended.
        /// </summary>
        /// <param name="packId">Pack identifier.</param>
        /// <param name="tenantId">Tenant, or null for the default tenant.</param>
        /// <returns>Vessel name.</returns>
        public static string VesselNameFor(string packId, string? tenantId = null)
        {
            if (String.IsNullOrEmpty(tenantId) || String.Equals(tenantId, Constants.DefaultTenantId, StringComparison.Ordinal))
                return VesselNamePrefix + packId;
            return VesselNamePrefix + packId + "-" + tenantId;
        }

        /// <summary>
        /// Path of a pack's fixture repository (a bare repository; the fixture vessel's remote).
        /// </summary>
        /// <param name="packId">Pack identifier.</param>
        /// <returns>Path.</returns>
        public string OriginPathFor(string packId)
        {
            return Path.Combine(_Root, packId, "origin.git");
        }

        /// <summary>
        /// Hold a pack's fixture for the duration of one task, so two runs never reset it under each other.
        /// </summary>
        /// <param name="packId">Pack identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A lease; dispose it to release the fixture.</returns>
        public async Task<IDisposable> LockAsync(string packId, CancellationToken token = default)
        {
            SemaphoreSlim gate = _Locks.GetOrAdd(_Root + "|" + packId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            return new Lease(gate);
        }

        /// <summary>
        /// Create the pack's fixture repository when it does not exist yet.
        /// </summary>
        /// <param name="pack">Pack.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The repository path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when pack is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when git fails.</exception>
        public async Task<string> EnsureRepositoryAsync(BatteryPack pack, CancellationToken token = default)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            string origin = OriginPathFor(pack.Id);
            string work = WorkPathFor(pack.Id);
            string branch = BranchOf(pack);
            if (!Directory.Exists(origin))
            {
                Directory.CreateDirectory(origin);
                await GitAsync(origin, token, "init", "--bare", "--initial-branch=" + branch).ConfigureAwait(false);
                _Logging.Info(_Header + "created fixture repository " + origin);
            }

            if (!Directory.Exists(Path.Combine(work, ".git")))
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                await GitAsync(work, token, "init", "--initial-branch=" + branch).ConfigureAwait(false);
                await GitAsync(work, token, "remote", "add", "origin", origin).ConfigureAwait(false);
            }

            return origin;
        }

        /// <summary>
        /// Reset the fixture for a task: commit the pack's fixture plus the task's setup files on the default branch and
        /// push it to the fixture repository.
        /// </summary>
        /// <param name="pack">Pack.</param>
        /// <param name="task">Task, or null for the plain fixture.</param>
        /// <param name="label">Label for the commit message (for example the run and attempt).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The commit the task starts from.</returns>
        /// <exception cref="ArgumentNullException">Thrown when pack is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the pack has no manifest or git fails.</exception>
        public async Task<string> ResetAsync(BatteryPack pack, BatteryTaskDefinition? task, string? label, CancellationToken token = default)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (pack.Manifest?.Fixture == null) throw new InvalidOperationException("Pack " + pack.Id + " has no fixture.");
            await EnsureRepositoryAsync(pack, token).ConfigureAwait(false);
            string work = WorkPathFor(pack.Id);
            string branch = BranchOf(pack);

            // Start from whatever the fixture repository holds, so the reset is a fast-forward.
            await GitAsync(work, token, "fetch", "origin").ConfigureAwait(false);
            HostCommandResult remote = await RunGitAsync(work, token, "rev-parse", "--verify", "--quiet", "refs/remotes/origin/" + branch).ConfigureAwait(false);
            if (remote.Success) await GitAsync(work, token, "checkout", "-B", branch, "origin/" + branch).ConfigureAwait(false);
            else await GitAsync(work, token, "symbolic-ref", "HEAD", "refs/heads/" + branch).ConfigureAwait(false);

            foreach (string entry in Directory.GetFileSystemEntries(work))
            {
                if (String.Equals(Path.GetFileName(entry), ".git", StringComparison.Ordinal)) continue;
                if (Directory.Exists(entry)) Directory.Delete(entry, true);
                else File.Delete(entry);
            }

            CopyTree(pack.Resolve(pack.Manifest.Fixture.Path), work);
            if (task != null && !String.IsNullOrWhiteSpace(task.Setup)) CopyTree(pack.Resolve(task.Setup), work);

            await GitAsync(work, token, "add", "--all").ConfigureAwait(false);
            string message = "Battery reset: " + pack.Id + " v" + pack.Manifest.Version + (task != null ? ", task " + task.Id : String.Empty)
                + (String.IsNullOrWhiteSpace(label) ? String.Empty : " (" + label + ")");
            await GitAsync(work, token, "-c", "user.name=Armada Battery", "-c", "user.email=battery@armada.invalid", "-c", "commit.gpgsign=false",
                "commit", "--allow-empty", "--quiet", "-m", message).ConfigureAwait(false);
            await GitAsync(work, token, "push", "--quiet", "origin", "HEAD:refs/heads/" + branch).ConfigureAwait(false);
            HostCommandResult head = await GitAsync(work, token, "rev-parse", "HEAD").ConfigureAwait(false);
            return head.StandardOutput.Trim();
        }

        /// <summary>
        /// Find a pack's fixture vessel in a tenant.
        /// </summary>
        /// <param name="pack">Pack.</param>
        /// <param name="tenantId">Tenant, or null for the default tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The vessel, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when pack is null.</exception>
        public async Task<Vessel?> FindVesselAsync(BatteryPack pack, string? tenantId, CancellationToken token = default)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            string tenant = tenantId ?? Constants.DefaultTenantId;
            return await _Database.Vessels.ReadByNameAsync(tenant, VesselNameFor(pack.Id, tenant), token).ConfigureAwait(false);
        }

        /// <summary>
        /// The pack's fixture vessel, created (with its fixture repository) when asked and missing.
        /// </summary>
        /// <param name="pack">Pack.</param>
        /// <param name="tenantId">Tenant, or null for the default tenant.</param>
        /// <param name="userId">Owner of a created vessel.</param>
        /// <param name="create">Whether to create a missing vessel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The vessel.</returns>
        /// <exception cref="ArgumentNullException">Thrown when pack is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the vessel is missing and create is false.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the ModelEvaluation pipeline is missing.</exception>
        public async Task<Vessel> EnsureVesselAsync(BatteryPack pack, string? tenantId, string? userId, bool create, CancellationToken token = default)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            Vessel? existing = await FindVesselAsync(pack, tenantId, token).ConfigureAwait(false);
            if (existing != null) return existing;
            if (!create)
                throw new ArgumentException("Pack " + pack.Id + " has no fixture vessel (" + VesselNameFor(pack.Id, tenantId) + ") yet; pass createFixtureVessel to create it.");

            Pipeline pipeline = await _Database.Pipelines.ReadByNameAsync(PipelineName, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The built-in " + PipelineName + " pipeline is missing.");
            string origin = await EnsureRepositoryAsync(pack, token).ConfigureAwait(false);
            await ResetAsync(pack, null, "fixture vessel created", token).ConfigureAwait(false);

            Vessel vessel = new Vessel(VesselNameFor(pack.Id, tenantId), origin)
            {
                TenantId = tenantId ?? Constants.DefaultTenantId,
                UserId = userId,
                DefaultBranch = BranchOf(pack),
                DefaultPipelineId = pipeline.Id,
                LandingMode = LandingModeEnum.None,
                ProjectContext = "Fixture vessel of battery pack " + pack.Id + " (docs/MODEL_HETEROGENEITY.md). Missions here are evaluation work; "
                    + "the fixture is reset before each task. " + (pack.Manifest?.Description ?? String.Empty)
            };
            if (!String.IsNullOrWhiteSpace(pack.Manifest?.StageChecks?.BuildCommand) || !String.IsNullOrWhiteSpace(pack.Manifest?.StageChecks?.TestCommand))
            {
                vessel.DefinitionOfDoneBuildCommand = pack.Manifest!.StageChecks!.BuildCommand;
                vessel.DefinitionOfDoneTestCommand = pack.Manifest.StageChecks.TestCommand;
            }

            vessel = await _Database.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created fixture vessel " + vessel.Id + " for pack " + pack.Id);
            return vessel;
        }

        #endregion

        #region Private-Methods

        private string WorkPathFor(string packId)
        {
            return Path.Combine(_Root, packId, "work");
        }

        private static string BranchOf(BatteryPack pack)
        {
            string? branch = pack.Manifest?.Fixture?.DefaultBranch;
            return String.IsNullOrWhiteSpace(branch) ? "main" : branch;
        }

        private static void CopyTree(string source, string destination)
        {
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(s => s == "bin" || s == "obj" || s == ".git")) continue;
                string target = Path.Combine(destination, relative);
                string? folder = Path.GetDirectoryName(target);
                if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.Copy(file, target, true);
            }
        }

        private async Task<HostCommandResult> GitAsync(string directory, CancellationToken token, params string[] arguments)
        {
            HostCommandResult result = await RunGitAsync(directory, token, arguments).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException("git " + String.Join(" ", arguments) + " failed in " + directory + ": " + (result.StandardError + result.StandardOutput).Trim());
            return result;
        }

        private async Task<HostCommandResult> RunGitAsync(string directory, CancellationToken token, params string[] arguments)
        {
            HostCommandRequest request = new HostCommandRequest
            {
                Executable = "git",
                Arguments = new List<string>(arguments),
                WorkingDirectory = directory,
                TimeoutMs = 120000
            };
            return await _Commands.RunAsync(request, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Classes

        private sealed class Lease : IDisposable
        {
            private SemaphoreSlim? _Gate;

            public Lease(SemaphoreSlim gate)
            {
                _Gate = gate;
            }

            public void Dispose()
            {
                SemaphoreSlim? gate = Interlocked.Exchange(ref _Gate, null);
                gate?.Release();
            }
        }

        #endregion
    }
}
