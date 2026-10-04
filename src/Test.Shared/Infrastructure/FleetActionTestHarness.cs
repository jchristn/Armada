namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// A database, settings, stub mission dispatcher, runner, and service wired together for fleet action tests.
    /// Disposing stops the runner before the database is released.
    /// </summary>
    public sealed class FleetActionTestHarness : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Test database.
        /// </summary>
        public TestDatabase Db { get; }

        /// <summary>
        /// Settings shared with the runner (FleetActions values are read live).
        /// </summary>
        public ArmadaSettings Settings { get; }

        /// <summary>
        /// Logging (console disabled).
        /// </summary>
        public LoggingModule Logging { get; }

        /// <summary>
        /// Stub mission dispatcher.
        /// </summary>
        public StubFleetActionMissionDispatcher Dispatcher { get; }

        /// <summary>
        /// Runner under test.
        /// </summary>
        public FleetActionRunner Runner { get; private set; }

        /// <summary>
        /// Service under test.
        /// </summary>
        public FleetActionService Service { get; private set; }

        /// <summary>
        /// Seeder.
        /// </summary>
        public FleetActionSeedService Seeder { get; }

        /// <summary>
        /// Tenant-admin caller in the default tenant.
        /// </summary>
        public AuthContext Admin { get; } = AuthContext.Authenticated(Constants.DefaultTenantId, Constants.DefaultUserId, false, true, "Test");

        /// <summary>
        /// Regular (non-admin) caller in the default tenant.
        /// </summary>
        public AuthContext Regular { get; } = AuthContext.Authenticated(Constants.DefaultTenantId, Constants.DefaultUserId, false, false, "Test");

        #endregion

        #region Private-Members

        private readonly List<string> _TempDirs = new List<string>();

        #endregion

        #region Constructors-and-Factories

        private FleetActionTestHarness(TestDatabase db)
        {
            Db = db;
            Settings = new ArmadaSettings();
            Logging = new LoggingModule();
            Logging.Settings.EnableConsole = false;
            Dispatcher = new StubFleetActionMissionDispatcher();
            Seeder = new FleetActionSeedService(db.Driver, Logging);
            Runner = NewRunner();
            Service = new FleetActionService(db.Driver, Settings, Runner, Seeder, Logging);
        }

        /// <summary>
        /// Create a harness over a fresh test database. The runner is started (with recovery).
        /// </summary>
        /// <returns>Harness.</returns>
        public static async Task<FleetActionTestHarness> CreateAsync()
        {
            TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            FleetActionTestHarness harness = new FleetActionTestHarness(db);
            harness.Runner.GlobalSlotPollMs = 20;
            await harness.Runner.StartAsync().ConfigureAwait(false);
            return harness;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the runner with a fresh instance (simulating an Admiral restart) and start it.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task RestartRunnerAsync()
        {
            Runner.Dispose();
            Runner = NewRunner();
            Runner.GlobalSlotPollMs = 20;
            Service = new FleetActionService(Db.Driver, Settings, Runner, Seeder, Logging);
            await Runner.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Create a vessel in the given tenant (default tenant when null).
        /// </summary>
        /// <param name="name">Vessel name.</param>
        /// <param name="workingDirectory">Working directory, or null.</param>
        /// <param name="tenantId">Tenant, or null for the default tenant.</param>
        /// <returns>Created vessel.</returns>
        public async Task<Vessel> CreateVesselAsync(string name, string? workingDirectory, string? tenantId = null)
        {
            Vessel vessel = new Vessel(name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), "https://example.com/" + name + ".git");
            vessel.TenantId = tenantId ?? Constants.DefaultTenantId;
            vessel.UserId = Constants.DefaultUserId;
            vessel.WorkingDirectory = workingDirectory;
            return await Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a vessel backed by a fresh clean git working copy.
        /// </summary>
        /// <param name="name">Vessel name.</param>
        /// <returns>Created vessel.</returns>
        public async Task<Vessel> CreateRepoVesselAsync(string name)
        {
            string dir = TestGitRepoHelper.CreateWorkingRepoCopy();
            _TempDirs.Add(dir);
            return await CreateVesselAsync(name, dir).ConfigureAwait(false);
        }

        /// <summary>
        /// Start an ad hoc Command run as tenant admin.
        /// </summary>
        /// <param name="command">Command text.</param>
        /// <param name="vesselIds">Targets.</param>
        /// <param name="concurrency">Run concurrency.</param>
        /// <param name="requiresClean">Whether a clean tree is required.</param>
        /// <param name="timeoutSeconds">Timeout in seconds.</param>
        /// <returns>The run.</returns>
        public async Task<FleetActionRun> StartCommandAsync(string command, List<string> vesselIds, int concurrency = 4, bool requiresClean = true, int timeoutSeconds = 120)
        {
            FleetActionRunRequest request = new FleetActionRunRequest
            {
                VesselIds = vesselIds,
                Concurrency = concurrency,
                Definition = new FleetActionUpsertRequest
                {
                    Name = "test command",
                    Kind = FleetActionKindEnum.Command,
                    CommandText = command,
                    RequiresCleanWorkingTree = requiresClean,
                    TimeoutSeconds = timeoutSeconds
                }
            };
            return await Service.StartRunAsync(Admin, null, request).ConfigureAwait(false);
        }

        /// <summary>
        /// Read all targets of a run.
        /// </summary>
        /// <param name="runId">Run ID.</param>
        /// <returns>Targets.</returns>
        public async Task<List<FleetActionRunTarget>> TargetsAsync(string runId)
        {
            return await Db.Driver.FleetActionRunTargets.ReadAllByRunAsync(runId).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose: stop the runner and release the database and temp directories.
        /// </summary>
        public void Dispose()
        {
            Runner.Dispose();
            Db.Dispose();
            foreach (string dir in _TempDirs) TestTemp.TryDelete(dir);
        }

        #endregion

        #region Private-Methods

        private FleetActionRunner NewRunner()
        {
            return new FleetActionRunner(Db.Driver, Settings, Logging, Dispatcher, new LocalHostCommandExecutor(), null);
        }

        #endregion
    }
}
