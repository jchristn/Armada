namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Shim agent CLIs for interactive-launch tests. Each shim records where and how it ran (working directory,
    /// arguments, the ARMADA_MCP_TOKEN it was given, the prompt it read on stdin) into a record directory and produces
    /// a scripted reply, so a test can tell which host ran it and what reached it. Shell scripts on Unix, .cmd files on
    /// Windows.
    /// </summary>
    public static class ChatShimCli
    {
        #region Public-Methods

        /// <summary>
        /// A Claude Code stand-in that answers in streaming-JSON: one text delta per reply part, then a result event
        /// whose text is the whole reply, with a usage block (input 40, cache read 300, cache creation 10, output 7) and
        /// total_cost_usd 0.0042. It also writes a line to stderr.
        /// </summary>
        /// <param name="directory">Directory to write the shim into.</param>
        /// <param name="recordDirectory">Directory the shim records into.</param>
        /// <param name="replyParts">Reply text deltas (plain ASCII words, no quotes).</param>
        /// <returns>Path of the shim executable.</returns>
        public static string WriteClaudeStreamShim(string directory, string recordDirectory, List<string> replyParts)
        {
            List<string> events = new List<string>();
            foreach (string part in replyParts)
                events.Add("{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"" + part + "\"}}}");
            events.Add("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":5,\"total_cost_usd\":0.0042,\"usage\":{\"input_tokens\":40,\"cache_read_input_tokens\":300,\"cache_creation_input_tokens\":10,\"output_tokens\":7},\"result\":\"" + String.Join(String.Empty, replyParts) + "\"}");
            return WriteShim(directory, "claude", recordDirectory, events, "claude stderr banner", null, false);
        }

        /// <summary>
        /// A Codex stand-in that writes its final answer to the --output-last-message file and prints unrelated
        /// progress noise on stdout and stderr.
        /// </summary>
        /// <param name="directory">Directory to write the shim into.</param>
        /// <param name="recordDirectory">Directory the shim records into.</param>
        /// <param name="finalMessage">Final answer (one ASCII word).</param>
        /// <returns>Path of the shim executable.</returns>
        public static string WriteCodexFinalMessageShim(string directory, string recordDirectory, string finalMessage)
        {
            return WriteShim(directory, "codex", recordDirectory, new List<string> { "codex progress noise" }, "codex stderr banner", finalMessage, false);
        }

        /// <summary>
        /// A Codex stand-in for 'codex exec --json': reasoning, one command execution (started, then completed), the
        /// agent message, and turn.completed with usage (input 1200, cached input 1000, output 33) on stdout, and the
        /// final answer in the --output-last-message file.
        /// </summary>
        /// <param name="directory">Directory to write the shim into.</param>
        /// <param name="recordDirectory">Directory the shim records into.</param>
        /// <param name="finalMessage">Final answer (one ASCII word).</param>
        /// <returns>Path of the shim executable.</returns>
        public static string WriteCodexJsonShim(string directory, string recordDirectory, string finalMessage)
        {
            List<string> events = new List<string>
            {
                "{\"type\":\"thread.started\",\"thread_id\":\"th_1\"}",
                "{\"type\":\"turn.started\"}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"reasoning\",\"text\":\"Checking\"}}",
                "{\"type\":\"item.started\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"ls\",\"status\":\"in_progress\"}}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"ls\",\"exit_code\":0,\"status\":\"completed\"}}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_2\",\"type\":\"agent_message\",\"text\":\"" + finalMessage + "\"}}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1200,\"cached_input_tokens\":1000,\"output_tokens\":33}}"
            };
            return WriteShim(directory, "codex", recordDirectory, events, "codex stderr banner", finalMessage, false);
        }

        /// <summary>
        /// A stand-in that records its launch and then keeps running (about two minutes) until it is killed.
        /// </summary>
        /// <param name="directory">Directory to write the shim into.</param>
        /// <param name="name">Executable name (for example "claude").</param>
        /// <param name="recordDirectory">Directory the shim records into.</param>
        /// <returns>Path of the shim executable.</returns>
        public static string WriteSleepingShim(string directory, string name, string recordDirectory)
        {
            return WriteShim(directory, name, recordDirectory, new List<string>(), null, null, true);
        }

        /// <summary>
        /// Head of the provider message in <see cref="OpenCodeTextThenErrorLines"/>.
        /// </summary>
        public const string OpenCodeProviderHead = "Provider rejected the request ";

        /// <summary>
        /// Characters after the 240-character provider message. A bounded failure must omit them.
        /// </summary>
        public const string OpenCodeErrorTail = "TAILMARKER";

        /// <summary>
        /// Top-level error message. The visible failure uses the nested data message instead.
        /// </summary>
        public const string OpenCodeTopLevelMessage = "TOPLEVELMARKER";

        /// <summary>
        /// Distinctive response-body field. It must not appear in operator text.
        /// </summary>
        public const string OpenCodeBodyMarker = "BODYMARKER";

        /// <summary>
        /// Distinctive response-header field. It must not appear in operator text.
        /// </summary>
        public const string OpenCodeHeaderMarker = "HEADERMARKER";

        /// <summary>
        /// Distinctive URL field. It must not appear in operator text.
        /// </summary>
        public const string OpenCodeUrlMarker = "URLMARKER";

        /// <summary>
        /// Distinctive session id. It must not appear in operator text.
        /// </summary>
        public const string OpenCodeSessionMarker = "SESSIONMARKER";

        /// <summary>
        /// Assistant text written before the error event.
        /// </summary>
        public const string OpenCodePartialText = "partial answer";

        /// <summary>
        /// Assistant text of a turn that has no error event.
        /// </summary>
        public const string OpenCodeCompleteText = "complete answer";

        /// <summary>
        /// HTTP status nested under data. This is the status the operator sees.
        /// </summary>
        public const int OpenCodeStatus = 400;

        /// <summary>
        /// Top-level status. The nested data status wins, so this value stays out of operator text.
        /// </summary>
        public const int OpenCodeIgnoredStatus = 500;

        /// <summary>
        /// A text event followed by a type=error event. The error carries a nested message, a status, and
        /// distinctive body, header, URL, and session fields.
        /// </summary>
        /// <returns>Stdout lines for an OpenCode JSON stream.</returns>
        public static List<string> OpenCodeTextThenErrorLines()
        {
            string message = OpenCodeProviderMessage();
            string text = "{\"type\":\"text\",\"timestamp\":1,\"sessionID\":\"" + OpenCodeSessionMarker + "\",\"part\":{\"type\":\"text\",\"text\":\"" + OpenCodePartialText + "\"}}";
            string error = "{\"type\":\"error\",\"timestamp\":2,\"sessionID\":\"" + OpenCodeSessionMarker + "\",\"error\":{\"message\":\"" + OpenCodeTopLevelMessage + "\",\"statusCode\":" + OpenCodeIgnoredStatus.ToString() + ",\"data\":{\"message\":\"" + message + "\",\"statusCode\":" + OpenCodeStatus.ToString() + ",\"responseBody\":\"" + OpenCodeBodyMarker + "\",\"responseHeaders\":{\"authorization\":\"" + OpenCodeHeaderMarker + "\"},\"url\":\"" + OpenCodeUrlMarker + "\"}}}";
            return new List<string> { text, error };
        }

        /// <summary>
        /// One text event and no error event.
        /// </summary>
        /// <returns>Stdout lines for an OpenCode JSON stream.</returns>
        public static List<string> OpenCodeTextOnlyLines()
        {
            string text = "{\"type\":\"text\",\"timestamp\":3,\"sessionID\":\"ses_ok\",\"part\":{\"type\":\"text\",\"text\":\"" + OpenCodeCompleteText + "\"}}";
            return new List<string> { text };
        }

        /// <summary>
        /// One plain JSON summary line. It is not an OpenCode protocol event.
        /// </summary>
        /// <returns>Stdout lines for a planning summary.</returns>
        public static List<string> OpenCodePlanningSummaryLines()
        {
            return new List<string> { "{\"title\":\"Ship the fix\",\"description\":\"Do the work\"}" };
        }

        /// <summary>
        /// One plain JSON summary line. It is not an OpenCode protocol event.
        /// </summary>
        /// <returns>Stdout lines for a refinement summary.</returns>
        public static List<string> OpenCodeRefinementSummaryLines()
        {
            return new List<string> { "{\"summary\":\"Keep the contract\",\"acceptanceCriteria\":[\"The turn fails\"]}" };
        }

        /// <summary>
        /// Write an OpenCode stand-in that drains stdin, prints the lines, and exits 0.
        /// </summary>
        /// <param name="directory">Directory to write the shim into.</param>
        /// <param name="stdoutLines">Lines to print. None may contain a single quote.</param>
        /// <returns>Path of the shim executable.</returns>
        public static string WriteOpenCodeShim(string directory, List<string> stdoutLines)
        {
            if (stdoutLines == null) throw new ArgumentNullException(nameof(stdoutLines));
            foreach (string line in stdoutLines)
            {
                if (line != null && line.Contains('\'')) throw new ArgumentException("An OpenCode shim line cannot contain a single quote.");
            }

            string record = Path.Combine(directory, "shim-record");
            return WriteShim(directory, "opencode", record, stdoutLines, null, null, false);
        }

        /// <summary>
        /// Point OpenCode host-config lookups at an empty directory for the life of the returned scope,
        /// then restore the previous values.
        /// </summary>
        /// <returns>A scope that restores the process environment.</returns>
        public static IDisposable IsolateOpenCodeHostConfig()
        {
            return new OpenCodeHostConfigScope();
        }

        /// <summary>
        /// The visible failure has the nested provider message and status, and none of the secret-bearing fields.
        /// </summary>
        /// <param name="text">Operator-visible text.</param>
        /// <param name="label">Assertion label.</param>
        public static void AssertBoundedProviderFailure(string? text, string label)
        {
            string value = text ?? String.Empty;
            Asserts.AssertContains(OpenCodeProviderHead, value, label);
            Asserts.AssertContains("(status " + OpenCodeStatus.ToString() + ")", value, label);
            Asserts.AssertFalse(value.Contains(OpenCodeErrorTail), label + " includes the message tail");
            Asserts.AssertFalse(value.Contains(OpenCodeTopLevelMessage), label + " includes the top-level message");
            Asserts.AssertFalse(value.Contains(OpenCodeBodyMarker), label + " includes the response body");
            Asserts.AssertFalse(value.Contains(OpenCodeHeaderMarker), label + " includes a response header");
            Asserts.AssertFalse(value.Contains(OpenCodeUrlMarker), label + " includes the URL");
            Asserts.AssertFalse(value.Contains(OpenCodeSessionMarker), label + " includes the session id");
            Asserts.AssertFalse(value.Contains(OpenCodePartialText), label + " keeps earlier assistant text");
            Asserts.AssertFalse(value.Contains(OpenCodeIgnoredStatus.ToString()), label + " includes the unused status");
        }

        /// <summary>
        /// Read a file the shim recorded, or an empty string when it does not exist yet.
        /// </summary>
        /// <param name="recordDirectory">Record directory.</param>
        /// <param name="fileName">File name (cwd.txt, args.txt, token.txt, prompt.txt).</param>
        /// <returns>The file content.</returns>
        public static string ReadRecord(string recordDirectory, string fileName)
        {
            string path = Path.Combine(recordDirectory, fileName);
            try { return File.Exists(path) ? File.ReadAllText(path) : String.Empty; }
            catch (IOException) { return String.Empty; }
        }

        #endregion

        #region Private-Methods

        private static string OpenCodeProviderMessage()
        {
            int pad = 240 - OpenCodeProviderHead.Length;
            if (pad < 1) pad = 1;
            return OpenCodeProviderHead + new string('m', pad) + OpenCodeErrorTail;
        }

        private sealed class OpenCodeHostConfigScope : IDisposable
        {
            private readonly string? _Xdg;
            private readonly string? _Config;
            private readonly string? _Content;
            private readonly string _Temp;
            private bool _Disposed;

            public OpenCodeHostConfigScope()
            {
                _Xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                _Config = Environment.GetEnvironmentVariable("OPENCODE_CONFIG");
                _Content = Environment.GetEnvironmentVariable("OPENCODE_CONFIG_CONTENT");
                _Temp = Path.Combine(Path.GetTempPath(), "armada-oc-config-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_Temp);
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _Temp);
                Environment.SetEnvironmentVariable("OPENCODE_CONFIG", null);
                Environment.SetEnvironmentVariable("OPENCODE_CONFIG_CONTENT", null);
            }

            public void Dispose()
            {
                if (_Disposed) return;
                _Disposed = true;
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _Xdg);
                Environment.SetEnvironmentVariable("OPENCODE_CONFIG", _Config);
                Environment.SetEnvironmentVariable("OPENCODE_CONFIG_CONTENT", _Content);
                try
                {
                    if (Directory.Exists(_Temp)) Directory.Delete(_Temp, true);
                }
                catch
                {
                }
            }
        }

        private static string WriteShim(string directory, string name, string recordDirectory, List<string> stdoutLines, string? stderrLine, string? finalMessage, bool sleep)
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(recordDirectory);
            StringBuilder script = new StringBuilder();

            if (OperatingSystem.IsWindows())
            {
                string path = Path.Combine(directory, name + ".cmd");
                script.Append("@echo off\r\n");
                script.Append("setlocal EnableExtensions EnableDelayedExpansion\r\n");
                script.Append("set \"REC=" + recordDirectory + "\"\r\n");
                script.Append("cd > \"!REC!\\cwd.txt\"\r\n");
                script.Append(">\"!REC!\\token.txt\" echo(!ARMADA_MCP_TOKEN!\r\n");
                script.Append("type nul > \"!REC!\\args.txt\"\r\n");
                script.Append("set \"PREV=\"\r\n");
                script.Append(":loop\r\n");
                script.Append("if \"%~1\"==\"\" goto done\r\n");
                script.Append(">> \"!REC!\\args.txt\" echo(%~1\r\n");
                if (finalMessage != null)
                    script.Append("if \"!PREV!\"==\"--output-last-message\" (>\"%~1\" echo " + finalMessage + ")\r\n");
                script.Append("set \"PREV=%~1\"\r\n");
                script.Append("shift\r\n");
                script.Append("goto loop\r\n");
                script.Append(":done\r\n");
                script.Append("findstr \"^\" > \"!REC!\\prompt.txt\"\r\n");
                if (stderrLine != null) script.Append("echo " + stderrLine + " 1>&2\r\n");
                foreach (string line in stdoutLines) script.Append("echo " + line + "\r\n");
                if (sleep) script.Append("ping -n 120 127.0.0.1 >nul\r\n");
                script.Append("exit /b 0\r\n");
                File.WriteAllText(path, script.ToString());
                return path;
            }

            string shimPath = Path.Combine(directory, name);
            script.Append("#!/usr/bin/env sh\n");
            script.Append("rec='" + recordDirectory + "'\n");
            script.Append("pwd > \"$rec/cwd.txt\"\n");
            script.Append("printf '%s\\n' \"$ARMADA_MCP_TOKEN\" > \"$rec/token.txt\"\n");
            script.Append(": > \"$rec/args.txt\"\n");
            script.Append("prev=\"\"\n");
            script.Append("for a in \"$@\"; do\n");
            script.Append("  printf '%s\\n' \"$a\" >> \"$rec/args.txt\"\n");
            if (finalMessage != null)
                script.Append("  if [ \"$prev\" = \"--output-last-message\" ]; then printf '%s' '" + finalMessage + "' > \"$a\"; fi\n");
            script.Append("  prev=\"$a\"\n");
            script.Append("done\n");
            script.Append("cat > \"$rec/prompt.txt\"\n");
            if (stderrLine != null) script.Append("echo '" + stderrLine + "' 1>&2\n");
            foreach (string line in stdoutLines) script.Append("printf '%s\\n' '" + line + "'\n");
            if (sleep) script.Append("exec sleep 120\n");
            script.Append("exit 0\n");
            File.WriteAllText(shimPath, script.ToString());
            File.SetUnixFileMode(shimPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return shimPath;
        }

        #endregion
    }
}
