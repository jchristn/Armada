namespace Test.Shared.Infrastructure.OpenModel
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One scripted reply served by <see cref="OpenModelSimulator"/>. Build instances with the static factories; each
    /// request the simulator receives on a chat route consumes the next turn in its script.
    /// </summary>
    public sealed class SimulatedTurn
    {
        #region Public-Members

        /// <summary>What the reply looks like.</summary>
        public SimulatedTurnKindEnum Kind { get; set; } = SimulatedTurnKindEnum.Text;

        /// <summary>Assistant text for text replies, or text written around a text tool call. Never null.</summary>
        public string Text
        {
            get => _Text;
            set => _Text = value ?? String.Empty;
        }

        /// <summary>Reasoning text sent in the protocol's reasoning field (Ollama <c>thinking</c>), or null for none.</summary>
        public string? Reasoning { get; set; }

        /// <summary>Tool name for tool-call kinds.</summary>
        public string? ToolName { get; set; }

        /// <summary>Tool arguments as JSON text for tool-call kinds (sent verbatim for <see cref="SimulatedTurnKindEnum.MalformedToolArguments"/>).</summary>
        public string ToolArgumentsJson
        {
            get => _ToolArgumentsJson;
            set => _ToolArgumentsJson = value ?? "{}";
        }

        /// <summary>Status code for <see cref="SimulatedTurnKindEnum.RawResponse"/>. Default 200.</summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>Body for <see cref="SimulatedTurnKindEnum.RawResponse"/>.</summary>
        public string? Body { get; set; }

        /// <summary>Extra response headers, for example <c>Retry-After</c>.</summary>
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Delay between streamed chunks in milliseconds, to imitate a slow local model. Default 0, minimum 0,
        /// maximum 10000. Only applies when the request asks for streaming.
        /// </summary>
        public int ChunkDelayMs
        {
            get => _ChunkDelayMs;
            set
            {
                if (value < 0 || value > 10000) throw new ArgumentOutOfRangeException(nameof(ChunkDelayMs), "ChunkDelayMs must be between 0 and 10000.");
                _ChunkDelayMs = value;
            }
        }

        #endregion

        #region Private-Members

        private string _Text = String.Empty;
        private string _ToolArgumentsJson = "{}";
        private int _ChunkDelayMs = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>A plain text reply.</summary>
        /// <param name="text">Assistant text.</param>
        /// <returns>The turn.</returns>
        public static SimulatedTurn Say(string text) => new SimulatedTurn { Kind = SimulatedTurnKindEnum.Text, Text = text };

        /// <summary>A tool call in the given form.</summary>
        /// <param name="kind">One of the tool-call kinds.</param>
        /// <param name="toolName">Tool name.</param>
        /// <param name="argumentsJson">Arguments as JSON text.</param>
        /// <returns>The turn.</returns>
        /// <exception cref="ArgumentNullException">When <paramref name="toolName"/> is null or empty.</exception>
        public static SimulatedTurn Call(SimulatedTurnKindEnum kind, string toolName, string argumentsJson)
        {
            if (String.IsNullOrEmpty(toolName)) throw new ArgumentNullException(nameof(toolName));
            return new SimulatedTurn { Kind = kind, ToolName = toolName, ToolArgumentsJson = argumentsJson };
        }

        /// <summary>A failure or unusual reply of the given kind with no further settings.</summary>
        /// <param name="kind">Kind of reply.</param>
        /// <returns>The turn.</returns>
        public static SimulatedTurn Of(SimulatedTurnKindEnum kind) => new SimulatedTurn { Kind = kind };

        #endregion
    }
}
