namespace Test.Shared.Infrastructure
{
    using System;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Runs the per-screen keyboard flow of the Tui.KeyboardFlows suites against a stub server: open the screen, filter,
    /// select the row, open and close its row-action menu, open and dismiss the create form (or page), open the row with
    /// Enter, and go back with Alt+Left to the same list.
    /// </summary>
    public static class TuiFlowRunner
    {
        #region Public-Methods

        /// <summary>
        /// Run the flow for one screen; throws <see cref="AssertionException"/> naming the step that failed.
        /// </summary>
        /// <param name="spec">Screen.</param>
        public static void Run(TuiFlowSpec spec)
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json(spec.ListMethod, spec.ListPath, spec.BareArray ? "[" + spec.RowJson + "]" : TuiEntityFixtures.Page(spec.RowJson));
            if (spec.DetailPath != null) stub.Json("GET", spec.DetailPath, spec.DetailJson ?? spec.RowJson);
            foreach (string[] route in spec.ExtraRoutes) stub.Json(route[0], route[1], route[2]);

            using (TuiTestHost host = TuiEntityFixtures.Open(stub, spec.Route))
            {
                // Open.
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains(spec.RowText, StringComparison.Ordinal), spec + ": row shown");
                string listPath = host.Tui.Context.Router.Current!.FullPath;

                // Filter: / focuses the filter; the row still matches; Esc returns to the grid.
                if (spec.FilterKey != null)
                {
                    host.Press(spec.FilterKey);
                    host.Type(spec.RowText.Substring(0, 3));
                    TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains(spec.RowText, StringComparison.Ordinal), spec + ": row matches the filter");
                    host.Press("esc");
                    AssertFalse(host.App.Modals.IsActive, spec + ": no modal after the filter");
                }

                // Select and open the row-action menu.
                host.Press("home");
                if (spec.RowMenuKey != null)
                {
                    host.Press(spec.RowMenuKey);
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive, spec + ": row menu");
                    host.Press("esc");
                    TuiEntityFixtures.WaitFor(host, () => !host.App.Modals.IsActive, spec + ": row menu closed");
                }

                // Modal: the create form opens and Esc dismisses it.
                if (spec.NewKey != null && spec.NewRoute != null)
                {
                    host.Press(spec.NewKey);
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == spec.NewRoute, spec + ": create page " + spec.NewRoute);
                    host.Press(spec.NewBackKey);
                    if (host.App.Modals.IsActive) host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == listPath, spec + ": back from the create page (now " + host.Tui.Context.Router.Current!.FullPath + ")");
                    TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains(spec.RowText, StringComparison.Ordinal), spec + ": row shown after the create page");
                }
                else if (spec.NewKey != null)
                {
                    host.Press(spec.NewKey);
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive, spec + ": create form");
                    host.Press("esc");
                    if (host.App.Modals.IsActive) host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => !host.App.Modals.IsActive, spec + ": create form dismissed");
                }

                // Open the row with Enter.
                host.Press("home");
                host.Press(spec.OpenKey);
                if (spec.DetailRoute != null)
                {
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == spec.DetailRoute, spec + ": Enter opens " + spec.DetailRoute + " (now " + host.Tui.Context.Router.Current!.FullPath + ")");
                    string? detailText = spec.DetailText == "" ? spec.RowText : spec.DetailText;
                    if (detailText != null) TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains(detailText, StringComparison.Ordinal), spec + ": detail shows " + detailText);
                }
                else
                {
                    if (spec.OpenedText != null)
                    {
                        TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains(spec.OpenedText, StringComparison.Ordinal), spec + ": the row opens in place (" + spec.OpenedText + ")");
                    }
                    else
                    {
                        TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive, spec + ": the row opens in a dialog");
                    }

                    host.Press("esc");
                    if (host.App.Modals.IsActive) host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => !host.App.Modals.IsActive, spec + ": dialog closed");
                    host.Tui.Context.Navigate("/jobs");
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/jobs", spec + ": left the list");
                }

                // Back.
                host.Press(spec.DetailRoute != null ? spec.DetailBackKey : "alt+left");
                if (host.App.Modals.IsActive) host.Press("y");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == listPath, spec + ": back returns to " + listPath + " (now " + host.Tui.Context.Router.Current!.FullPath + ")");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains(spec.RowText, StringComparison.Ordinal), spec + ": list shows the row again");
            }
        }

        #endregion
    }
}
