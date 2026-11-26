namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Recovery policy overrides for a model profile, set by an administrator (RS-5.5). Every value is null unless set; a
    /// null value means the global setting applies. The values apply to missions run by captains linked to the profile.
    /// </summary>
    public class ModelProfileRecovery
    {
        #region Public-Members

        /// <summary>
        /// In-place resumes after a transient or task failure (overrides MaxRecoveryAttempts), 0 to 10, or null.
        /// </summary>
        public int? MaxResumes
        {
            get => _MaxResumes;
            set => _MaxResumes = Check(value, 0, 10, nameof(MaxResumes));
        }

        /// <summary>
        /// In-place resumes after an Unclassified exit (overrides UnclassifiedResumeLimit), 0 to 3, or null. Never more
        /// than the effective <see cref="MaxResumes"/>.
        /// </summary>
        public int? UnclassifiedResumes
        {
            get => _UnclassifiedResumes;
            set => _UnclassifiedResumes = Check(value, 0, 3, nameof(UnclassifiedResumes));
        }

        /// <summary>
        /// Minutes a transient endpoint outage is ridden out past the resumes (overrides TransientOutageGraceMinutes),
        /// 0 to 120, or null.
        /// </summary>
        public int? TransientGraceMinutes
        {
            get => _TransientGraceMinutes;
            set => _TransientGraceMinutes = Check(value, 0, 120, nameof(TransientGraceMinutes));
        }

        /// <summary>
        /// Automatic Judge rework rounds for a Worker on this model, 1 to 5, or null. It can raise JudgeReworkRounds but
        /// never lower it, so a profile cannot shorten the FullPipeline rework chain.
        /// </summary>
        public int? JudgeReworkRounds
        {
            get => _JudgeReworkRounds;
            set => _JudgeReworkRounds = Check(value, 1, 5, nameof(JudgeReworkRounds));
        }

        /// <summary>
        /// Output-contract repair turns per mission (overrides MaxOutputRepairAttempts), 0 to 3, or null.
        /// </summary>
        public int? OutputRepairAttempts
        {
            get => _OutputRepairAttempts;
            set => _OutputRepairAttempts = Check(value, 0, 3, nameof(OutputRepairAttempts));
        }

        /// <summary>
        /// Resumes after which a task or Unclassified failure escalates to the next captain on the persona's escalation
        /// ladder instead of resuming again (RS-5.6), 0 to 10, or null to escalate only once the resumes are spent.
        /// Never more than the effective <see cref="MaxResumes"/>. Transient outages keep resuming.
        /// </summary>
        public int? EscalateAfterResumes
        {
            get => _EscalateAfterResumes;
            set => _EscalateAfterResumes = Check(value, 0, 10, nameof(EscalateAfterResumes));
        }

        #endregion

        #region Private-Members

        private int? _MaxResumes = null;
        private int? _UnclassifiedResumes = null;
        private int? _TransientGraceMinutes = null;
        private int? _JudgeReworkRounds = null;
        private int? _OutputRepairAttempts = null;
        private int? _EscalateAfterResumes = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileRecovery()
        {
        }

        #endregion

        #region Private-Methods

        private static int? Check(int? value, int min, int max, string name)
        {
            if (value.HasValue && (value.Value < min || value.Value > max))
                throw new ArgumentOutOfRangeException(name, name + " must be between " + min + " and " + max + ".");
            return value;
        }

        #endregion
    }
}
