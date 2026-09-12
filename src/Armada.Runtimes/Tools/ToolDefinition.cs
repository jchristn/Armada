namespace Armada.Runtimes.Tools
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Defines a tool available to mux, including its JSON schema for parameters.
    /// </summary>
    public class ToolDefinition
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Description = string.Empty;
        private object _ParametersSchema = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolDefinition"/> class with default values.
        /// </summary>
        public ToolDefinition()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of the tool.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? throw new ArgumentNullException(nameof(Name));
        }

        /// <summary>
        /// A human-readable description of what the tool does.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description
        {
            get => _Description;
            set => _Description = value ?? throw new ArgumentNullException(nameof(Description));
        }

        /// <summary>
        /// The JSON schema object describing the tool's input parameters.
        /// </summary>
        [JsonPropertyName("parametersSchema")]
        public object ParametersSchema
        {
            get => _ParametersSchema;
            set => _ParametersSchema = value ?? throw new ArgumentNullException(nameof(ParametersSchema));
        }

        #endregion
    }
}
