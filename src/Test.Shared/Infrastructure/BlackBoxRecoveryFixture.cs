namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// A black box runtime over a test database with one analyzed mission entry (on a real vessel and dock) for recovery
    /// and recommendation tests, a recording event sink and a recording action executor.
    /// </summary>
    public sealed class BlackBoxRecoveryFixture : IDisposable
    {
        /// <summary>Test database.</summary>
        public TestDatabase Database { get; private set; } = null!;

        /// <summary>Database driver.</summary>
        public DatabaseDriver Db => Database.Driver;

        /// <summary>Runtime.</summary>
        public BlackBoxRuntime Runtime { get; private set; } = null!;

        /// <summary>Service.</summary>
        public BlackBoxService Service => Runtime.Service;

        /// <summary>Event sink.</summary>
        public RecordingBlackBoxEventSink Sink { get; } = new RecordingBlackBoxEventSink();

        /// <summary>Action executor.</summary>
        public RecordingBlackBoxActionExecutor Executor { get; } = new RecordingBlackBoxActionExecutor();

        /// <summary>A tenant admin of the entry's tenant.</summary>
        public AuthContext Admin { get; } = BlackBoxTestKit.User("ten_bb", "usr_bb_admin", true);

        /// <summary>Entry id.</summary>
        public string EntryId { get; private set; } = "";

        /// <summary>Mission id.</summary>
        public string MissionId { get; private set; } = "";

        /// <summary>Vessel id.</summary>
        public string VesselId { get; private set; } = "";

        /// <summary>Dock id.</summary>
        public string DockId { get; private set; } = "";

        private BlackBoxRecoveryFixture()
        {
        }

        /// <summary>Create the fixture.</summary>
        /// <param name="step">Failure step.</param>
        /// <param name="kind">Failure kind.</param>
        /// <returns>The fixture.</returns>
        public static async Task<BlackBoxRecoveryFixture> CreateAsync(BlackBoxStepEnum step = BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum kind = MissionFailureKindEnum.Infra)
        {
            BlackBoxRecoveryFixture f = new BlackBoxRecoveryFixture();
            f.Database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            f.Runtime = BlackBoxTestKit.Runtime(f.Db, null, f.Sink);
            f.Service.ActionExecutor = f.Executor;
            Vessel vessel = await f.Db.Vessels.CreateAsync(new Vessel("bb-recovery-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.invalid/recovery.git")).ConfigureAwait(false);
            Dock dock = await f.Db.Docks.CreateAsync(new Dock(vessel.Id)).ConfigureAwait(false);
            MissionStatusEnum status = step == BlackBoxStepEnum.DirtyCheckout ? MissionStatusEnum.LandingFailed : MissionStatusEnum.Failed;
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(f.Db, null, null, kind, "Recovery fixture failure at " + step, status).ConfigureAwait(false);
            mission.VesselId = vessel.Id;
            mission.DockId = dock.Id;
            mission = await f.Db.Missions.UpdateAsync(mission).ConfigureAwait(false);
            mission.TenantId = "ten_bb";
            mission.UserId = "usr_bb";
            string? id = await f.Runtime.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, step, c =>
            {
                if (step == BlackBoxStepEnum.DirtyCheckout)
                {
                    c.GitFacts = new BlackBoxGitFacts { CheckoutPath = "/work/checkout" };
                    c.GitFacts.DirtyPaths.AddRange(new[] { "src/a.cs", "src/b.cs" });
                }
            }).ConfigureAwait(false);
            f.EntryId = id ?? throw new InvalidOperationException("capture failed");
            await f.Runtime.Pipeline.ProcessAsync(f.EntryId).ConfigureAwait(false);
            f.MissionId = mission.Id;
            f.VesselId = vessel.Id;
            f.DockId = dock.Id;
            return f;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Runtime?.Dispose();
            Database?.Dispose();
        }
    }
}
