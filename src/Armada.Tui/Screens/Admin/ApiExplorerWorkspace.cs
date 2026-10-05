namespace Armada.Tui.Screens.Admin
{
    using System;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The API Explorer body: the request builder and the response pane, side by side at 110 columns and wider,
    /// stacked otherwise. <c>Tab</c> moves between them. Not thread-safe.
    /// </summary>
    public class ApiExplorerWorkspace : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// The request builder (replaced when the operation changes).
        /// </summary>
        public IWidget Builder
        {
            get { return _Builder; }
        }

        /// <summary>
        /// Response pane.
        /// </summary>
        public ApiExplorerResponsePane ResponsePane { get; }

        #endregion

        #region Private-Members

        private IWidget _Builder;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="builder">Initial builder.</param>
        /// <param name="response">Response pane.</param>
        public ApiExplorerWorkspace(IWidget builder, ApiExplorerResponsePane response)
        {
            _Builder = builder ?? throw new ArgumentNullException(nameof(builder));
            ResponsePane = response ?? throw new ArgumentNullException(nameof(response));
            AddChild(_Builder);
            AddChild(ResponsePane);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the builder, keeping focus on it when it had focus.
        /// </summary>
        /// <param name="builder">New builder.</param>
        public void SetBuilder(IWidget builder)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            bool focused = ReferenceEquals(Scope.Focused, _Builder);
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            _Builder = builder;
            AddChild(_Builder);
            AddChild(ResponsePane);
            if (!focused) Scope.Focus(ResponsePane);
            if (active) Scope.SetActive(true);
        }

        /// <summary>
        /// Focus the builder.
        /// </summary>
        public void FocusBuilder()
        {
            Scope.Focus(_Builder);
        }

        /// <summary>
        /// Focus the response pane.
        /// </summary>
        public void FocusResponse()
        {
            Scope.Focus(ResponsePane);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 2) return;
            if (width >= 110)
            {
                int left = width / 2;
                Scope.RenderChild(surface, _Builder, new Rect(0, 0, left - 1, height));
                for (int y = 0; y < height; y++) surface.DrawText(left - 1, y, "|", Theme.Border);
                Scope.RenderChild(surface, ResponsePane, new Rect(left + 1, 0, width - left - 1, height));
                return;
            }

            int top = Math.Max(3, (height * 55) / 100);
            Scope.RenderChild(surface, _Builder, new Rect(0, 0, width, top));
            SurfaceText.FillRow(surface, 0, top, width, Theme.Border);
            SurfaceText.Draw(surface, 0, top, new string('-', width), Theme.Border, width);
            if (height - top - 1 > 0) Scope.RenderChild(surface, ResponsePane, new Rect(0, top + 1, width, height - top - 1));
        }

        #endregion
    }
}
