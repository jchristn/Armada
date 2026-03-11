namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Test.Common;

    public class ProgressParserTests : TestSuite
    {
        public override string Name => "Progress Parser";

        protected override async Task RunTestsAsync()
        {
            await RunTest("TryParse Null ReturnsNull", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse(null!);
                AssertNull(result);
            });

            await RunTest("TryParse Empty ReturnsNull", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("");
                AssertNull(result);
            });

            await RunTest("TryParse NoSignal ReturnsNull", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("Just a regular log line");
                AssertNull(result);
            });

            await RunTest("TryParse ProgressSignal ParsesPercentage", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:PROGRESS] 75");
                AssertNotNull(result);
                AssertEqual("progress", result!.Type);
                AssertEqual("75", result.Value);
                AssertEqual(75, result.Percentage);
            });

            await RunTest("TryParse ProgressSignal WithPercentSign", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:PROGRESS] 50%");
                AssertNotNull(result);
                AssertEqual(50, result!.Percentage);
            });

            await RunTest("TryParse ProgressSignal ClampsTo0", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:PROGRESS] -10");
                AssertNotNull(result);
                AssertEqual(0, result!.Percentage);
            });

            await RunTest("TryParse ProgressSignal ClampsTo100", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:PROGRESS] 150");
                AssertNotNull(result);
                AssertEqual(100, result!.Percentage);
            });

            await RunTest("TryParse StatusSignal ParsesEnum", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:STATUS] Testing");
                AssertNotNull(result);
                AssertEqual("status", result!.Type);
                AssertEqual(MissionStatusEnum.Testing, result.MissionStatus);
            });

            await RunTest("TryParse StatusSignal Review", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:STATUS] Review");
                AssertNotNull(result);
                AssertEqual(MissionStatusEnum.Review, result!.MissionStatus);
            });

            await RunTest("TryParse StatusSignal CaseInsensitive", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[armada:status] testing");
                AssertNotNull(result);
                AssertEqual("status", result!.Type);
                AssertEqual(MissionStatusEnum.Testing, result.MissionStatus);
            });

            await RunTest("TryParse StatusSignal InvalidEnum NoMissionStatus", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:STATUS] InvalidState");
                AssertNotNull(result);
                AssertEqual("status", result!.Type);
                AssertNull(result.MissionStatus);
            });

            await RunTest("TryParse MessageSignal ParsesValue", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("[ARMADA:MESSAGE] Running unit tests now");
                AssertNotNull(result);
                AssertEqual("message", result!.Type);
                AssertEqual("Running unit tests now", result.Value);
                AssertNull(result.Percentage);
                AssertNull(result.MissionStatus);
            });

            await RunTest("TryParse EmbeddedInOutput StillMatches", () =>
            {
                ProgressParser.ProgressSignal? result = ProgressParser.TryParse("some prefix [ARMADA:PROGRESS] 30");
                AssertNotNull(result);
                AssertEqual(30, result!.Percentage);
            });
        }
    }
}
