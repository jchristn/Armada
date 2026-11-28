namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Emits the signals and events that report a mission's outcome.
    /// </summary>
    internal sealed class MissionOutcomeTelemetry
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly string _Header;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionOutcomeTelemetry(MissionService service, MissionServiceContext context)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (context == null) throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Header = context.Header;
            _Logging = context.Logging;
        }

        #endregion

        #region Internal-Methods

        internal async Task EmitMissionOutcomeTelemetryAsync(Mission mission, Captain captain, CancellationToken token)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            MissionOutcomeEventDefinition missionOutcomeEvent = BuildMissionOutcomeEvent(mission);

            try
            {
                ArmadaEvent outcomeEvent = new ArmadaEvent(missionOutcomeEvent.EventType, missionOutcomeEvent.EventMessage);
                outcomeEvent.TenantId = mission.TenantId;
                outcomeEvent.UserId = mission.UserId;
                outcomeEvent.EntityType = "mission";
                outcomeEvent.EntityId = mission.Id;
                outcomeEvent.CaptainId = captain.Id;
                outcomeEvent.MissionId = mission.Id;
                outcomeEvent.VesselId = mission.VesselId;
                outcomeEvent.VoyageId = mission.VoyageId;
                await _Database.Events.CreateAsync(outcomeEvent, token).ConfigureAwait(false);
            }
            catch (Exception evtEx)
            {
                _Logging.Warn(_Header + "error emitting mission outcome event for " + mission.Id + ": " + evtEx.Message);
            }
        }

        internal static MissionOutcomeSignalDefinition BuildMissionOutcomeSignal(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            return mission.Status switch
            {
                MissionStatusEnum.Complete => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Completion, Payload = "Mission completed: " + mission.Title },
                MissionStatusEnum.PullRequestOpen => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Completion, Payload = "Pull request open: " + mission.Title },
                MissionStatusEnum.Failed => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Error, Payload = BuildFailurePayload("Mission failed: ", mission) },
                MissionStatusEnum.LandingFailed => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Error, Payload = BuildFailurePayload("Landing failed: ", mission) },
                MissionStatusEnum.Cancelled => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Error, Payload = BuildFailurePayload("Mission cancelled: ", mission) },
                _ => new MissionOutcomeSignalDefinition { Type = SignalTypeEnum.Completion, Payload = "Work produced: " + mission.Title }
            };
        }

        #endregion

        #region Private-Methods

        private static MissionOutcomeEventDefinition BuildMissionOutcomeEvent(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            return mission.Status switch
            {
                MissionStatusEnum.Failed => new MissionOutcomeEventDefinition { EventType = "mission.failed", EventMessage = BuildFailurePayload("Mission failed: ", mission) },
                MissionStatusEnum.LandingFailed => new MissionOutcomeEventDefinition { EventType = "mission.landing_failed", EventMessage = BuildFailurePayload("Landing failed: ", mission) },
                MissionStatusEnum.Cancelled => new MissionOutcomeEventDefinition { EventType = "mission.cancelled", EventMessage = BuildFailurePayload("Mission cancelled: ", mission) },
                _ => new MissionOutcomeEventDefinition { EventType = "mission.work_produced", EventMessage = "Work produced: " + mission.Title }
            };
        }

        private static string BuildFailurePayload(string prefix, Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            string payload = prefix + mission.Title;
            if (!String.IsNullOrWhiteSpace(mission.FailureReason))
            {
                payload += " (" + mission.FailureReason + ")";
            }

            return payload;
        }

        #endregion
    }
}
