namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for handoff artifacts: list a mission's artifacts (with paging) and
    /// read one, including the error replies for unknown ids.
    /// </summary>
    public sealed class ClientContractHandoffArtifactSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.HandoffArtifacts";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(ClientContract.Case(Suite, this, "artifacts", "Handoff artifacts: list a mission's artifacts with paging, get one, unknown ids fail", async (c, fx) =>
            {
                Mission mission = await SeedAsync(fx).ConfigureAwait(false);

                EnumerationResult<HandoffArtifact> page = (await c.HandoffArtifacts.ListForMissionAsync(mission.Id))!;
                AssertEqual(2, (int)page.TotalRecords, "both artifacts");
                AssertTrue(page.Objects.All(a => a.Output == null), "list leaves out output");
                EnumerationResult<HandoffArtifact> second = (await c.HandoffArtifacts.ListForMissionAsync(mission.Id, 2, 1))!;
                AssertEqual(1, second.PageSize, "page size sent");
                AssertEqual(2, second.PageNumber, "page number sent");
                string id = second.Objects.Single().Id;

                HandoffArtifact read = (await c.HandoffArtifacts.GetAsync(id))!;
                AssertEqual(mission.Id, read.MissionId, "get");
                AssertEqual("contract output", read.Output, "full output on get");
                AssertEqual("src/Contract.cs", read.Findings.Single().File, "finding");
                await ClientContract.ExpectErrorAsync(() => c.HandoffArtifacts.GetAsync("hoa_missing"), "get unknown", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.HandoffArtifacts.ListForMissionAsync("msn_missing_" + ClientContract.Suffix()), "list unknown mission", 404, 404);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: handoff artifacts", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Mission> SeedAsync(E2EServerFixture fx)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Mission mission = new Mission("[Judge] Contract " + ClientContract.Suffix(), "Contract review");
                mission.Persona = "Judge";
                mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                for (int i = 0; i < 2; i++)
                {
                    HandoffArtifact artifact = new HandoffArtifact { TenantId = mission.TenantId, MissionId = mission.Id, Persona = "Judge", Output = "contract output", OutputLength = 15, CreatedUtc = DateTime.UtcNow.AddMinutes(-i) };
                    artifact.Findings.Add(new HandoffFinding { Number = 1, File = "src/Contract.cs", Severity = HandoffFindingSeverityEnum.Blocking, Summary = "File: src/Contract.cs" });
                    await db.HandoffArtifacts.CreateAsync(artifact).ConfigureAwait(false);
                }

                return mission;
            }
        }

        #endregion
    }
}
