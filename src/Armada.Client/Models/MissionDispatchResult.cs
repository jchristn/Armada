namespace Armada.Client.Models
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Reply of <c>POST /api/v1/missions</c>. The server returns the Mission and, when no captain could take it yet,
    /// leaves it Pending with an <c>X-Armada-Warning</c> header. Servers before v1.3.0 wrapped that case as
    /// <c>{ Mission, Warning }</c>. Both shapes are read into this class, so the mission id and the warning are never lost.
    /// </summary>
    public class MissionDispatchResult
    {
        #region Public-Members

        /// <summary>
        /// The created mission, or null when the reply had no body.
        /// </summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>
        /// Warning when no captain could take the mission yet: the older wrapped reply's text, or the shared
        /// <c>usageSelection.missionWaiting</c> message for a Pending mission. Null when a captain took it.
        /// </summary>
        public string? Warning { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionDispatchResult()
        {
        }

        /// <summary>
        /// Read either reply shape.
        /// </summary>
        /// <param name="json">Reply body.</param>
        /// <returns>The result, or null when the body is empty.</returns>
        public static MissionDispatchResult? Parse(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            MissionDispatchResult? wrapped = ArmadaJson.Deserialize<MissionDispatchResult>(json);
            if (wrapped != null && wrapped.Mission != null) return wrapped;

            MissionDispatchResult plain = new MissionDispatchResult();
            plain.Mission = ArmadaJson.Deserialize<Mission>(json);
            if (plain.Mission != null && plain.Mission.Status == MissionStatusEnum.Pending)
                plain.Warning = UsageSelectionText.Get("usageSelection.missionWaiting");
            return plain;
        }

        #endregion
    }
}
