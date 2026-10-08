namespace Armada.Harbor
{
    using System;
    using Armada.Core.Metrics.Charts;
    using Avalonia.Controls;
    using Avalonia.Media;
    using Avalonia.Styling;

    /// <summary>
    /// Maps chart color roles to the theme resources in App.axaml, so charts follow light and dark like the rest of the
    /// app.
    /// </summary>
    public static class ChartPalette
    {
        #region Public-Methods

        /// <summary>
        /// The theme resource key of a color role.
        /// </summary>
        /// <param name="color">Color role.</param>
        /// <returns>Resource key.</returns>
        public static string ResourceKey(ChartColorEnum color)
        {
            switch (color)
            {
                case ChartColorEnum.Success: return "HarborSuccessBrush";
                case ChartColorEnum.Danger: return "HarborDangerBrush";
                case ChartColorEnum.Warning: return "HarborWarningBrush";
                case ChartColorEnum.Idle: return "HarborIdleBrush";
                case ChartColorEnum.Series1: return "HarborChartSeries1Brush";
                case ChartColorEnum.Series2: return "HarborChartSeries2Brush";
                case ChartColorEnum.Series3: return "HarborChartSeries3Brush";
                case ChartColorEnum.Series4: return "HarborChartSeries4Brush";
                case ChartColorEnum.Series5: return "HarborChartSeries5Brush";
                case ChartColorEnum.Series6: return "HarborChartSeries6Brush";
                default: return "HarborAccentBrush";
            }
        }

        /// <summary>
        /// Look up a theme brush for a control in its current theme.
        /// </summary>
        /// <param name="control">Control.</param>
        /// <param name="key">Resource key.</param>
        /// <returns>The brush, or a gray fallback when the resource is missing.</returns>
        public static IBrush Brush(Control control, string key)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            ThemeVariant variant = control.ActualThemeVariant;
            if (control.TryFindResource(key, variant, out object? value) && value is IBrush brush) return brush;
            return Brushes.Gray;
        }

        /// <summary>
        /// Look up the brush of a color role for a control in its current theme.
        /// </summary>
        /// <param name="control">Control.</param>
        /// <param name="color">Color role.</param>
        /// <returns>The brush.</returns>
        public static IBrush Brush(Control control, ChartColorEnum color)
        {
            return Brush(control, ResourceKey(color));
        }

        #endregion
    }
}
