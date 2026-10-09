namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Shim agent CLIs that print structured output (Claude Code stream-json, Codex exec --json) in phases. Between two
    /// phases the shim waits until the test creates the next gate file (<see cref="Gate"/>) in its record directory, so a
    /// test can look at what a running captain reported so far without timing anything. The shim records its arguments
    /// and prompt, can commit a file in its working directory (as a captain does its work) before the last phase, and can
    /// write a final message to the --output-last-message file. Shell scripts on Unix, .cmd files on Windows. Lines must
    /// not contain single quotes, percent signs, exclamation marks, or the characters &amp; | &lt; &gt; ^.
    /// </summary>
    public static class StreamingShimCli
    {
        #region Public-Methods

        /// <summary>
        /// Write a shim.
        /// </summary>
        /// <param name="directory">Directory to write the shim into.</param>
        /// <param name="name">Executable name (for example "claude").</param>
        /// <param name="recordDirectory">Directory the shim records into and reads its gates from.</param>
        /// <param name="phases">Stdout lines per phase; the shim waits for gate N before phase N (from 1).</param>
        /// <param name="commitFile">File to create and commit in the working directory before the last phase, or null.</param>
        /// <param name="finalMessage">Text written to the --output-last-message file at the end (one ASCII line), or null.</param>
        /// <returns>Path of the shim executable.</returns>
        public static string Write(string directory, string name, string recordDirectory, List<List<string>> phases, string? commitFile, string? finalMessage)
        {
            if (phases == null || phases.Count == 0) throw new ArgumentException("At least one phase is required.", nameof(phases));
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(recordDirectory);
            return OperatingSystem.IsWindows()
                ? WriteCmd(directory, name, recordDirectory, phases, commitFile, finalMessage)
                : WriteSh(directory, name, recordDirectory, phases, commitFile, finalMessage);
        }

        /// <summary>
        /// Open gate <paramref name="number"/>: the shim moves on to phase <paramref name="number"/>.
        /// </summary>
        /// <param name="recordDirectory">The shim's record directory.</param>
        /// <param name="number">Gate number, from 1.</param>
        public static void Gate(string recordDirectory, int number)
        {
            File.WriteAllText(Path.Combine(recordDirectory, "gate" + number), "open");
        }

        #endregion

        #region Private-Methods

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static string WriteSh(string directory, string name, string recordDirectory, List<List<string>> phases, string? commitFile, string? finalMessage)
        {
            StringBuilder script = new StringBuilder();
            script.Append("#!/usr/bin/env sh\n");
            script.Append("rec='" + recordDirectory + "'\n");
            script.Append(": > \"$rec/args.txt\"\n");
            script.Append("prev=\"\"\n");
            script.Append("out=\"\"\n");
            script.Append("for a in \"$@\"; do\n");
            script.Append("  printf '%s\\n' \"$a\" >> \"$rec/args.txt\"\n");
            script.Append("  if [ \"$prev\" = \"--output-last-message\" ]; then out=\"$a\"; fi\n");
            script.Append("  prev=\"$a\"\n");
            script.Append("done\n");
            script.Append("cat > \"$rec/prompt.txt\"\n");
            for (int i = 0; i < phases.Count; i++)
            {
                if (i > 0) script.Append("while [ ! -f \"$rec/gate" + i + "\" ]; do sleep 0.05; done\n");
                if (i == phases.Count - 1 && commitFile != null)
                {
                    script.Append("printf 'captain work\\n' > '" + commitFile + "'\n");
                    script.Append("git add '" + commitFile + "' >/dev/null 2>&1\n");
                    script.Append("git -c user.name=Captain -c user.email=captain@example.com commit -q -m 'Captain work' >/dev/null 2>&1\n");
                }

                foreach (string line in phases[i]) script.Append("printf '%s\\n' '" + line + "'\n");
            }

            if (finalMessage != null) script.Append("if [ -n \"$out\" ]; then printf '%s' '" + finalMessage + "' > \"$out\"; fi\n");
            script.Append("exit 0\n");

            string path = Path.Combine(directory, name);
            File.WriteAllText(path, script.ToString());
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return path;
        }

        private static string WriteCmd(string directory, string name, string recordDirectory, List<List<string>> phases, string? commitFile, string? finalMessage)
        {
            StringBuilder script = new StringBuilder();
            script.Append("@echo off\r\n");
            script.Append("setlocal EnableExtensions\r\n");
            script.Append("set \"REC=" + recordDirectory + "\"\r\n");
            script.Append("type nul > \"%REC%\\args.txt\"\r\n");
            script.Append("set \"PREV=\"\r\n");
            script.Append("set \"OUT=\"\r\n");
            script.Append(":args\r\n");
            script.Append("if \"%~1\"==\"\" goto argsdone\r\n");
            script.Append(">> \"%REC%\\args.txt\" echo(%~1\r\n");
            script.Append("if \"%PREV%\"==\"--output-last-message\" set \"OUT=%~1\"\r\n");
            script.Append("set \"PREV=%~1\"\r\n");
            script.Append("shift\r\n");
            script.Append("goto args\r\n");
            script.Append(":argsdone\r\n");
            script.Append("findstr \"^\" > \"%REC%\\prompt.txt\"\r\n");
            for (int i = 0; i < phases.Count; i++)
            {
                if (i > 0)
                {
                    script.Append(":gate" + i + "\r\n");
                    script.Append("if not exist \"%REC%\\gate" + i + "\" (ping -n 1 -w 50 127.0.0.1 >nul & goto gate" + i + ")\r\n");
                }

                if (i == phases.Count - 1 && commitFile != null)
                {
                    script.Append(">\"" + commitFile + "\" echo captain work\r\n");
                    script.Append("git add \"" + commitFile + "\" >nul 2>&1\r\n");
                    script.Append("git -c user.name=Captain -c user.email=captain@example.com commit -q -m \"Captain work\" >nul 2>&1\r\n");
                }

                foreach (string line in phases[i]) script.Append("echo " + line + "\r\n");
            }

            if (finalMessage != null) script.Append("if not \"%OUT%\"==\"\" (>\"%OUT%\" echo " + finalMessage + ")\r\n");
            script.Append("exit /b 0\r\n");

            string path = Path.Combine(directory, name + ".cmd");
            File.WriteAllText(path, script.ToString());
            return path;
        }

        #endregion
    }
}
