namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Modals;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using TUIKit.Widgets;

    /// <summary>
    /// Helpers for the Skills, Playbooks, Endpoints, Harbors, and Memory suites: the open form dialog, its fields by
    /// label, and stub responders that record request bodies.
    /// </summary>
    internal static class TuiConfigTestHelpers
    {
        public static FormDialog Dialog(TuiTestHost host, string title)
        {
            TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog && host.Screen().Contains(title), "form dialog " + title);
            return (FormDialog)host.App.Modals.Top!;
        }

        public static T Field<T>(FormDialog dialog, string label) where T : class, IWidget
        {
            T? field = dialog.Form.Rows.Where(r => r.Label == label).Select(r => r.Field).OfType<T>().FirstOrDefault();
            if (field == null) throw new AssertionException("No field " + label + " of type " + typeof(T).Name);
            return field;
        }

        public static InputField Input(FormDialog dialog, string label)
        {
            return Field<InputField>(dialog, label);
        }

        public static StubHttpHandler Capture(StubHttpHandler stub, string method, string path, string response, TuiConfigBody box, HttpStatusCode status = HttpStatusCode.OK)
        {
            return stub.On(method, path, body =>
            {
                box.Body = body;
                box.Count++;
                return StubHttpHandler.Response(status, response);
            });
        }

        public static void Contains(string frame, params string[] expected)
        {
            foreach (string e in expected) TuiCase.Contains(frame, e, e);
        }
    }
}
