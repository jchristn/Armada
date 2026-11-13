namespace Test.Shared.Infrastructure
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;

    /// <summary>
    /// A mission ready to land with real git: an origin, the vessel's bare repository (configured as Armada configures
    /// it, with remote-tracking fetches), a developer checkout on main, a mission dock worktree holding one commit on the
    /// mission branch, and the vessel, dock and WorkProduced mission rows. Exposes the real landing handler and dock
    /// service over that state.
    /// </summary>
    public sealed class RealLandingFixture
    {
        #region Public-Members

        /// <summary>
        /// The repositories.
        /// </summary>
        public MergeQueueRepoSet Repos { get; private set; } = null!;

        /// <summary>
        /// Real git service.
        /// </summary>
        public GitService Git { get; private set; } = null!;

        /// <summary>
        /// Settings.
        /// </summary>
        public ArmadaSettings Settings { get; private set; } = null!;

        /// <summary>
        /// Logging.
        /// </summary>
        public LoggingModule Logging { get; private set; } = null!;

        /// <summary>
        /// Dock service.
        /// </summary>
        public DockService Docks { get; private set; } = null!;

        /// <summary>
        /// Landing handler.
        /// </summary>
        public MissionLandingHandler Handler { get; private set; } = null!;

        /// <summary>
        /// Vessel row.
        /// </summary>
        public Vessel Vessel { get; private set; } = null!;

        /// <summary>
        /// Dock row.
        /// </summary>
        public Dock Dock { get; private set; } = null!;

        /// <summary>
        /// Mission row (WorkProduced).
        /// </summary>
        public Mission Mission { get; private set; } = null!;

        /// <summary>
        /// The mission's commit.
        /// </summary>
        public string WorkCommit { get; private set; } = "";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the fixture.
        /// </summary>
        /// <param name="db">Database.</param>
        /// <param name="mode">Landing mode.</param>
        /// <param name="file">File the mission commit adds or changes.</param>
        /// <param name="content">Its content.</param>
        /// <returns>The fixture.</returns>
        public static async Task<RealLandingFixture> CreateAsync(DatabaseDriver db, LandingModeEnum mode, string file = "feature.txt", string content = "feature\n")
        {
            RealLandingFixture f = new RealLandingFixture();
            f.Logging = new LoggingModule();
            f.Logging.Settings.EnableConsole = false;
            f.Repos = MergeQueueRepoSet.Create();
            await HarborDockGitFixture.GitAsync(f.Repos.VesselRepo, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*").ConfigureAwait(false);
            await HarborDockGitFixture.GitAsync(f.Repos.VesselRepo, "fetch", "-q", "origin").ConfigureAwait(false);

            string suffix = Guid.NewGuid().ToString("N").Substring(0, 10);
            string branch = "armada/real/" + suffix;
            f.Repos.PushBranch(branch, file, content);
            await HarborDockGitFixture.GitAsync(f.Repos.Work, "checkout", "-q", "-B", "main", "origin/main").ConfigureAwait(false);

            f.Git = new GitService(f.Logging);
            string dockPath = TestTemp.NewDirectory("real_landing") + "_dock";
            await f.Git.CreateWorktreeAsync(f.Repos.VesselRepo, dockPath, branch).ConfigureAwait(false);
            f.WorkCommit = (await f.Git.GetHeadCommitHashAsync(dockPath).ConfigureAwait(false))!;

            f.Settings = new ArmadaSettings();
            f.Settings.DocksDirectory = TestTemp.NewDirectory("real_landing_docks");
            f.Settings.ReposDirectory = TestTemp.NewDirectory("real_landing_repos");
            f.Settings.LogDirectory = TestTemp.NewDirectory("real_landing_logs");

            Vessel vessel = new Vessel("real-landing-" + suffix, f.Repos.Origin);
            vessel.LocalPath = f.Repos.VesselRepo;
            vessel.WorkingDirectory = f.Repos.Work;
            vessel.DefaultBranch = "main";
            vessel.LandingMode = mode;
            vessel.BranchCleanupPolicy = BranchCleanupPolicyEnum.LocalOnly;
            f.Vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Dock dock = new Dock(f.Vessel.Id);
            dock.WorktreePath = dockPath;
            dock.BranchName = branch;
            dock.Active = true;
            f.Dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);

            Mission mission = new Mission("Real landing " + suffix);
            mission.Status = MissionStatusEnum.WorkProduced;
            mission.VesselId = f.Vessel.Id;
            mission.DockId = f.Dock.Id;
            mission.BranchName = branch;
            mission.CommitHash = f.WorkCommit;
            mission.DiffSnapshot = "diff --git a/" + file + " b/" + file;
            f.Mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);

            f.Docks = new DockService(f.Logging, db, f.Settings, f.Git);
            f.Handler = new MissionLandingHandler(f.Logging, db, f.Settings, f.Git, new RecordingMergeQueueService(),
                new MessageTemplateService(f.Logging), null, f.Docks, null);
            return f;
        }

        #endregion
    }
}
