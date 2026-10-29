namespace Armada.Core.BlackBox.Storage
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The black box storage doctor check (GET /api/v1/doctor, "Black Box Storage").
    /// </summary>
    public class BlackBoxStorageDoctorResult
    {
        /// <summary>
        /// Check name.
        /// </summary>
        public const string CheckName = "Black Box Storage";

        /// <summary>
        /// Pass when the configured target is valid and reachable, Warn when the black box is disabled or unwired, Fail
        /// otherwise.
        /// </summary>
        public DoctorCheckStatusEnum Status { get; set; } = DoctorCheckStatusEnum.Pass;

        /// <summary>
        /// What was checked, and the reason when it did not pass (never a secret).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStorageDoctorResult()
        {
        }
    }
}
