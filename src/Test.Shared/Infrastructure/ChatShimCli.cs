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
        /// whose text is the whole reply. It also writes a line to stderr.
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
            events.Add("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":5,\"result\":\"" + String.Join(String.Empty, replyParts) + "\"}");
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
