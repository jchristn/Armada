namespace Armada.Core.Settings
{
    /// <summary>
    /// Configuration for commit message and PR description templates.
    /// Templates support placeholder parameters such as {MissionId}, {CaptainId}, etc.
    /// </summary>
    public class MessageTemplateSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether to inject commit message metadata instructions into agent prompts.
        /// </summary>
        public bool EnableCommitMetadata { get; set; } = true;

        /// <summary>
        /// Whether to append metadata to pull request descriptions.
        /// </summary>
        public bool EnablePrMetadata { get; set; } = true;

        /// <summary>
        /// Template for commit message metadata appended via agent prompt instructions.
        /// Supported placeholders: {MissionId}, {MissionTitle}, {VoyageId}, {VoyageTitle},
        /// {CaptainId}, {CaptainName}, {VesselId}, {VesselName}, {FleetId}, {DockId},
        /// {BranchName}, {Timestamp}.
        /// </summary>
        public string CommitMessageTemplate
        {
            get => _CommitMessageTemplate;
            set => _CommitMessageTemplate = value ?? "";
        }

        /// <summary>
        /// Template for pull request description metadata.
        /// Supported placeholders: {MissionId}, {MissionTitle}, {VoyageId}, {VoyageTitle},
        /// {CaptainId}, {CaptainName}, {VesselId}, {VesselName}, {FleetId}, {DockId},
        /// {BranchName}, {Timestamp}.
        /// </summary>
        public string PrDescriptionTemplate
        {
            get => _PrDescriptionTemplate;
            set => _PrDescriptionTemplate = value ?? "";
        }

        /// <summary>
        /// Template for local merge commit messages.
        /// Supported placeholders: {MissionId}, {MissionTitle}, {VoyageId}, {VoyageTitle},
        /// {CaptainId}, {CaptainName}, {VesselId}, {VesselName}, {FleetId}, {DockId},
        /// {BranchName}, {Timestamp}.
        /// </summary>
        public string MergeCommitTemplate
        {
            get => _MergeCommitTemplate;
            set => _MergeCommitTemplate = value ?? "";
        }

        #endregion

        #region Private-Members

        private string _CommitMessageTemplate =
            "\nArmada-Mission-Id: {MissionId}\n" +
            "Armada-Voyage-Id: {VoyageId}\n" +
            "Armada-Captain-Id: {CaptainId}\n" +
            "Armada-Vessel-Id: {VesselId}";

        private string _PrDescriptionTemplate =
            "\n\n---\n" +
            "Committed by [Armada](https://github.com/jchristn/armada)\n" +
            "- Mission ID : {MissionId}\n" +
            "- Voyage ID  : {VoyageId}\n" +
            "- Captain ID : {CaptainId}\n" +
            "- Vessel ID  : {VesselId}";

        private string _MergeCommitTemplate =
            "Merge armada mission: {BranchName}\n\n" +
            "Armada-Mission-Id: {MissionId}\n" +
            "Armada-Voyage-Id: {VoyageId}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public MessageTemplateSettings()
        {
        }

        #endregion
    }
}
