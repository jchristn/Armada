namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class TenantMetadataTests : TestSuite
    {
        public override string Name => "TenantMetadata Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("TenantMetadata DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                AssertStartsWith(Constants.TenantIdPrefix, tenant.Id);
            });

            await RunTest("TenantMetadata DefaultConstructor SetsDefaultName", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                AssertEqual("My Tenant", tenant.Name);
            });

            await RunTest("TenantMetadata DefaultConstructor IsActive", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                AssertTrue(tenant.Active);
            });

            await RunTest("TenantMetadata DefaultConstructor SetsTimestamps", () =>
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-1);
                TenantMetadata tenant = new TenantMetadata();
                DateTime after = DateTime.UtcNow.AddSeconds(1);

                AssertTrue(tenant.CreatedUtc >= before && tenant.CreatedUtc <= after, "CreatedUtc should be recent");
                AssertTrue(tenant.LastUpdateUtc >= before && tenant.LastUpdateUtc <= after, "LastUpdateUtc should be recent");
            });

            await RunTest("TenantMetadata NameConstructor SetsName", () =>
            {
                TenantMetadata tenant = new TenantMetadata("Acme Corp");
                AssertEqual("Acme Corp", tenant.Name);
            });

            await RunTest("TenantMetadata NameConstructor StillGeneratesId", () =>
            {
                TenantMetadata tenant = new TenantMetadata("Test Tenant");
                AssertStartsWith(Constants.TenantIdPrefix, tenant.Id);
            });

            await RunTest("TenantMetadata SetId Null Throws", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                AssertThrows<ArgumentNullException>(() => tenant.Id = null!);
            });

            await RunTest("TenantMetadata SetId Empty Throws", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                AssertThrows<ArgumentNullException>(() => tenant.Id = "");
            });

            await RunTest("TenantMetadata SetName Null Throws", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                AssertThrows<ArgumentNullException>(() => tenant.Name = null!);
            });

            await RunTest("TenantMetadata SetName Empty Throws", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                AssertThrows<ArgumentNullException>(() => tenant.Name = "");
            });

            await RunTest("TenantMetadata NameConstructor Null Throws", () =>
            {
                AssertThrows<ArgumentNullException>(() => new TenantMetadata(null!));
            });

            await RunTest("TenantMetadata SetId ValidValue Succeeds", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                tenant.Id = "ten_custom";
                AssertEqual("ten_custom", tenant.Id);
            });

            await RunTest("TenantMetadata SetActive ToFalse", () =>
            {
                TenantMetadata tenant = new TenantMetadata();
                tenant.Active = false;
                AssertFalse(tenant.Active);
            });

            await RunTest("TenantMetadata UniqueIds AcrossInstances", () =>
            {
                TenantMetadata t1 = new TenantMetadata();
                TenantMetadata t2 = new TenantMetadata();
                AssertNotEqual(t1.Id, t2.Id);
            });

            await RunTest("TenantMetadata Serialization RoundTrip", () =>
            {
                TenantMetadata tenant = new TenantMetadata("Serialization Test");
                tenant.Active = false;

                string json = JsonSerializer.Serialize(tenant);
                TenantMetadata deserialized = JsonSerializer.Deserialize<TenantMetadata>(json)!;

                AssertEqual(tenant.Id, deserialized.Id);
                AssertEqual(tenant.Name, deserialized.Name);
                AssertEqual(tenant.Active, deserialized.Active);
            });
        }
    }
}
