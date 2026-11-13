namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds and reads the event that records a stage check on its mission (event type <see cref="EventType"/>, the
    /// <see cref="StageCheckResult"/> as payload), so every stage shows whether it was verified, failed, skipped or
    /// could not be checked.
    /// </summary>
    public static class StageCheckEvents
    {
        #region Public-Members

        /// <summary>
        /// Event type of a stage-check event.
        /// </summary>
        public const string EventType = "mission.stage_check";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the event that records a stage check.
        /// </summary>
        /// <param name="mission">Mission the check ran for.</param>
        /// <param name="captain">Captain whose stage was checked.</param>
        /// <param name="result">Check result.</param>
        /// <returns>The event, not yet stored.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static ArmadaEvent Build(Mission mission, Captain captain, StageCheckResult result)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (result == null) throw new ArgumentNullException(nameof(result));

            ArmadaEvent evt = new ArmadaEvent(EventType, Describe(mission, captain, result));
            evt.TenantId = mission.TenantId;
            evt.UserId = mission.UserId;
            evt.EntityType = "mission";
            evt.EntityId = mission.Id;
            evt.CaptainId = captain.Id;
            evt.MissionId = mission.Id;
            evt.VesselId = mission.VesselId;
            evt.VoyageId = mission.VoyageId;
            var payload = JsonSerializer.SerializeToNode(result, _JsonOptions)!.AsObject();
            if (mission.Status == MissionStatusEnum.Verifying) payload["verificationVersion"] = mission.LastUpdateUtc;
            evt.Payload = payload.ToJsonString(_JsonOptions);
            return evt;
        }

        /// <summary>
        /// Read the check result from a stage-check event.
        /// </summary>
        /// <param name="evt">Event.</param>
        /// <returns>The result, or null when the event is not a stage-check event or its payload is unreadable.</returns>
        public static StageCheckResult? Read(ArmadaEvent? evt)
        {
            if (evt == null || !String.Equals(evt.EventType, EventType, StringComparison.Ordinal) || String.IsNullOrWhiteSpace(evt.Payload)) return null;
            try
            {
                return JsonSerializer.Deserialize<StageCheckResult>(evt.Payload!, _JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string Describe(Mission mission, Captain captain, StageCheckResult result)
        {
            string subject = "Stage check for mission " + mission.Id + " (" + captain.Name + ")";
            switch (result.Status)
            {
                case StageCheckStatusEnum.Passed:
                    return subject + " passed" + (result.PreExistingFailures.Count > 0 ? "; failing on the base commit too: " + String.Join("; ", result.PreExistingFailures) : "");
                case StageCheckStatusEnum.Failed:
                    return subject + " failed: " + result.FailedCommand + " exited " + result.ExitCode;
                case StageCheckStatusEnum.CouldNotRun:
                    return subject + " could not run (" + result.Reason + ")" + (result.FailedCommand != null ? ": " + result.FailedCommand : "");
                default:
                    return subject + " skipped (" + result.Reason + ")";
            }
        }

        #endregion
    }
}
