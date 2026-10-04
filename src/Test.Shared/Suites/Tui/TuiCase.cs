namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Case builders shared by the Tui.* and Client.* suites.
    /// </summary>
    internal static class TuiCase
    {
        /// <summary>
        /// Synchronous case.
        /// </summary>
        public static TestCaseDescriptor Sync(string suiteId, string caseId, string displayName, Action body, string tag = TestTags.Positive)
        {
            return new TestCaseDescriptor(
                suiteId: suiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        /// <summary>
        /// Asynchronous case.
        /// </summary>
        public static TestCaseDescriptor Async(string suiteId, string caseId, string displayName, Func<Task> body, string tag = TestTags.Positive)
        {
            return new TestCaseDescriptor(
                suiteId: suiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        /// <summary>
        /// A signed-in host at a size, already on a route.
        /// </summary>
        public static TuiTestHost SignedIn(int width, int height, string route = "/missions", StubHttpHandler? stub = null)
        {
            StubHttpHandler handler = stub ?? TuiFixtures.SignedInServer();
            TuiTestHost host = new TuiTestHost(width, height, handler, "http://127.0.0.1:9", o => { o.Token = "tok_env"; o.StartRoute = route; });
            host.Start();
            host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null, 5000);
            return host;
        }

        /// <summary>
        /// Assert text appears in a frame, reporting the frame on failure.
        /// </summary>
        public static void Contains(string frame, string expected, string label)
        {
            if (!frame.Contains(expected, StringComparison.Ordinal))
                throw new AssertionException(label + ": expected \"" + expected + "\" in frame:\n" + frame);
        }

        /// <summary>
        /// Assert text is absent.
        /// </summary>
        public static void NotContains(string frame, string unexpected, string label)
        {
            if (frame.Contains(unexpected, StringComparison.Ordinal))
                throw new AssertionException(label + ": did not expect \"" + unexpected + "\" in frame:\n" + frame);
        }
    }
}
