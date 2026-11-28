namespace Test.Shared.Infrastructure.OpenModel
{
    using System;

    /// <summary>
    /// The protocol-neutral content of one successful simulated reply, rendered by <see cref="OpenModelSimulator"/>
    /// as an Ollama or OpenAI-compatible response.
    /// </summary>
    public sealed class ReplyShape
    {
        /// <summary>Assistant text. Never null.</summary>
        public string Content
        {
            get => _Content;
            set => _Content = value ?? String.Empty;
        }

        /// <summary>Reasoning text, or null.</summary>
        public string? Reasoning { get; set; }

        /// <summary>Native tool-call name, or null when the reply has no native tool call.</summary>
        public string? ToolName { get; set; }

        /// <summary>Native tool-call arguments as JSON text.</summary>
        public string ToolArgumentsJson { get; set; } = "{}";

        /// <summary>False when <see cref="ToolArgumentsJson"/> is deliberately malformed.</summary>
        public bool ArgumentsAreValidJson { get; set; } = true;

        /// <summary>OpenAI-style finish reason: <c>stop</c>, <c>tool_calls</c>, or <c>length</c>.</summary>
        public string FinishReason { get; set; } = "stop";

        private string _Content = String.Empty;
    }
}
