namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The body of the fleet action run page: the run summary (progress, counts, timing, snapshot) above the
    /// "Targets" heading with its status filter and the targets table. <c>Tab</c> moves between the three; <c>o</c> on
    /// a target raises <see cref="OutputRequested"/>. Not thread-safe.
    /// </summary>
    public class FleetRunBody : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Run summary.
        /// </summary>
        public OpsDocumentView Summary { get; } = new OpsDocumentView();

        /// <summary>
        /// Target status filter.
        /// </summary>
        public SelectField<string> Filter { get; } = new SelectField<string>();

        /// <summary>
        /// Targets.
        /// </summary>
        public ArmadaGrid<FleetActionRunTargetSummary> Grid { get; } = new ArmadaGrid<FleetActionRunTargetSummary>(t => t.Id);

        /// <summary>
        /// Rows the summary wants (set by the owner when the run changes).
        /// </summary>
        public int SummaryRows { get; set; } = 8;

        /// <summary>
        /// Raised for <c>o</c> on a target (View output).
        /// </summary>
        public event EventHandler<FleetActionRunTargetSummary>? OutputRequested;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetRunBody()
        {
            // The summary, the status filter, and the target grid are separate focus regions, each in its own box.
            Scope.RegionHost = true;
            AddChild(Summary);
            AddChild(Filter);
            AddChild(Grid);
            Scope.Wrap = true;
            Scope.Focus(Grid);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (ReferenceEquals(Scope.Focused, Grid) && key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == 'o')
            {
                FleetActionRunTargetSummary? current = Grid.Current;
                if (current != null) OutputRequested?.Invoke(this, current);
                return current != null;
            }

            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            // Rows: summary, line, "Targets  Status: [filter]", line, grid (the lines are box edges, see RegionFrames).
            int summary = Math.Max(3, Math.Min(SummaryRows, height / 2));
            Scope.RenderChild(surface, Summary, new Rect(0, 0, width, summary));
            int y = summary + 1;
            if (y >= height) return;
            string heading = T("Targets");
            int x = SurfaceText.Draw(surface, 0, y, heading, Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            x += SurfaceText.Draw(surface, x + 2, y, T("Status") + ":", Theme.Muted, width - x - 2) + 3;
            Scope.RenderChild(surface, Filter, new Rect(x, y, Math.Min(22, Math.Max(4, width - x - 1)), 1));
            y += 2;
            if (y < height) Scope.RenderChild(surface, Grid, new Rect(0, y, width, height - y));
        }

        #endregion
    }
}
