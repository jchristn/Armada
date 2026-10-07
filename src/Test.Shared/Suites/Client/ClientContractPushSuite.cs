namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for push devices: register (and refresh), list, update, test push
    /// (through the fixture's recording transport), and delete.
    /// </summary>
    public sealed class ClientContractPushSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Push";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("devices", "Push devices: register, refresh, list, update, test, delete", async (c, fx) =>
            {
                string token = "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";
                PushDeviceRegisterRequest register = new PushDeviceRegisterRequest { Platform = PushPlatformEnum.Android, ExpoPushToken = token, DeviceName = "contract" };
                PushDevice created = (await c.RegisterPushDeviceAsync(register))!;
                AssertTrue(created.Id.StartsWith("pdv_", StringComparison.Ordinal), "created id");
                AssertTrue(created.ExpoPushToken.Contains("****"), "masked token");
                PushDevice refreshed = (await c.RegisterPushDeviceAsync(register))!;
                AssertEqual(created.Id, refreshed.Id, "refresh keeps the device");

                List<PushDevice> listed = (await c.ListPushDevicesAsync())!;
                AssertTrue(listed.Any(d => d.Id == created.Id), "listed");

                PushDevice updated = (await c.UpdatePushDeviceAsync(created.Id, new PushDeviceUpdateRequest { DeviceName = "renamed", Categories = new List<PushCategoryEnum> { PushCategoryEnum.VoyageFinished } }))!;
                AssertEqual("renamed", updated.DeviceName, "renamed");
                AssertEqual("VoyageFinished", String.Join(",", updated.Categories), "categories");

                PushTestResult test = (await c.TestPushDeviceAsync(created.Id))!;
                AssertEqual(PushTestStatusEnum.Sent, test.Status, test.Message ?? "");
                AssertTrue(fx.PushTransport.Messages.Any(m => m.To == token), "the recording transport received the test push");

                await c.DeletePushDeviceAsync(created.Id);
                await ClientContract.ExpectErrorAsync(() => c.TestPushDeviceAsync(created.Id), "deleted", 404, 404);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: push devices", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body);
        }

        #endregion
    }
}
