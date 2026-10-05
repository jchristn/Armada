namespace Armada.Tui.Screens.Configuration
{
    using System;
    using Armada.Tui.Widgets;
    using TUIKit.Input;

    /// <summary>
    /// Content of the endpoint health dialog: the health summary over the history chart. <c>v</c> runs Validate Now
    /// when <see cref="ValidateRequested"/> is set (users who can edit the endpoint). Not thread-safe.
    /// </summary>
    public class EndpointHealthView : StackPanel
    {
        #region Public-Members

        /// <summary>
        /// Summary rows.
        /// </summary>
        public LinkDetailView Summary { get; } = new LinkDetailView();

        /// <summary>
        /// History chart (successful probes per bucket).
        /// </summary>
        public ChartView Chart { get; } = new ChartView();

        /// <summary>
        /// Runs Validate Now (<c>v</c>), or null when the user may not validate.
        /// </summary>
        public Action? ValidateRequested { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EndpointHealthView()
        {
            Add(Summary, null, null, 3);
            Chart.Kind = ChartKindEnum.Bar;
            Add(Chart, null, null, 2);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == 'v' && ValidateRequested != null)
            {
                ValidateRequested();
                return true;
            }

            return base.HandleKey(key);
        }

        #endregion
    }
}
