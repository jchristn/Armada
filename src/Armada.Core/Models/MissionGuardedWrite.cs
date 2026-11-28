namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A full-row mission update that applies only while the stored status is one of <see cref="ExpectedStatuses"/>.
    /// </summary>
    public class MissionGuardedWrite
    {
        /// <summary>
        /// The mission as it should be written.
        /// </summary>
        public Mission Mission { get; set; }

        /// <summary>
        /// Statuses the stored row may have for the write to apply.
        /// </summary>
        public List<MissionStatusEnum> ExpectedStatuses { get; set; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="mission">The mission as it should be written.</param>
        /// <param name="expectedStatuses">Statuses the stored row may have.</param>
        /// <exception cref="ArgumentNullException">A value is null.</exception>
        /// <exception cref="ArgumentException">No expected status is given.</exception>
        public MissionGuardedWrite(Mission mission, IEnumerable<MissionStatusEnum> expectedStatuses)
        {
            Mission = mission ?? throw new ArgumentNullException(nameof(mission));
            if (expectedStatuses == null) throw new ArgumentNullException(nameof(expectedStatuses));
            ExpectedStatuses = new List<MissionStatusEnum>(expectedStatuses);
            if (ExpectedStatuses.Count == 0) throw new ArgumentException("At least one expected status is required.", nameof(expectedStatuses));
        }
    }
}
