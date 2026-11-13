namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One step of a rule's recovery path template. A step with a <see cref="Tool"/> is run by run_blackbox_recovery
    /// through that existing MCP tool with the rendered <see cref="Arguments"/>; a step without one is a manual step the
    /// operator or agent does first (its <see cref="Cli"/> text says how). Text fields are rule templates
    /// (<see cref="BlackBoxRuleTemplate"/>). Not thread-safe while being built; read-only afterwards.
    /// </summary>
    public class BlackBoxRuleStep
    {
        #region Public-Members

        /// <summary>What the step does (template).</summary>
        public string Description { get; set; } = "";

        /// <summary>Existing MCP tool the step runs, or null for a manual step.</summary>
        public string? Tool { get; set; } = null;

        /// <summary>Tool arguments (templates).</summary>
        public Dictionary<string, string> Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>CLI command (template); when null and a tool is set, the standard Helm command for the tool is used.</summary>
        public string? Cli { get; set; } = null;

        /// <summary>Whether the step can lose work (uncommitted changes, a branch, a dock's worktree).</summary>
        public bool DataLossRisk { get; set; } = false;

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRuleStep()
        {
        }

        /// <summary>
        /// A step that runs an MCP tool.
        /// </summary>
        /// <param name="description">Description template.</param>
        /// <param name="tool">MCP tool name.</param>
        /// <param name="arguments">Argument templates as name, value pairs.</param>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentNullException">Thrown when description or tool is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when arguments has an odd length.</exception>
        public static BlackBoxRuleStep Run(string description, string tool, params string[] arguments)
        {
            if (String.IsNullOrEmpty(description)) throw new ArgumentNullException(nameof(description));
            if (String.IsNullOrEmpty(tool)) throw new ArgumentNullException(nameof(tool));
            if (arguments.Length % 2 != 0) throw new ArgumentException("Arguments are name, value pairs.", nameof(arguments));
            BlackBoxRuleStep step = new BlackBoxRuleStep { Description = description, Tool = tool };
            for (int i = 0; i < arguments.Length; i += 2) step.Arguments[arguments[i]] = arguments[i + 1];
            return step;
        }

        /// <summary>
        /// A manual step.
        /// </summary>
        /// <param name="description">Description template.</param>
        /// <param name="cli">Command to run by hand (template), or null.</param>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentNullException">Thrown when description is null or empty.</exception>
        public static BlackBoxRuleStep Manual(string description, string? cli = null)
        {
            if (String.IsNullOrEmpty(description)) throw new ArgumentNullException(nameof(description));
            return new BlackBoxRuleStep { Description = description, Cli = cli };
        }

        #endregion
    }
}
