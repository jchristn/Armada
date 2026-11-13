namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// An Ask harness with a real black box on the same database: a regular user of the default tenant owns a failed
    /// mission (landing found a dirty checkout, src/a.cs and src/b.cs) whose analyzed entry has recovery paths, and a
    /// conversation with a captain. Recovery steps go to a recording executor.
    /// </summary>
    public sealed class AskBlackBoxFixture : IDisposable
    {
        /// <summary>Ask harness.</summary>
        public AskTestHarness H { get; private set; } = null!;

        /// <summary>Black box runtime.</summary>
        public BlackBoxRuntime Bb { get; private set; } = null!;

        /// <summary>Recovery step executor.</summary>
        public RecordingBlackBoxActionExecutor Executor { get; } = new RecordingBlackBoxActionExecutor();

        /// <summary>The conversation's owner.</summary>
        public UserMaster User { get; private set; } = null!;

        /// <summary>The owner as a caller.</summary>
        public AuthContext Owner { get; private set; } = null!;

        /// <summary>The conversation.</summary>
        public AskThread Thread { get; private set; } = null!;

        /// <summary>The entry.</summary>
        public string EntryId { get; private set; } = "";

        /// <summary>The failed mission.</summary>
        public string MissionId { get; private set; } = "";

        private AskBlackBoxFixture()
        {
        }

        /// <summary>
        /// Create the fixture.
        /// </summary>
        /// <param name="tenantAdmin">Whether the owner is a tenant admin (recovery steps such as retry_landing need one);
        /// false for a regular user who sees only their own entries.</param>
        /// <returns>The fixture.</returns>
        public static async Task<AskBlackBoxFixture> CreateAsync(bool tenantAdmin = true)
        {
            AskBlackBoxFixture f = new AskBlackBoxFixture();
            f.H = await AskTestHarness.CreateAsync().ConfigureAwait(false);
            f.Bb = BlackBoxTestKit.Runtime(f.H.Db.Driver);
            f.Bb.Service.ActionExecutor = f.Executor;
            f.H.UseBlackBox(f.Bb.Service);
            UserMaster user = new UserMaster(Constants.DefaultTenantId, "bb-ask-" + Guid.NewGuid().ToString("N").Substring(0, 6) + "@example.com", "password");
            user.IsTenantAdmin = tenantAdmin;
            f.User = await f.H.Db.Driver.Users.CreateAsync(user).ConfigureAwait(false);
            f.Owner = AskTestHarness.User(f.User.Id, tenantAdmin);
            AskBlackBoxCaptured captured = await f.CaptureAsync(f.User.Id).ConfigureAwait(false);
            f.EntryId = captured.EntryId;
            f.MissionId = captured.MissionId;
            Captain captain = await f.H.Db.Driver.Captains.CreateAsync(new Captain("captain-bb-ask") { TenantId = Constants.DefaultTenantId, UserId = f.User.Id }).ConfigureAwait(false);
            f.Thread = await f.H.Threads.CreateThreadAsync(f.Owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
            return f;
        }

        /// <summary>
        /// Capture and analyze a dirty-checkout landing failure of a new mission owned by a user.
        /// </summary>
        /// <param name="userId">Owner of the mission (an existing user of the default tenant).</param>
        /// <returns>The entry and mission.</returns>
        public async Task<AskBlackBoxCaptured> CaptureAsync(string userId)
        {
            Vessel vessel = new Vessel("bb-ask-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.invalid/bb-ask.git");
            vessel.TenantId = Constants.DefaultTenantId;
            vessel = await H.Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            Dock dock = new Dock(vessel.Id);
            dock.TenantId = Constants.DefaultTenantId;
            dock = await H.Db.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(H.Db.Driver, Constants.DefaultTenantId, userId, MissionFailureKindEnum.Infra, "Landing found a dirty checkout", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
            mission.VesselId = vessel.Id;
            mission.DockId = dock.Id;
            mission = await H.Db.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
            string? id = await Bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.DirtyCheckout, c =>
            {
                c.GitFacts = new BlackBoxGitFacts { CheckoutPath = "/work/checkout" };
                c.GitFacts.DirtyPaths.AddRange(new[] { "src/a.cs", "src/b.cs" });
            }).ConfigureAwait(false);
            if (id == null) throw new InvalidOperationException("capture failed");
            await Bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);
            return new AskBlackBoxCaptured(id, mission.Id);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Bb?.Dispose();
            H?.Dispose();
        }
    }
}
