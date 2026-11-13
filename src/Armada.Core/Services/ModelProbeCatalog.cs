namespace Armada.Core.Services
{
    using System;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// The Quick probe set: the prompts each mechanical probe sends through the captain's runtime. The prompts use
    /// Armada's real output contracts (the Worker completion line, <see cref="JudgeContract.OutputContract"/> and
    /// <see cref="ArchitectPlanParser.FormatInstructions"/>) and its real ASCII rules. Change
    /// <see cref="ProbeSetVersion"/> whenever a prompt or a scoring rule changes, so runs stay comparable.
    /// </summary>
    public static class ModelProbeCatalog
    {
        #region Public-Members

        /// <summary>
        /// Version of the probe set (prompts and scoring rules).
        /// </summary>
        public const string ProbeSetVersion = "quick-2";

        /// <summary>
        /// Fence info string of the tool-calling probe's answer block.
        /// </summary>
        public const string ToolAnswerFence = "armada-probe";

        /// <summary>
        /// Repeats of the determinism probe.
        /// </summary>
        public const int DeterminismRepeats = 3;

        /// <summary>
        /// Probes in the quick set, and so the most results one run records (output contract, served context, ASCII,
        /// tool calling, speed, determinism).
        /// </summary>
        public const int ProbeCount = 6;

        /// <summary>
        /// Approximate input tokens of the small speed exchange.
        /// </summary>
        public const int SpeedSmallInputTokens = 4000;

        /// <summary>
        /// Approximate input tokens of the large speed exchange.
        /// </summary>
        public const int SpeedLargeInputTokens = 32000;

        /// <summary>
        /// Output tokens a probe answer is assumed to need when estimating its cost before it runs.
        /// </summary>
        public const int AssumedOutputTokens = 1500;

        /// <summary>
        /// Armada's ASCII rules, as given to every captain.
        /// </summary>
        public const string AsciiRules =
            "Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output. " +
            "Do not use ANSI color codes or terminal formatting in output -- keep all output plain text.";

        #endregion

        #region Private-Members

        private const string _Preamble =
            "This is an Armada evaluation probe, not a real mission. Work only in your current working directory. ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Worker output-contract prompt.
        /// </summary>
        /// <returns>Prompt.</returns>
        public static string WorkerContractPrompt()
        {
            return _Preamble + "Do not call any tools and do not modify any files.\n\n" +
                "You are an Armada worker agent. Task: explain in two plain sentences what a git worktree is.\n\n" +
                "End with a standalone [ARMADA:RESULT] COMPLETE line followed by a brief plain-text summary.\n" + AsciiRules;
        }

        /// <summary>
        /// Judge output-contract prompt.
        /// </summary>
        /// <returns>Prompt.</returns>
        public static string JudgeContractPrompt()
        {
            return _Preamble + "Do not call any tools and do not modify any files.\n\n" +
                "You are an Armada judge agent. Acceptance criterion: Add(a, b) returns the sum of a and b.\n" +
                "The worker's complete change:\n\n" +
                "```diff\n" +
                "-        public static int Add(int a, int b) => a - b;\n" +
                "+        public static int Add(int a, int b) => a + b;\n" +
                "```\n\n" +
                "The worker reports that the existing test AddReturnsSum now passes.\n\n" +
                JudgeContract.OutputContract() + "\n" + AsciiRules;
        }

        /// <summary>
        /// Architect output-contract prompt.
        /// </summary>
        /// <returns>Prompt.</returns>
        public static string ArchitectContractPrompt()
        {
            return _Preamble + "Do not call any tools and do not modify any files.\n\n" +
                "You are an Armada architect agent. Objective (REQ-1): add a --version flag to a small C# command-line tool " +
                "that prints the assembly version and exits with code 0. The tool is built with `dotnet build` and tested with `dotnet test`.\n\n" +
                ArchitectPlanParser.FormatInstructions() + "\n" + AsciiRules;
        }

        /// <summary>
        /// ASCII and format rules prompt: a task that tempts em dashes, smart quotes, non-ASCII letters and ANSI codes.
        /// </summary>
        /// <returns>Prompt.</returns>
        public static string AsciiPrompt()
        {
            return _Preamble + "Do not call any tools and do not modify any files.\n\n" +
                "Write a short release announcement (about 120 words) for a coffee shop app. Include: a customer testimonial in " +
                "quotation marks, a date range from 2019 to 2024, an aside set off with dashes, the French word for coffee shop, " +
                "a trailing ellipsis, a temperature in degrees, and an example of how a green OK status looks in a colored terminal.\n\n" +
                AsciiRules;
        }

        /// <summary>
        /// Tool-calling prompt: single, sequential and parallel calls, a failing call and a large result.
        /// </summary>
        /// <param name="task">Per-run values.</param>
        /// <returns>Prompt.</returns>
        /// <exception cref="ArgumentNullException">Thrown when task is null.</exception>
        public static string ToolCallingPrompt(ModelProbeToolTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            return _Preamble + "Use your tools (shell commands or file tools) for every step; do not compute hashes in your head.\n\n" +
                "1. Single call: compute the SHA-256 hex digest of the exact UTF-8 text `" + task.SingleText + "` (no trailing newline).\n" +
                "2. Sequential calls: write the exact text `" + task.SequentialText + "` (no trailing newline) to a file named " +
                "`armada-probe-seq.txt`, then compute the SHA-256 hex digest of that file.\n" +
                "3. Parallel calls (call the tools in parallel if you can): compute the SHA-256 hex digests of the texts `" +
                task.ParallelTextA + "` and `" + task.ParallelTextB + "` (no trailing newline each).\n" +
                "4. Error: read the file `" + task.MissingFileName + "`. It does not exist, so the tool returns an error. Do not create it and do not retry more than once; continue with step 5.\n" +
                "5. Large result: write a file named `armada-probe-large.txt` with " + task.LargeLineCount + " lines, where line N is `line-N-" +
                task.LargeMarker + "`, then read the whole file back with a tool and take line " + task.LargeLineNumber + " from what you read.\n\n" +
                "Finish with exactly one fenced ```" + ToolAnswerFence + " JSON block and nothing after it, with these fields: " +
                "{\"single\":\"<hex>\",\"sequential\":\"<hex>\",\"parallelA\":\"<hex>\",\"parallelB\":\"<hex>\"," +
                "\"missingFileError\":true or false,\"missingFileErrorText\":\"<the error the tool returned>\"," +
                "\"largeLine\":\"<line " + task.LargeLineNumber + ">\",\"largeLineCount\":<number of lines you read>}.\n" + AsciiRules;
        }

        /// <summary>
        /// Speed prompt with roughly the given number of input tokens.
        /// </summary>
        /// <param name="inputTokens">Approximate input tokens (four characters each).</param>
        /// <returns>Prompt.</returns>
        public static string SpeedPrompt(int inputTokens)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(_Preamble).Append("Do not call any tools and do not modify any files.\n\nInventory records:\n");
            int targetChars = Math.Max(1000, inputTokens) * 4;
            int record = 1;
            while (builder.Length < targetChars)
            {
                builder.Append("Record ").Append(record.ToString("D5")).Append(": crate of ")
                    .Append(record % 97).Append(" brass fittings, shelf ").Append(record % 13)
                    .Append(", checked by station ").Append(record % 7).Append(".\n");
                record++;
            }

            builder.Append("\nTask: reply with READY on the first line, then exactly ten short numbered lines, each naming one record " +
                "number from above and its shelf.\n").Append(AsciiRules);
            return builder.ToString();
        }

        /// <summary>
        /// Determinism prompt (the same short task each repeat).
        /// </summary>
        /// <returns>Prompt.</returns>
        public static string DeterminismPrompt()
        {
            return _Preamble + "Do not call any tools and do not modify any files.\n\n" +
                "Reply with exactly one line that starts with ANSWER: followed by the first five prime numbers greater than 100, " +
                "comma-separated with no spaces. Do not include an explanation, Markdown, or any other text.\n" + AsciiRules;
        }

        /// <summary>
        /// Estimated tokens of a text (characters divided by four, rounded up).
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Tokens.</returns>
        public static long EstimateTokens(string? text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            return (text.Length + 3) / 4;
        }

        #endregion
    }
}
