namespace Armada.Test.Runtimes.Suites
{
    using Armada.Runtimes;
    using Armada.Test.Common;
    using SyslogLogging;

    public class GeminiRuntimeTests : TestSuite
    {
        public override string Name => "Gemini Runtime Tests";

        private sealed class InspectableGeminiRuntime : GeminiRuntime
        {
            public InspectableGeminiRuntime(LoggingModule logging) : base(logging)
            {
            }

            public string Command() => GetCommand();

            public List<string> Args(string prompt, string? model = null) => BuildArguments(prompt, model);
        }

        private InspectableGeminiRuntime CreateRuntime()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new InspectableGeminiRuntime(logging);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("ApprovalMode Default Is Yolo", () =>
            {
                InspectableGeminiRuntime runtime = CreateRuntime();
                AssertEqual("yolo", runtime.ApprovalMode);
            });

            await RunTest("BuildArguments Uses Prompt And ApprovalMode", () =>
            {
                InspectableGeminiRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt");
                AssertEqual("-p", args[0]);
                AssertEqual("test prompt", args[1]);
                AssertTrue(args.Contains("--approval-mode"));
                AssertTrue(args.Contains("yolo"));
            });

            await RunTest("BuildArguments Includes Model When Supplied", () =>
            {
                InspectableGeminiRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt", "gemini-2.5-pro");
                int modelIndex = args.IndexOf("--model");
                AssertTrue(modelIndex >= 0);
                AssertEqual("gemini-2.5-pro", args[modelIndex + 1]);
            });

            await RunTest("Windows Command Resolves Cmd Wrapper", () =>
            {
                InspectableGeminiRuntime runtime = CreateRuntime();
                string command = runtime.Command();

                if (OperatingSystem.IsWindows())
                    AssertTrue(command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || command.Equals("gemini", StringComparison.OrdinalIgnoreCase), "Expected gemini command to resolve to .cmd or gemini");
                else
                    AssertEqual("gemini", command);
            });
        }
    }
}
