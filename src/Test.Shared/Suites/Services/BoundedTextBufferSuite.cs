namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The bounded live output buffer (RS-10.5): it returns exactly what the earlier StringBuilder trim returned (all
    /// text while it fits, then the first part, the truncation marker and the latest part), and an append past the cap
    /// costs the appended length instead of a copy of the whole buffer.
    /// </summary>
    public sealed class BoundedTextBufferSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BoundedTextBuffer";
        private static readonly string _Marker = "\n" + Armada.Core.Services.ProtocolLines.TruncationMarker + "\n";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("matches_previous_trim", "Lines and fragments of random length give the same text as the previous trim-on-every-append StringBuilder, below and past the cap", TestTags.Positive, () =>
            {
                Random random = new Random(1505);
                foreach (int max in new int[] { 1, 10, _Marker.Length, _Marker.Length + 1, 64, 257, 4096 })
                {
                    BoundedTextBuffer buffer = new BoundedTextBuffer(max);
                    StringBuilder reference = new StringBuilder();
                    for (int i = 0; i < 400; i++)
                    {
                        string text = new string((char)('a' + (i % 26)), random.Next(0, 40));
                        if (random.Next(2) == 0)
                        {
                            buffer.AppendLine(text);
                            if (reference.Length > 0) reference.AppendLine();
                            reference.Append(text);
                        }
                        else
                        {
                            buffer.Append(text);
                            reference.Append(text);
                        }
                        PreviousTrim(reference, max);
                        AssertEqual(reference.ToString(), buffer.ToString(), "max " + max + ", step " + i);
                        AssertEqual(reference.Length, buffer.Length, "length, max " + max + ", step " + i);
                    }
                }
            }));

            cases.Add(Case("keeps_head_and_latest_tail", "Past the cap the buffer holds the first lines, one marker and the newest lines", TestTags.Positive, () =>
            {
                BoundedTextBuffer buffer = new BoundedTextBuffer(200);
                for (int i = 0; i < 1000; i++) buffer.AppendLine("line " + i);
                string text = buffer.ToString();
                AssertTrue(text.StartsWith("line 0", StringComparison.Ordinal), "head kept");
                AssertTrue(text.EndsWith("line 999", StringComparison.Ordinal), "newest line kept");
                AssertEqual(1, CountOf(text, Armada.Core.Services.ProtocolLines.TruncationMarker), "one marker");
                AssertTrue(text.Length <= 200, "bounded: " + text.Length);
            }));

            cases.Add(Case("append_cost_does_not_grow_with_the_cap", "6,000 lines into a 256 KiB buffer finish well under the time of the previous full-copy trim", TestTags.Reliability, () =>
            {
                const int max = 262144;
                string line = new string('x', 120);
                Stopwatch ring = Stopwatch.StartNew();
                BoundedTextBuffer buffer = new BoundedTextBuffer(max);
                for (int i = 0; i < 6000; i++) buffer.AppendLine(line);
                ring.Stop();

                Stopwatch previous = Stopwatch.StartNew();
                StringBuilder reference = new StringBuilder();
                for (int i = 0; i < 6000; i++)
                {
                    if (reference.Length > 0) reference.AppendLine();
                    reference.Append(line);
                    PreviousTrim(reference, max);
                }
                previous.Stop();

                AssertEqual(reference.ToString(), buffer.ToString(), "same text");
                Console.WriteLine("  ring buffer " + ring.ElapsedMilliseconds + " ms, previous trim " + previous.ElapsedMilliseconds + " ms");
                AssertTrue(ring.ElapsedMilliseconds * 5 < Math.Max(previous.ElapsedMilliseconds, 50), "ring " + ring.ElapsedMilliseconds + " ms vs previous " + previous.ElapsedMilliseconds + " ms");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Bounded text buffer", cases: cases);
        }

        // The 1.4.0 algorithm (StringBuilder trimmed by a full ToString on every append past the cap), as the reference.
        private static void PreviousTrim(StringBuilder builder, int maxChars)
        {
            if (builder.Length <= maxChars) return;
            int markerLength = _Marker.Length;
            int headChars = Math.Max(0, (maxChars - markerLength) / 2);
            int tailChars = Math.Max(0, maxChars - markerLength - headChars);
            string current = builder.ToString();
            string head = headChars > 0 ? current.Substring(0, Math.Min(headChars, current.Length)) : String.Empty;
            string tail = tailChars > 0 ? current.Substring(Math.Max(0, current.Length - tailChars)) : String.Empty;
            builder.Clear();
            builder.Append(head);
            if (builder.Length + markerLength + tail.Length > maxChars && tail.Length > 0)
            {
                int allowedTail = Math.Max(0, maxChars - builder.Length - markerLength);
                tail = tail.Substring(Math.Max(0, tail.Length - allowedTail));
            }
            builder.Append(_Marker);
            builder.Append(tail);
        }

        private static int CountOf(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; }, tags: new List<string> { tag });
        }
    }
}
