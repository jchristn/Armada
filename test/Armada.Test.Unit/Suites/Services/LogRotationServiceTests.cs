namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Services;
    using Armada.Test.Common;
    using SyslogLogging;

    public class LogRotationServiceTests : TestSuite
    {
        public override string Name => "Log Rotation Service";

        private (string TestDir, LogRotationService Service) CreateTestContext()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            LogRotationService service = new LogRotationService(logging, 100, 3); // 100 bytes max, 3 files max
            return (testDir, service);
        }

        private void CleanupTestDir(string testDir)
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("RotateIfNeeded UnderThreshold NoRotation", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    string filePath = Path.Combine(testDir, "small.log");
                    File.WriteAllText(filePath, "short");

                    service.RotateIfNeeded(filePath);

                    AssertTrue(File.Exists(filePath));
                    AssertFalse(File.Exists(filePath + ".1"));
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });

            await RunTest("RotateIfNeeded OverThreshold RotatesFile", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    string filePath = Path.Combine(testDir, "large.log");
                    File.WriteAllText(filePath, new string('x', 200));

                    service.RotateIfNeeded(filePath);

                    AssertFalse(File.Exists(filePath));
                    AssertTrue(File.Exists(filePath + ".1"));
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });

            await RunTest("RotateIfNeeded ShiftsExistingFiles", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    string filePath = Path.Combine(testDir, "shift.log");

                    File.WriteAllText(filePath + ".1", "old-1");
                    File.WriteAllText(filePath + ".2", "old-2");

                    File.WriteAllText(filePath, new string('x', 200));

                    service.RotateIfNeeded(filePath);

                    AssertTrue(File.Exists(filePath + ".1"));
                    AssertTrue(File.Exists(filePath + ".2"));
                    AssertTrue(File.Exists(filePath + ".3"));
                    AssertEqual("old-1", File.ReadAllText(filePath + ".2"));
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });

            await RunTest("RotateIfNeeded NonExistentFile NoOp", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    service.RotateIfNeeded(Path.Combine(testDir, "nonexistent.log"));
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });

            await RunTest("RotateIfNeeded NullPath NoOp", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    service.RotateIfNeeded(null!);
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });

            await RunTest("RotateIfNeeded EmptyPath NoOp", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    service.RotateIfNeeded("");
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });

            await RunTest("RotateAllInDirectory RotatesLargeFiles", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    File.WriteAllText(Path.Combine(testDir, "a.log"), new string('x', 200));
                    File.WriteAllText(Path.Combine(testDir, "b.log"), "small");

                    service.RotateAllInDirectory(testDir);

                    AssertTrue(File.Exists(Path.Combine(testDir, "a.log.1")));
                    AssertTrue(File.Exists(Path.Combine(testDir, "b.log")));
                    AssertFalse(File.Exists(Path.Combine(testDir, "b.log.1")));
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });

            await RunTest("RotateAllInDirectory NonExistentDir NoOp", () =>
            {
                (string testDir, LogRotationService service) = CreateTestContext();
                try
                {
                    service.RotateAllInDirectory("/nonexistent/path");
                }
                finally
                {
                    CleanupTestDir(testDir);
                }
            });
        }
    }
}
