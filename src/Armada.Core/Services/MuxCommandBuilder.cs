namespace Armada.Core.Services
{
    using System.Globalization;
    using Armada.Core.Models;

    /// <summary>
    /// Builds Mux CLI command arguments from Armada captain settings.
    /// </summary>
    public static class MuxCommandBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build arguments for `mux print`.
        /// </summary>
        public static List<string> BuildPrintArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            MuxCaptainOptions? options,
            string? effort = null,
            bool showThinking = false)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (String.IsNullOrWhiteSpace(prompt)) throw new ArgumentNullException(nameof(prompt));

            List<string> args = new List<string>
            {
                "print"
            };

            AppendConfigDirectory(args, options?.ConfigDirectory);
            AppendMcpConfig(args, options?.ConfigDirectory);

            if (!String.IsNullOrWhiteSpace(effort))
            {
                args.Add("--effort");
                args.Add(effort!.Trim());
            }

            if (showThinking)
            {
                // Surface the model's reasoning for this run as assistant_thinking JSONL events.
                args.Add("--show-thinking");
            }

            args.Add("--output-format");
            args.Add("jsonl");

            if (!String.IsNullOrWhiteSpace(finalMessageFilePath))
            {
                args.Add("--output-last-message");
                args.Add(finalMessageFilePath!);
            }

            string? approvalPolicy = options?.ApprovalPolicy;
            if (String.IsNullOrWhiteSpace(approvalPolicy) && options?.AutoApprove == false) approvalPolicy = "deny";
            AppendApprovalArguments(args, approvalPolicy);
            AppendCommonOverrides(args, options, model);

            args.Add("--working-directory");
            args.Add(workingDirectory);
            args.Add(prompt);

            return args;
        }

        /// <summary>
        /// Build arguments for `mux probe`.
        /// </summary>
        public static List<string> BuildProbeArguments(
            string? model,
            MuxCaptainOptions? options,
            bool requireTools = true)
        {
            List<string> args = new List<string>
            {
                "probe",
                "--output-format",
                "json"
            };

            AppendConfigDirectory(args, options?.ConfigDirectory);

            if (requireTools)
            {
                args.Add("--require-tools");
            }

            AppendCommonOverrides(args, options, model);
            return args;
        }

        /// <summary>
        /// Build arguments for `mux endpoint list`.
        /// </summary>
        public static List<string> BuildEndpointListArguments(string? configDirectory)
        {
            List<string> args = new List<string>
            {
                "endpoint",
                "list",
                "--output-format",
                "json"
            };

            AppendConfigDirectory(args, configDirectory);
            return args;
        }

        /// <summary>
        /// Build arguments for `mux endpoint show`.
        /// </summary>
        public static List<string> BuildEndpointShowArguments(string endpointName, string? configDirectory)
        {
            if (String.IsNullOrWhiteSpace(endpointName)) throw new ArgumentNullException(nameof(endpointName));

            List<string> args = new List<string>
            {
                "endpoint",
                "show",
                endpointName.Trim(),
                "--output-format",
                "json"
            };

            AppendConfigDirectory(args, configDirectory);
            return args;
        }

        #endregion

        #region Private-Methods

        private static void AppendConfigDirectory(List<string> args, string? configDirectory)
        {
            if (!String.IsNullOrWhiteSpace(configDirectory))
            {
                args.Add("--config-dir");
                args.Add(configDirectory.Trim());
            }
        }

        /// <summary>
        /// Explicitly pass the Mux config directory's mcp-servers.json via --mcp-config.
        /// Headless `mux print` auto-loads mcp-servers.json normally, but stops doing so once
        /// --working-directory is supplied (which Armada always sets). Passing --mcp-config forces
        /// the configured MCP servers (including Armada) to load, so a captain answering a chat or
        /// running a mission has the same tools it has in an interactive Mux session.
        /// </summary>
        private static void AppendMcpConfig(List<string> args, string? configDirectory)
        {
            string directory = configDirectory?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(directory))
            {
                string? envDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                directory = !String.IsNullOrWhiteSpace(envDir)
                    ? envDir.Trim()
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mux");
            }

            string mcpServersPath = Path.Combine(directory, "mcp-servers.json");
            if (File.Exists(mcpServersPath))
            {
                args.Add("--mcp-config");
                args.Add(mcpServersPath);
            }
        }

        private static void AppendCommonOverrides(List<string> args, MuxCaptainOptions? options, string? model)
        {
            if (!String.IsNullOrWhiteSpace(options?.Endpoint))
            {
                args.Add("--endpoint");
                args.Add(options.Endpoint!);
            }

            if (!String.IsNullOrWhiteSpace(model))
            {
                args.Add("--model");
                args.Add(model!);
            }

            if (!String.IsNullOrWhiteSpace(options?.BaseUrl))
            {
                args.Add("--base-url");
                args.Add(options.BaseUrl!);
            }

            if (!String.IsNullOrWhiteSpace(options?.AdapterType))
            {
                args.Add("--adapter-type");
                args.Add(options.AdapterType!);
            }

            if (options?.Temperature.HasValue == true)
            {
                args.Add("--temperature");
                args.Add(options.Temperature.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (options?.MaxTokens.HasValue == true)
            {
                args.Add("--max-tokens");
                args.Add(options.MaxTokens.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!String.IsNullOrWhiteSpace(options?.SystemPromptPath))
            {
                args.Add("--system-prompt");
                args.Add(options.SystemPromptPath!);
            }
        }

        private static void AppendApprovalArguments(List<string> args, string? approvalPolicy)
        {
            string? normalized = approvalPolicy?.Trim().ToLowerInvariant();
            if (String.IsNullOrEmpty(normalized) || normalized == "auto" || normalized == "autoapprove")
            {
                args.Add("--yolo");
                return;
            }

            args.Add("--approval-policy");
            args.Add(normalized);
        }

        #endregion
    }
}
