namespace Armada.Test.Unit
{
    using Armada.Test.Common;
    using Armada.Test.Unit.Suites.Database;
    using Armada.Test.Unit.Suites.Models;
    using Armada.Test.Unit.Suites.Services;

    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            bool noCleanup = args.Contains("--no-cleanup");

            TestRunner runner = new TestRunner("ARMADA UNIT TEST SUITE");

            // Database tests
            runner.AddSuite(new FleetDatabaseTests());
            runner.AddSuite(new VesselDatabaseTests());
            runner.AddSuite(new VesselTests());
            runner.AddSuite(new CaptainDatabaseTests());
            runner.AddSuite(new CaptainTests());
            runner.AddSuite(new MissionDatabaseTests());
            runner.AddSuite(new VoyageDatabaseTests());
            runner.AddSuite(new DockDatabaseTests());
            runner.AddSuite(new SignalDatabaseTests());
            runner.AddSuite(new EventDatabaseTests());
            runner.AddSuite(new EventTests());
            runner.AddSuite(new EnumerationTests());
            runner.AddSuite(new ForeignKeyTests());
            runner.AddSuite(new ConcurrentAccessTests());
            runner.AddSuite(new DatabaseInitializationTests());
            runner.AddSuite(new SchemaMigrationTests());
            runner.AddSuite(new EdgeCaseTests());
            runner.AddSuite(new TenantMethodsTests());
            runner.AddSuite(new UserMethodsTests());
            runner.AddSuite(new CredentialMethodsTests());
            runner.AddSuite(new TenantFencingTests());
            runner.AddSuite(new EntityTenantScopedTests());
            runner.AddSuite(new DefaultSeedingTests());
            runner.AddSuite(new TenantScopedEnumerationTests());
            runner.AddSuite(new TenantScopedPaginationTests());
            runner.AddSuite(new TenantScopedPaginationTests2());

            // Model tests
            runner.AddSuite(new FleetModelTests());
            runner.AddSuite(new VesselModelTests());
            runner.AddSuite(new CaptainModelTests());
            runner.AddSuite(new MissionModelTests());
            runner.AddSuite(new VoyageModelTests());
            runner.AddSuite(new DockModelTests());
            runner.AddSuite(new SignalModelTests());
            runner.AddSuite(new ArmadaEventModelTests());
            runner.AddSuite(new ArmadaStatusModelTests());
            runner.AddSuite(new EnumModelTests());
            runner.AddSuite(new TenantMetadataTests());
            runner.AddSuite(new UserMasterTests());
            runner.AddSuite(new CredentialTests());
            runner.AddSuite(new AuthContextTests());

            // Service tests
            runner.AddSuite(new AdmiralServiceTests());
            runner.AddSuite(new EntityResolverTests());
            runner.AddSuite(new MessageTemplateServiceTests());
            runner.AddSuite(new ProgressParserTests());
            runner.AddSuite(new SettingsTests());
            runner.AddSuite(new GitServiceTests());
            runner.AddSuite(new GitInferenceTests());
            runner.AddSuite(new LogRotationServiceTests());
            runner.AddSuite(new DataExpiryServiceTests());
            runner.AddSuite(new NotificationServiceTests());
            runner.AddSuite(new RuntimeDetectionServiceTests());
            runner.AddSuite(new MissionPromptTests());
            runner.AddSuite(new SequentialDispatchTests());
            runner.AddSuite(new MissionStatusTransitionTests());
            runner.AddSuite(new LandingPipelineTests());
            runner.AddSuite(new SessionTokenServiceTests());
            runner.AddSuite(new AuthenticationServiceTests());
            runner.AddSuite(new AuthorizationConfigTests());
            runner.AddSuite(new AuthorizationServiceTests());
            runner.AddSuite(new AuthEndpointTests());

            int exitCode = await runner.RunAllAsync().ConfigureAwait(false);
            return exitCode;
        }
    }
}
