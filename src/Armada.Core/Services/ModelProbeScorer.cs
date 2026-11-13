namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Scores the answers of the Quick probes. Pure functions over the model's text, so they are tested with canned
    /// answers. Each result records an observation (counts and measured values), not a capacity.
    /// </summary>
    public static class ModelProbeScorer
    {
        #region Public-Members

        /// <summary>
        /// Lines repeated at least this many times in one answer count as a loop.
        /// </summary>
        public const int LoopRepeatThreshold = 25;

        #endregion

        #region Private-Members

        private static readonly Regex _WorkerResultLine = new Regex(
            @"^\[ARMADA:RESULT\][ \t]+COMPLETE[ \t]*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _LiteralAnsi = new Regex(
            @"(\\033|\\x1[bB]|\\e|\\u001[bB]|\\u001B|\^\[)\[",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _DeterminismAnswer = new Regex(
            @"^[ \t]*(?<values>[0-9]+(?:[ \t]*,[ \t]*[0-9]+){4})(?![ \t]*,[ \t]*[0-9])(?=$|[ \t.;])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly JsonSerializerOptions _AnswerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Served context: compare the context the endpoint serves with the configured value.
        /// </summary>
        /// <param name="configured">Configured context length, or null.</param>
        /// <param name="served">Context length the endpoint reported, or null when not observed.</param>
        /// <param name="couldNotRunReason">Why the probe cannot apply (for example a vendor CLI), or null.</param>
        /// <returns>The result.</returns>
        public static ModelProbeResult ScoreServedContext(int? configured, int? served, string? couldNotRunReason)
        {
            ModelProbeResult result = New(ModelProbeKindEnum.ServedContext);
            result.Metrics.ConfiguredContextTokens = configured;
            if (!String.IsNullOrWhiteSpace(couldNotRunReason)) return CouldNotRun(result, couldNotRunReason!);
            if (!served.HasValue || served.Value <= 0)
                return CouldNotRun(result, "The endpoint did not report the context it serves for this model.");

            result.Attempts = 1;
            result.Metrics.ServedContextTokens = served;
            if (!configured.HasValue)
            {
                result.Metrics.ContextMismatch = false;
                result.Outcome = ModelProbeOutcomeEnum.Passed;
                result.Summary = "Serves " + served.Value + " tokens of context; no context length is configured.";
                return result;
            }

            bool mismatch = configured.Value != served.Value;
            result.Metrics.ContextMismatch = mismatch;
            result.Outcome = mismatch ? ModelProbeOutcomeEnum.Failed : ModelProbeOutcomeEnum.Passed;
            result.Summary = mismatch
                ? "Configuration error: configured " + configured.Value + " tokens of context, the endpoint serves " + served.Value + "."
                : "Serves the configured " + served.Value + " tokens of context.";
            return result;
        }

        /// <summary>
        /// Output contract: exact compliance with the Worker completion line, the Judge verdict and lenses, and the
        /// Architect plan block, and how many answers would need repair.
        /// </summary>
        /// <param name="worker">Worker answer, or null when that exchange did not run.</param>
        /// <param name="judge">Judge answer, or null when that exchange did not run.</param>
        /// <param name="architect">Architect answer, or null when that exchange did not run.</param>
        /// <param name="couldNotRunReason">Why no exchange ran, used when all three are null.</param>
        /// <returns>The result.</returns>
        public static ModelProbeResult ScoreOutputContract(string? worker, string? judge, string? architect, string? couldNotRunReason = null)
        {
            ModelProbeResult result = New(ModelProbeKindEnum.OutputContract);
            if (worker == null && judge == null && architect == null)
                return CouldNotRun(result, couldNotRunReason ?? "No output-contract exchange could run.");

            int checkedCount = 0;
            int compliant = 0;
            List<string> repair = new List<string>();
            if (worker != null)
            {
                checkedCount++;
                bool ok = HasWorkerResultLine(worker);
                result.Metrics.WorkerResultCompliant = ok;
                if (ok) compliant++;
                else repair.Add("Worker");
            }

            if (judge != null)
            {
                checkedCount++;
                bool verdict = JudgeVerdictParser.Parse(judge) != JudgeVerdictEnum.None;
                bool lenses = JudgeContract.HasAllLenses(judge, out List<string> _);
                result.Metrics.JudgeVerdictCompliant = verdict;
                result.Metrics.JudgeSectionsCompliant = lenses;
                if (verdict && lenses) compliant++;
                else repair.Add("Judge");
            }

            if (architect != null)
            {
                checkedCount++;
                bool ok = ArchitectPlanParser.TryParse(architect, out ArchitectPlan? _, out string? _);
                result.Metrics.ArchitectPlanCompliant = ok;
                if (ok) compliant++;
                else repair.Add("Architect");
            }

            result.Attempts = checkedCount;
            result.Metrics.Checked = checkedCount;
            result.Metrics.Compliant = compliant;
            result.Metrics.RepairNeeded = repair.Count;
            result.Outcome = compliant == checkedCount ? ModelProbeOutcomeEnum.Passed : ModelProbeOutcomeEnum.Failed;
            result.Summary = compliant + " of " + checkedCount + " answers met the contract exactly"
                + (repair.Count == 0 ? "." : "; repair needed for " + String.Join(", ", repair) + ".");
            return result;
        }

        /// <summary>
        /// Whether an answer carries a standalone <c>[ARMADA:RESULT] COMPLETE</c> line outside any fenced block.
        /// </summary>
        /// <param name="output">Answer.</param>
        /// <returns>True when present.</returns>
        public static bool HasWorkerResultLine(string? output)
        {
            if (String.IsNullOrEmpty(output)) return false;
            bool inFence = false;
            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (!inFence && _WorkerResultLine.IsMatch(line)) return true;
            }

            return false;
        }

        /// <summary>
        /// ASCII and format rules: count em and en dashes, smart quotes, other non-ASCII characters and ANSI escape
        /// sequences (raw ESC characters and their written forms such as \033[ or \x1b[).
        /// </summary>
        /// <param name="output">Answer, or null when the exchange did not run.</param>
        /// <param name="couldNotRunReason">Why it did not run, or null.</param>
        /// <returns>The result.</returns>
        public static ModelProbeResult ScoreAscii(string? output, string? couldNotRunReason = null)
        {
            ModelProbeResult result = New(ModelProbeKindEnum.AsciiFormat);
            if (output == null) return CouldNotRun(result, couldNotRunReason ?? "The exchange could not run.");

            int dashes = 0;
            int quotes = 0;
            int other = 0;
            int ansi = 0;
            foreach (Rune rune in output.EnumerateRunes())
            {
                int value = rune.Value;
                if (value == 0x1B) ansi++;
                else if (value == 0x2012 || value == 0x2013 || value == 0x2014 || value == 0x2015) dashes++;
                else if (value == 0x2018 || value == 0x2019 || value == 0x201A || value == 0x201B
                    || value == 0x201C || value == 0x201D || value == 0x201E || value == 0x201F) quotes++;
                else if (value > 127) other++;
            }

            ansi += _LiteralAnsi.Matches(output).Count;
            int total = dashes + quotes + other + ansi;
            result.Attempts = 1;
            result.Metrics.EmDashes = dashes;
            result.Metrics.SmartQuotes = quotes;
            result.Metrics.OtherNonAscii = other;
            result.Metrics.AnsiCodes = ansi;
            result.Metrics.Violations = total;
            result.Outcome = total == 0 ? ModelProbeOutcomeEnum.Passed : ModelProbeOutcomeEnum.Failed;
            result.Summary = total == 0
                ? "No ASCII or format violations."
                : total + " violations: " + dashes + " em/en dashes, " + quotes + " smart quotes, " + other
                    + " other non-ASCII characters, " + ansi + " ANSI codes.";
            return result;
        }

        /// <summary>
        /// Tool calling: read the answer block and check each step against values the model can only get by calling a
        /// tool.
        /// </summary>
        /// <param name="output">Answer, or null when the exchange did not run.</param>
        /// <param name="task">The run's tool task.</param>
        /// <param name="timedOut">Whether the exchange hit its timeout.</param>
        /// <param name="couldNotRunReason">Why it did not run, or null.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when task is null.</exception>
        public static ModelProbeResult ScoreToolCalling(string? output, ModelProbeToolTask task, bool timedOut, string? couldNotRunReason = null)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            ModelProbeResult result = New(ModelProbeKindEnum.ToolCalling);
            if (output == null) return CouldNotRun(result, couldNotRunReason ?? "The exchange could not run.");

            result.Attempts = 1;
            bool looping = timedOut || HasRepeatedLines(output, LoopRepeatThreshold);
            result.Metrics.Looping = looping;
            const int steps = 6;
            result.Metrics.Checked = steps;

            ModelProbeToolAnswer? answer = ParseToolAnswer(output);
            if (answer == null)
            {
                result.Metrics.Compliant = 0;
                result.Metrics.MalformedCalls = 1;
                result.Metrics.ErrorRecovered = false;
                result.Outcome = ModelProbeOutcomeEnum.Failed;
                result.Summary = "No readable ```" + ModelProbeCatalog.ToolAnswerFence + " answer block"
                    + (timedOut ? "; timed out (possible tool loop)." : ".");
                return result;
            }

            int malformed = 0;
            int compliant = 0;
            List<string> wrong = new List<string>();
            CheckHash(answer.Single, task.SingleText, "single", ref compliant, ref malformed, wrong);
            CheckHash(answer.Sequential, task.SequentialText, "sequential", ref compliant, ref malformed, wrong);
            CheckHash(answer.ParallelA, task.ParallelTextA, "parallel A", ref compliant, ref malformed, wrong);
            CheckHash(answer.ParallelB, task.ParallelTextB, "parallel B", ref compliant, ref malformed, wrong);

            if (!answer.MissingFileError.HasValue) { malformed++; wrong.Add("error"); }
            else if (answer.MissingFileError.Value) compliant++;
            else wrong.Add("error");

            string expectedLine = "line-" + task.LargeLineNumber + "-" + task.LargeMarker;
            if (answer.LargeLine == null) { malformed++; wrong.Add("large result"); }
            else if (String.Equals(answer.LargeLine.Trim(), expectedLine, StringComparison.Ordinal)
                && (!answer.LargeLineCount.HasValue || answer.LargeLineCount.Value == task.LargeLineCount)) compliant++;
            else wrong.Add("large result");

            bool recovered = answer.MissingFileError == true && answer.LargeLine != null;
            result.Metrics.Compliant = compliant;
            result.Metrics.MalformedCalls = malformed;
            result.Metrics.ErrorRecovered = recovered;
            result.Outcome = compliant == steps && !looping ? ModelProbeOutcomeEnum.Passed : ModelProbeOutcomeEnum.Failed;
            result.Summary = compliant + " of " + steps + " tool steps correct"
                + (wrong.Count == 0 ? "" : " (wrong or missing: " + String.Join(", ", wrong) + ")")
                + "; " + malformed + " malformed; error " + (recovered ? "recovered" : "not recovered")
                + (looping ? "; looping or timed out." : ".");
            return result;
        }

        /// <summary>
        /// Read the last fenced armada-probe JSON block of an answer.
        /// </summary>
        /// <param name="output">Answer.</param>
        /// <returns>The answer, or null when absent or not valid JSON.</returns>
        public static ModelProbeToolAnswer? ParseToolAnswer(string? output)
        {
            if (String.IsNullOrEmpty(output)) return null;
            string? block = null;
            StringBuilder? current = null;
            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (current == null)
                {
                    if (line.StartsWith("```", StringComparison.Ordinal)
                        && String.Equals(line.Substring(3).Trim(), ModelProbeCatalog.ToolAnswerFence, StringComparison.OrdinalIgnoreCase))
                        current = new StringBuilder();
                }
                else if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    block = current.ToString();
                    current = null;
                }
                else
                {
                    current.Append(raw).Append('\n');
                }
            }

            if (String.IsNullOrWhiteSpace(block)) return null;
            try
            {
                return JsonSerializer.Deserialize<ModelProbeToolAnswer>(block, _AnswerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// SHA-256 hex digest (lowercase) of a UTF-8 text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Digest.</returns>
        public static string Sha256Hex(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? String.Empty));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Speed: time to first output and output tokens per second at about 4K and 32K input, and the inference timeouts
        /// they advise. Never used for stage-check timeouts.
        /// </summary>
        /// <param name="small">The 4K exchange, or null when it did not run.</param>
        /// <param name="large">The 32K exchange, or null when it did not run.</param>
        /// <param name="couldNotRunReason">Why neither ran, or null.</param>
        /// <returns>The result.</returns>
        public static ModelProbeResult ScoreSpeed(ModelProbeExchange? small, ModelProbeExchange? large, string? couldNotRunReason = null)
        {
            ModelProbeResult result = New(ModelProbeKindEnum.Speed);
            bool smallRan = Ran(small);
            bool largeRan = Ran(large);
            if (!smallRan && !largeRan)
                return CouldNotRun(result, couldNotRunReason ?? small?.CouldNotRunReason ?? large?.CouldNotRunReason ?? "No speed exchange could run.");

            List<string> notes = new List<string>();
            bool problem = false;
            long worstFirst = 0;
            double? slowest = null;
            if (smallRan)
            {
                result.Attempts++;
                Measure(small!, out long first, out double? tps);
                result.Metrics.FirstOutputMs4k = first;
                result.Metrics.OutputTokensPerSecond4k = tps;
                worstFirst = Math.Max(worstFirst, first);
                if (tps.HasValue) slowest = slowest.HasValue ? Math.Min(slowest.Value, tps.Value) : tps.Value;
                notes.Add("4K: first output " + first + " ms" + (tps.HasValue ? ", " + Format(tps.Value) + " tokens/s" : ""));
                if (small!.TimedOut || String.IsNullOrWhiteSpace(small.Output)) { problem = true; notes.Add("4K produced no answer in time"); }
            }

            if (largeRan)
            {
                result.Attempts++;
                Measure(large!, out long first, out double? tps);
                result.Metrics.FirstOutputMs32k = first;
                result.Metrics.OutputTokensPerSecond32k = tps;
                worstFirst = Math.Max(worstFirst, first);
                if (tps.HasValue) slowest = slowest.HasValue ? Math.Min(slowest.Value, tps.Value) : tps.Value;
                notes.Add("32K: first output " + first + " ms" + (tps.HasValue ? ", " + Format(tps.Value) + " tokens/s" : ""));
                if (large!.TimedOut || String.IsNullOrWhiteSpace(large.Output)) { problem = true; notes.Add("32K produced no answer in time"); }
            }
            else if (large?.CouldNotRunReason != null)
            {
                notes.Add("32K could not run: " + large.CouldNotRunReason);
            }

            // Advice: room for the slowest first output twice over plus a long answer (4000 tokens) at the slowest rate.
            double firstSeconds = worstFirst / 1000.0;
            double generateSeconds = slowest.HasValue && slowest.Value > 0 ? 4000.0 / slowest.Value : 600.0;
            int request = ClampInt((int)Math.Ceiling(firstSeconds * 2 + generateSeconds * 1.5), 60, 7200);
            int inactivity = ClampInt((int)Math.Ceiling(Math.Max(firstSeconds * 2, 30)), 30, 3600);
            int stall = ClampInt(Math.Max(inactivity * 2, 120), 120, 7200);
            result.Metrics.SuggestedRequestTimeoutSeconds = request;
            result.Metrics.SuggestedInactivityTimeoutSeconds = inactivity;
            result.Metrics.SuggestedStallTimeoutSeconds = stall;
            result.Outcome = problem ? ModelProbeOutcomeEnum.Failed : ModelProbeOutcomeEnum.Passed;
            result.Summary = String.Join("; ", notes) + ". Advised inference timeouts: request " + request + " s, inactivity "
                + inactivity + " s, stall " + stall + " s.";
            return result;
        }

        /// <summary>
        /// Determinism: agreement of parsed answer values across repeats. Explanation wording is diagnostic only.
        /// </summary>
        /// <param name="outputs">The answers of the repeats that ran.</param>
        /// <param name="couldNotRunReason">Why fewer than two ran, or null.</param>
        /// <returns>The result.</returns>
        public static ModelProbeResult ScoreDeterminism(List<string>? outputs, string? couldNotRunReason = null)
        {
            ModelProbeResult result = New(ModelProbeKindEnum.Determinism);
            if (outputs == null || outputs.Count < 2)
                return CouldNotRun(result, couldNotRunReason ?? "Fewer than two repeats could run.");

            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> answers = new HashSet<string>(StringComparer.Ordinal);
            int invalidAnswers = 0;
            List<HashSet<string>> words = new List<HashSet<string>>();
            foreach (string output in outputs)
            {
                string normalized = _Whitespace.Replace((output ?? String.Empty).Trim().ToLowerInvariant(), " ");
                distinct.Add(normalized);
                words.Add(new HashSet<string>(normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal));
                string? answer = AnswerLine(output);
                if (answer == null) invalidAnswers++;
                else answers.Add(answer);
            }

            double min = 1.0;
            for (int i = 0; i < words.Count; i++)
                for (int j = i + 1; j < words.Count; j++)
                    min = Math.Min(min, Jaccard(words[i], words[j]));

            bool agreement = invalidAnswers == 0 && answers.Count == 1;
            result.Attempts = outputs.Count;
            result.Metrics.DistinctOutputs = distinct.Count;
            result.Metrics.AnswerAgreement = agreement;
            result.Metrics.MinSimilarity = Math.Round(min, 3);
            result.Outcome = agreement ? ModelProbeOutcomeEnum.Passed : ModelProbeOutcomeEnum.Failed;
            if (invalidAnswers > 0)
                result.Reason = invalidAnswers + " of " + outputs.Count + " repeats did not contain a valid ANSWER: followed by exactly five comma-separated integers.";
            result.Summary = (invalidAnswers > 0 ? "Malformed or missing answers" : agreement ? "Same answer" : "Answers differ") + " across " + outputs.Count + " repeats; "
                + distinct.Count + " distinct outputs; lowest similarity " + Format(min) + ".";
            return result;
        }

        #endregion

        #region Private-Methods

        private static ModelProbeResult New(ModelProbeKindEnum probe)
        {
            return new ModelProbeResult { Probe = probe, Outcome = ModelProbeOutcomeEnum.CouldNotRun };
        }

        private static ModelProbeResult CouldNotRun(ModelProbeResult result, string reason)
        {
            result.Outcome = ModelProbeOutcomeEnum.CouldNotRun;
            result.Reason = reason;
            result.Summary = "Could not run: " + reason;
            return result;
        }

        private static void CheckHash(string? reported, string text, string name, ref int compliant, ref int malformed, List<string> wrong)
        {
            if (String.IsNullOrWhiteSpace(reported))
            {
                malformed++;
                wrong.Add(name);
                return;
            }

            if (String.Equals(reported.Trim(), Sha256Hex(text), StringComparison.OrdinalIgnoreCase)) compliant++;
            else wrong.Add(name);
        }

        private static bool HasRepeatedLines(string output, int threshold)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length < 8) continue;
                counts.TryGetValue(line, out int count);
                count++;
                if (count >= threshold) return true;
                counts[line] = count;
            }

            return false;
        }

        private static bool Ran(ModelProbeExchange? exchange)
        {
            return exchange != null && exchange.CouldNotRunReason == null;
        }

        private static void Measure(ModelProbeExchange exchange, out long first, out double? tokensPerSecond)
        {
            first = exchange.FirstOutputMs ?? exchange.DurationMs;
            long tokens = ModelProbeCatalog.EstimateTokens(exchange.Output);
            tokensPerSecond = null;
            if (tokens <= 0) return;

            // A runtime that prints the answer at once leaves no generation window; fall back to the whole duration.
            long window = exchange.DurationMs - first;
            if (window < 50) window = exchange.DurationMs;
            if (window <= 0) return;
            tokensPerSecond = Math.Round(tokens / (window / 1000.0), 2);
        }

        private static string? AnswerLine(string? output)
        {
            if (String.IsNullOrEmpty(output)) return null;
            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim().Trim('`', '*');
                if (line.StartsWith("ANSWER:", StringComparison.OrdinalIgnoreCase))
                {
                    Match match = _DeterminismAnswer.Match(line.Substring(7));
                    if (!match.Success) return null;
                    // Do not accept a fractional fifth value as an integer followed by explanatory punctuation.
                    string tail = line.Substring(7 + match.Length);
                    if (tail.Length > 1 && tail[0] == '.' && Char.IsDigit(tail[1])) return null;
                    string[] values = match.Groups["values"].Value.Split(',');
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (!Int32.TryParse(values[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)) return null;
                        values[i] = value.ToString(CultureInfo.InvariantCulture);
                    }
                    return String.Join(",", values);
                }
            }

            return null;
        }

        private static double Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 && b.Count == 0) return 1.0;
            int shared = 0;
            foreach (string word in a) if (b.Contains(word)) shared++;
            int union = a.Count + b.Count - shared;
            return union == 0 ? 1.0 : (double)shared / union;
        }

        private static int ClampInt(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static string Format(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
