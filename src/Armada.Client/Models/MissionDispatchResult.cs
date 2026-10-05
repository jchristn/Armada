namespace Armada.Client.Models
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Reply of <c>POST /api/v1/missions</c>. The server returns the bare mission when a captain took it, and
    /// <c>{ Mission, Warning }</c> when the mission was created but no captain could take it yet (status Pending).
    /// Both shapes are read into this class, so the mission id and the warning are never lost.
    /// </summary>
    public class MissionDispatchResult
    {
        #region Public-Members

        /// <summary>
        /// The created mission, or null when the reply had no body.
        /// </summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>
        /// Warning from the wrapped reply (no captain could take the mission yet), or null.
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
            return plain;
        }

        #endregion
    }
}
