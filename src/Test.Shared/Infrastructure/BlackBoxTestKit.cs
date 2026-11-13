namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Shared helpers for black box suites: quiet logging, settings, a runtime with a recording event sink, and entity
    /// builders.
    /// </summary>
    public static class BlackBoxTestKit
    {
        /// <summary>Quiet logging.</summary>
        /// <returns>The logging module.</returns>
        public static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        /// <summary>Settings with the black box on.</summary>
        /// <returns>Settings.</returns>
        public static ArmadaSettings Settings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.BlackBox.Enabled = true;
            return settings;
        }

        /// <summary>A runtime over a test database with a recording sink.</summary>
        /// <param name="db">Database.</param>
        /// <param name="settings">Settings, or null for defaults.</param>
        /// <param name="sink">Recording sink, or null for a new one.</param>
        /// <returns>The runtime.</returns>
        public static BlackBoxRuntime Runtime(DatabaseDriver db, ArmadaSettings? settings = null, RecordingBlackBoxEventSink? sink = null)
        {
            return BlackBoxRuntime.Create(db, settings ?? Settings(), Logging(), new AssemblyBlackBoxVersionProvider(typeof(BlackBoxTestKit).Assembly), sink ?? new RecordingBlackBoxEventSink());
        }

        /// <summary>A global admin caller.</summary>
        /// <returns>The caller.</returns>
        public static AuthContext Admin()
        {
            return new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = "ten_admin", UserId = "usr_admin" };
        }

        /// <summary>A regular user caller.</summary>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="userId">User.</param>
        /// <param name="tenantAdmin">Tenant admin.</param>
        /// <returns>The caller.</returns>
        public static AuthContext User(string tenantId, string userId, bool tenantAdmin = false)
        {
            return new AuthContext { IsAuthenticated = true, TenantId = tenantId, UserId = userId, IsTenantAdmin = tenantAdmin };
        }

        /// <summary>A failed mission row. Tenant and user are only set when they reference existing rows (foreign keys); pass
        /// null to leave them unset.</summary>
        /// <param name="db">Database.</param>
        /// <param name="tenantId">Tenant (must exist), or null.</param>
        /// <param name="userId">User (must exist), or null.</param>
        /// <param name="kind">Failure kind.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="status">Status.</param>
        /// <returns>The mission.</returns>
        public static async Task<Mission> FailedMissionAsync(DatabaseDriver db, string? tenantId = null, string? userId = null, MissionFailureKindEnum? kind = MissionFailureKindEnum.ScopeViolation, string reason = "Mission modified files outside its scoped file list: src/a.cs", MissionStatusEnum status = MissionStatusEnum.Failed)
        {
            Mission mission = new Mission("Black box test mission " + Guid.NewGuid().ToString("N").Substring(0, 6), "Fix it");
            mission.TenantId = tenantId;
            mission.UserId = userId;
            mission.Status = status;
            mission.FailureKind = kind;
            mission.FailureReason = reason;
            mission.LastUpdateUtc = DateTime.UtcNow.AddSeconds(-5);
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        /// <summary>A minimal entry (not inserted).</summary>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="missionId">Mission.</param>
        /// <param name="createdUtc">Creation time, or null for now.</param>
        /// <returns>The entry.</returns>
        public static BlackBoxEntry Entry(string? tenantId, string? missionId, DateTime? createdUtc = null)
        {
            return new BlackBoxEntry
            {
                TenantId = tenantId,
                UserId = tenantId == null ? null : "usr_" + tenantId,
                SubjectType = BlackBoxSubjectTypeEnum.Mission,
                MissionId = missionId,
                VesselId = "vsl_bb",
                MissionTitle = "Entry for " + missionId,
                Phase = BlackBoxPhaseEnum.Landing,
                Step = BlackBoxStepEnum.TargetIntegration,
                FailureKind = MissionFailureKindEnum.LandingConflict,
                StatusBefore = "WorkProduced",
                StatusAfter = "LandingFailed",
                Fingerprint = "fp_default",
                ReasonExcerpt = "merge conflict in src/a.cs",
                CreatedUtc = createdUtc ?? DateTime.UtcNow
            };
        }
    }
}
