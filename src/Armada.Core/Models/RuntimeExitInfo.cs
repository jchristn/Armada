namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Typed outcome of a captain runtime process, decided once when the process exits from its exit code and any
    /// structured provider error the runtime reported while it ran. Quarantine, stall, and crash-loop policy read
    /// <see cref="FailureKind"/>; nothing downstream re-derives it from output text.
    /// </summary>
    public class RuntimeExitInfo
    {
        #region Public-Members

        /// <summary>
        /// Process exit code (null when unavailable).
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Classified failure kind.
        /// </summary>
        public RuntimeFailureKindEnum FailureKind { get; set; } = RuntimeFailureKindEnum.Unclassified;

        /// <summary>
        /// The last structured provider error the runtime reported before exiting, if any.
        /// </summary>
        public RuntimeProviderError? ProviderError { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Human-readable summary suitable for a mission failure reason: the provider error's
        /// <see cref="RuntimeProviderError.Reason"/> followed by the exit code when the runtime gave one, otherwise the
        /// exit code and the provider error. Display text only; never parsed.
        /// </summary>
        /// <returns>Summary text.</returns>
        public string Describe()
        {
            string exit = ExitCode.HasValue ? ExitCode.Value.ToString() : "unknown";
            if (ProviderError != null && !String.IsNullOrWhiteSpace(ProviderError.Reason))
            {
                string reason = ProviderError.Reason!.Trim();
                if (!reason.EndsWith(".", StringComparison.Ordinal)) reason += ".";
                return reason + " Agent process exited with code " + exit + "." + Remediation();
            }

            string text = "Agent process exited with code " + (ExitCode.HasValue ? ExitCode.Value.ToString() : "unknown");
            if (ProviderError != null) text += " (" + ProviderError.ToString() + ")";
            return text + Remediation();
        }

        private string Remediation()
        {
            string? guidance = ProviderError?.ErrorType?.ToLowerInvariant() switch
            {
                "max_iterations_reached" => "Increase the endpoint or captain model-turn limit, or narrow the mission and reduce repeated investigation. A model turn can contain several tool calls.",
                "budget_exceeded" => "Review the runtime token or cost budget. Increase the appropriate budget or narrow the task before another run.",
                "degenerate_output" => "Inspect repeated tool calls and outputs; reduce repetitive context or select a model that can reliably complete this task.",
                "tool_call_limit_reached" => "Increase the tool-call limit or reduce the mission scope. Tool calls and model turns are separate budgets.",
                "output_token_limit_reached" => "Increase Max Output Tokens in the inference endpoint, including room for reasoning and the visible answer. Reduce reasoning effort where the model supports it.",
                "incomplete_completion" => "Inspect the final inference turn and mission log. Check output-token limits and model tool support; the captain must finish the work and return the required completion marker and summary.",
                "context_limit_exceeded" => "Reduce tool output and mission context, or select a model with a larger context window.",
                "model_out_of_memory" => "The model server could not fit the model in memory. Unload other models, lower the context length, choose a smaller quantization, or route this persona to a different endpoint.",
                "tool_calling_unsupported" => "The model or server cannot do tool calling as configured. Choose a model with tool support, or enable the server's tool-call parser (vLLM: --enable-auto-tool-choice and --tool-call-parser).",
                "tool_call_parse_error" => "The server could not parse the model's tool call. Check the server's tool-call parser for this model, lower the temperature, or select a model that emits tool calls reliably.",
                _ => FailureKind switch
                {
                    RuntimeFailureKindEnum.AuthFailure => "Check the endpoint credentials and provider permissions, then re-enable the captain.",
                    RuntimeFailureKindEnum.UsageLimit => "Check provider quota, billing and rate limits; wait for the limit to reset or select an available endpoint.",
                    RuntimeFailureKindEnum.ModelUnavailable => "Check the endpoint URL, model identifier and account access to that model.",
                    RuntimeFailureKindEnum.Transient => "Check provider availability, connectivity and request timeout settings.",
                    RuntimeFailureKindEnum.Unclassified when ProviderError == null => "The runtime reported no provider error. Inspect the mission log for the last model turn and tool call; if the cause is a provider message Armada does not recognize, report it so it can be classified.",
                    _ => ProviderError?.HttpStatusCode switch
                    {
                        400 or 405 or 415 or 422 => "The provider rejected the request. Check the endpoint URL, provider, model, inference options and tool support. If these are correct, inspect the provider error and SDK compatibility; a rejected request can also be an integration defect. Validate the endpoint after correcting the cause before restarting the mission.",
                        _ => null
                    }
                }
            };
            return guidance == null ? String.Empty : "\nRemediation: " + guidance;
        }

        #endregion
    }
}
