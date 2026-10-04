namespace Armada.Core.Services.Ask
{
    using System;

    /// <summary>
    /// A meaningful change in tracked work that is posted to the thread as a WorkUpdate message.
    /// </summary>
    public class AskMilestone
    {
        #region Public-Members

        /// <summary>
        /// Milestone kind: Started, MissionFailed, MissionLanded, PullRequestOpened, LandingFailed, Succeeded, Failed, or
        /// Cancelled.
        /// </summary>
        public string Kind
        {
            get => _Kind;
            set => _Kind = value ?? String.Empty;
        }

        /// <summary>
        /// Deterministic one-sentence description, used verbatim when the captain does not narrate.
        /// </summary>
        public string Text
        {
            get => _Text;
            set => _Text = value ?? String.Empty;
        }

        /// <summary>
        /// Whether the milestone ends the tracked work.
        /// </summary>
        public bool Terminal { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Kind = String.Empty;
        private string _Text = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMilestone()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="kind">Milestone kind.</param>
        /// <param name="text">Deterministic sentence.</param>
        /// <param name="terminal">Whether it ends the work.</param>
        public AskMilestone(string kind, string text, bool terminal)
        {
            Kind = kind;
            Text = text;
            Terminal = terminal;
        }

        #endregion
    }
}
