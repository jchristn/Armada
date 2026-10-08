namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Metrics.Charts;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// A chart legend: one swatch and label per series (dashed series and the reference line show a dashed swatch), with
    /// each series' total or highest value, so the chart is readable without telling colors apart.
    /// </summary>
    public class ChartLegend : WrapPanel
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ChartLegend()
        {
            Orientation = Orientation.Horizontal;
            Margin = new Thickness(0, 6, 0, 0);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the series of a bucketed chart.
        /// </summary>
        /// <param name="model">Model, or null to clear.</param>
        public void Show(BucketChartModel? model)
        {
            Children.Clear();
            if (model == null) return;
            foreach (ChartSeriesData series in model.Series)
            {
                string value = model.Kind == BucketChartKindEnum.StackedBar
                    ? ChartFormat.Value(series.Total(), model.Format)
                    : "highest " + ChartFormat.Value(series.Max(), model.Format);
                Add(series.Color, series.Dashed, model.Kind == BucketChartKindEnum.Line, series.Name + "  " + value);
            }

            if (model.Reference != null) Add(model.Reference.Color, true, true, model.Reference.Label);
        }

        /// <summary>
        /// Show the states of a status strip.
        /// </summary>
        public void ShowStates()
        {
            Children.Clear();
            foreach (HarborLinkSegmentStateEnum state in StatusStripModel.LegendStates)
            {
                Add(StatusStripModel.StateColor(state), false, false, StatusStripModel.StateLabel(state));
            }
        }

        #endregion

        #region Private-Methods

        private void Add(ChartColorEnum color, bool dashed, bool line, string text)
        {
            StackPanel item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 16, 2) };
            Control swatch;
            if (line)
            {
                Avalonia.Controls.Shapes.Line stroke = new Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Point(0, 6),
                    EndPoint = new Point(16, 6),
                    StrokeThickness = 2,
                    Width = 16,
                    Height = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (dashed) stroke.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2, 1.5 };
                stroke.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, stroke.GetResourceObservable(ChartPalette.ResourceKey(color)));
                swatch = stroke;
            }
            else
            {
                Border box = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
                box.Bind(Border.BackgroundProperty, box.GetResourceObservable(ChartPalette.ResourceKey(color)));
                swatch = box;
            }

            item.Children.Add(swatch);
            item.Children.Add(HarborUi.Secondary(new TextBlock { Text = text ?? String.Empty, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }));
            Children.Add(item);
        }

        #endregion
    }
}
