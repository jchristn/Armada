namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Builds the argument list for a headless <c>opencode run</c> invocation. Kept separate from the
    /// runtime adapter so the argument shape can be unit tested without launching a process, mirroring
    /// <see cref="MuxCommandBuilder"/>.
    /// </summary>
    public static class OpenCodeCommandBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build arguments for <c>opencode run</c> in JSONL-event mode.
        /// </summary>
        /// <param name="workingDirectory">Directory to run in; validated here, and set as the process's working directory by the runtime.</param>
        /// <param name="prompt">The message/prompt to send.</param>
        /// <param name="model">Optional <c>provider/model</c> identifier; omitted when null/blank.</param>
        /// <param name="variant">Optional reasoning variant (<c>minimal</c>/<c>high</c>); omitted when null/blank.</param>
        /// <param name="showThinking">When true, add <c>--thinking</c>.</param>
        /// <param name="autoApprove">When true, add <c>--auto</c> to auto-approve non-denied permissions.</param>
        /// <returns>The ordered argument list.</returns>
        public static List<string> BuildRunArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? variant,
            bool showThinking,
            bool autoApprove)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (String.IsNullOrWhiteSpace(prompt)) throw new ArgumentNullException(nameof(prompt));

            List<string> args = new List<string>
            {
                "run",
                "--format",
                "json"
            };

            if (!String.IsNullOrWhiteSpace(model))
            {
                args.Add("--model");
                args.Add(model!.Trim());
            }

            if (!String.IsNullOrWhiteSpace(variant))
            {
                args.Add("--variant");
                args.Add(variant!.Trim());
            }

            if (showThinking)
            {
                args.Add("--thinking");
            }

            if (autoApprove)
            {
                args.Add("--auto");
            }

            // No --dir: OpenCode 2 rejects it ("Unrecognized flag: --dir"), and the process already starts in the
            // working directory, which every OpenCode version uses as the project directory.

            // The prompt is delivered on stdin by the runtime (OpenCodeRuntime.UsePromptStdin), not as a
            // positional argument, because on Windows the opencode executable is an npm ".cmd" wrapper and a
            // multi-line argument passed through cmd.exe is truncated at the first newline. 'opencode run'
            // reads the prompt from stdin when no positional message is supplied. The prompt is still
            // validated above so an empty prompt is rejected consistently across runtimes.
            return args;
        }

        #endregion
    }
}
