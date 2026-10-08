namespace Armada.Harbor
{
    using System;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Automation;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Media;

    /// <summary>
    /// Base for the chart controls drawn with Avalonia's <see cref="DrawingContext"/> (no chart library): theme colors from
    /// App.axaml (redrawn when the theme changes), a tooltip that follows the pointer, and an accessible name that
    /// summarizes what the chart shows. The layout math lives in Armada.Core.Metrics.Charts, so it is tested without
    /// Avalonia.
    /// </summary>
    public abstract class ChartControlBase : Control
    {
        #region Public-Members

        /// <summary>
        /// Font size of axis labels.
        /// </summary>
        public double LabelFontSize { get; set; } = 10;

        /// <summary>
        /// The tooltip text shown now, or null (tests and screenshots).
        /// </summary>
        public string? CurrentTip
        {
            get { return _CurrentTip; }
        }

        #endregion

        #region Private-Members

        private string? _CurrentTip = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        protected ChartControlBase()
        {
            ClipToBounds = true;
            ToolTip.SetShowDelay(this, 150);
            ToolTip.SetPlacement(this, PlacementMode.Pointer);
            ActualThemeVariantChanged += (sender, args) => InvalidateVisual();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the tooltip for a point in the control's coordinates, as hovering there would (tests and screenshots).
        /// </summary>
        /// <param name="point">Point.</param>
        public void HoverAt(Point point)
        {
            UpdateTip(point);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// The tooltip text for a point in the control's coordinates, or null for none.
        /// </summary>
        /// <param name="point">Point.</param>
        /// <returns>Text or null.</returns>
        protected abstract string? TipAt(Point point);

        /// <summary>
        /// Called when the hovered point changes (null when the pointer leaves), so a chart can highlight it.
        /// </summary>
        /// <param name="point">Point or null.</param>
        protected virtual void OnHoverChanged(Point? point)
        {
        }

        /// <summary>
        /// Set the accessible name (what a screen reader announces for the chart).
        /// </summary>
        /// <param name="summary">Summary text.</param>
        protected void SetSummary(string summary)
        {
            AutomationProperties.SetName(this, summary ?? String.Empty);
        }

        /// <summary>
        /// A theme brush by resource key.
        /// </summary>
        /// <param name="key">Resource key.</param>
        /// <returns>Brush.</returns>
        protected IBrush ThemeBrush(string key)
        {
            return ChartPalette.Brush(this, key);
        }

        /// <summary>
        /// Text laid out for drawing in the secondary text color.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="brush">Brush, or null for the secondary text brush.</param>
        /// <returns>Formatted text.</returns>
        protected FormattedText Label(string text, IBrush? brush = null)
        {
            return new FormattedText(text ?? String.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, LabelFontSize, brush ?? ThemeBrush("HarborSecondaryTextBrush"));
        }

        /// <inheritdoc />
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            UpdateTip(e.GetPosition(this));
        }

        /// <inheritdoc />
        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _CurrentTip = null;
            ToolTip.SetTip(this, null);
            OnHoverChanged(null);
            InvalidateVisual();
        }

        #endregion

        #region Private-Methods

        private void UpdateTip(Point point)
        {
            string? tip = TipAt(point);
            OnHoverChanged(point);
            InvalidateVisual();
            if (String.Equals(tip, _CurrentTip, StringComparison.Ordinal)) return;
            _CurrentTip = tip;
            ToolTip.SetTip(this, tip);
        }

        #endregion
    }
}
