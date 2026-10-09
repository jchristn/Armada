namespace Armada.Core.Services
{
    /// <summary>
    /// What a directory on the Harbor is, as the Harbor learned it from the Admiral's dock and launch requests: the
    /// vessel, the mission whose dock it is, and where that dock is in its life. Lets work in that directory be logged
    /// with typed fields instead of a path to read.
    /// </summary>
    public class HarborLogSubject
    {
        #region Public-Members

        /// <summary>
        /// Vessel name, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Mission ID of a mission dock, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// The directory.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// What the directory is.
        /// </summary>
        public HarborLogPathKindEnum PathKind { get; set; } = HarborLogPathKindEnum.Other;

        /// <summary>
        /// For a mission dock, where it is in its life.
        /// </summary>
        public HarborLogStageEnum Stage { get; set; } = HarborLogStageEnum.None;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A copy of this subject.
        /// </summary>
        /// <returns>The copy.</returns>
        public HarborLogSubject Clone()
        {
            return new HarborLogSubject
            {
                VesselName = VesselName,
                MissionId = MissionId,
                Path = Path,
                PathKind = PathKind,
                Stage = Stage
            };
        }

        #endregion
    }
}
