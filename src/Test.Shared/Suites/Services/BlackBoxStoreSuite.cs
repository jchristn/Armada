namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box storage: the codec (gzip and identity, SHA-256), DatabaseBlackBoxStore ranged and whole reads, SHA mismatch detection, missing bytes, the key layout and the factory.
    /// </summary>
    public sealed class BlackBoxStoreSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxStore";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("codec_roundtrip", "gzip and identity encode and decode; the SHA-256 is over the stored bytes", TestTags.Positive, () =>
            {
                byte[] raw = Encoding.UTF8.GetBytes(new string('a', 10000) + "é");
                BlackBoxEncodedObject gz = BlackBoxObjectCodec.Encode(raw, true);
                AssertEqual("gzip", gz.ContentEncoding); AssertTrue(gz.Data.Length < raw.Length, "compressed"); AssertEqual(raw.LongLength, gz.UncompressedBytes);
                AssertEqual(BlackBoxObjectCodec.Sha256Hex(gz.Data), gz.Sha256, "hash of stored bytes");
                AssertTrue(raw.SequenceEqual(BlackBoxObjectCodec.Decode(gz.Data, "gzip")), "gzip round trip");
                BlackBoxEncodedObject id = BlackBoxObjectCodec.Encode(raw, false);
                AssertEqual("identity", id.ContentEncoding); AssertTrue(raw.SequenceEqual(BlackBoxObjectCodec.Decode(id.Data, "identity")), "identity");
                AssertEqual(0, BlackBoxObjectCodec.Decode(BlackBoxObjectCodec.Encode(Array.Empty<byte>(), true).Data, "gzip").Length, "empty");
                AssertThrows<BlackBoxObjectCorruptException>(() => BlackBoxObjectCodec.Decode(new byte[] { 1, 2, 3 }, "gzip"), "bad gzip");
                return Task.CompletedTask;
            }));

            cases.Add(Case("sha_mismatch_detected", "Tampered bytes in the database read as BlackBoxObjectCorruptException; cleared bytes as missing", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                BlackBoxEntry entry = await t.Driver.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_s", "msn_sha")).ConfigureAwait(false);
                foreach (bool compress in new[] { true, false })
                {
                    string name = compress ? "evidence/gz.txt" : "evidence/plain.txt";
                    BlackBoxObject obj = await bb.Objects.WriteAsync(entry, name, "text/plain", Encoding.UTF8.GetBytes("hello black box"), compress).ConfigureAwait(false);
                    BlackBoxObjectReadResult ok = await bb.Objects.ReadAsync(entry, name).ConfigureAwait(false);
                    AssertEqual("hello black box", Encoding.UTF8.GetString(ok.Data)); AssertTrue(ok.Verified, "verified");
                    BlackBoxObjectReadResult range = await bb.Objects.ReadAsync(entry, name, 6, 5).ConfigureAwait(false);
                    AssertEqual("black", Encoding.UTF8.GetString(range.Data), compress ? "range of a gzip object" : "range cut in SQL");
                    AssertEqual(15L, range.TotalBytes, "total bytes");
                    byte[] stored = (await t.Driver.BlackBoxObjects.ReadByIdAsync(obj.Id, true).ConfigureAwait(false))!.Data!;
                    stored[stored.Length - 1] ^= 0xFF;
                    await t.Driver.BlackBoxObjects.WriteDataAsync(obj.Id, stored).ConfigureAwait(false);
                    await AssertThrowsAsync<BlackBoxObjectCorruptException>(() => bb.Objects.ReadAsync(entry, name), name + " tampered").ConfigureAwait(false);
                    await t.Driver.BlackBoxObjects.WriteDataAsync(obj.Id, null).ConfigureAwait(false);
                    await AssertThrowsAsync<BlackBoxObjectMissingException>(() => bb.Objects.ReadAsync(entry, name), name + " cleared").ConfigureAwait(false);
                }
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Objects.ReadAsync(entry, "nope"), "unknown object").ConfigureAwait(false);
                DatabaseBlackBoxStore store = new DatabaseBlackBoxStore(t.Driver);
                AssertTrue((await store.CheckHealthAsync().ConfigureAwait(false)).Healthy, "health");
                await AssertThrowsAsync<BlackBoxObjectMissingException>(() => store.WriteAsync(new BlackBoxObjectWrite { Reference = new BlackBoxObjectRef { ObjectId = "bbo_none", Name = "x" }, Data = new byte[] { 1 } }), "write needs the row").ConfigureAwait(false);
            }));

            cases.Add(Case("key_layout_and_factory", "Keys follow prefix/tenant/vessel/yyyy/MM/entry/name and cannot escape; the factory serves Database and refuses the others clearly", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string prefix = BlackBoxKeyLayout.BuildEntryPrefix("armada/blackbox", "ten_a", null, new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc), "bbx_1");
                AssertEqual("armada/blackbox/ten_a/_/2026/03/bbx_1", prefix);
                AssertEqual("armada/blackbox/ten_a/_/2026/03/bbx_1/evidence/check-1.txt", BlackBoxKeyLayout.BuildKey(prefix, "evidence/check-1.txt"));
                AssertEqual(prefix + "/_/_/etc/passwd", BlackBoxKeyLayout.BuildKey(prefix, "../..\\etc/passwd"), "no traversal");
                AssertEqual("a_b", BlackBoxKeyLayout.Segment("a b"));
                BlackBoxStoreFactory factory = new BlackBoxStoreFactory(t.Driver, new BlackBoxSettings());
                AssertTrue(factory.Get(BlackBoxStorageTargetEnum.Database) is DatabaseBlackBoxStore, "database");
                // Unconfigured external targets are refused with the settings that are missing; an unknown target is not supported.
                foreach (BlackBoxStorageTargetEnum target in new[] { BlackBoxStorageTargetEnum.Disk, BlackBoxStorageTargetEnum.S3, BlackBoxStorageTargetEnum.AzureBlob, BlackBoxStorageTargetEnum.GoogleCloud, BlackBoxStorageTargetEnum.Cifs, BlackBoxStorageTargetEnum.Nfs })
                {
                    BlackBoxStoreUnavailableException? caught = null;
                    try { factory.Get(target); } catch (BlackBoxStoreUnavailableException e) { caught = e; }
                    AssertNotNull(caught, target + " refused");
                    AssertContains("misconfigured", caught!.Message, target + " says why");
                }
                NotSupportedException? unknown = null;
                try { factory.Get(BlackBoxStorageTargetEnum.Unknown); } catch (NotSupportedException e) { unknown = e; }
                AssertNotNull(unknown, "Unknown refused");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box storage", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
