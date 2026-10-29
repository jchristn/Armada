namespace Armada.Server
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Models;
    using Armada.Server.Ask;

    /// <summary>
    /// Runs recorded black box actions through the Admiral's registered MCP tool handlers, as the caller
    /// (<see cref="AskActionService.ExecuteAsync"/>): the same argument validation, authorization (the
    /// McpToolAuthorizationRegistry wrapper) and tenant scoping as a direct MCP call by that caller. Only tools in
    /// <see cref="BlackBoxActionCommands.SupportedTools"/> run. Arguments become JSON (true, false and whole numbers as
    /// JSON booleans and numbers, everything else as strings). Thread-safe.
    /// </summary>
    public class BlackBoxMcpActionExecutor : IBlackBoxActionExecutor
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly AskActionService _Actions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="actions">The Admiral's tool executor.</param>
        /// <exception cref="ArgumentNullException">Thrown when actions is null.</exception>
        public BlackBoxMcpActionExecutor(AskActionService actions)
        {
            _Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxRecoveryStepResult> ExecuteAsync(AuthContext caller, BlackBoxAction action, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (action == null) throw new ArgumentNullException(nameof(action));
            token.ThrowIfCancellationRequested();
            BlackBoxRecoveryStepResult result = new BlackBoxRecoveryStepResult { Tool = action.Tool };
            if (!BlackBoxActionCommands.IsSupported(action.Tool))
            {
                result.Message = "Tool " + action.Tool + " is not allowed for black box actions.";
                return result;
            }
            if (!_Actions.HasTool(action.Tool))
            {
                result.Message = "Tool " + action.Tool + " is not registered on this Admiral.";
                return result;
            }
            string arguments = "{" + String.Join(",", action.Arguments.Select(a => JsonSerializer.Serialize(a.Key) + ":" + BlackBoxRuleTemplate.JsonLiteral(a.Value))) + "}";
            AskExecutionOutcome outcome = await _Actions.ExecuteAsync(action.Tool, arguments, caller).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                result.Succeeded = false;
                result.Message = outcome.ErrorText ?? outcome.ErrorCode?.ToString() ?? "The tool failed.";
                return result;
            }
            BlackBoxToolResultProbe? probe = Probe(outcome.ResultText);
            if (probe?.Success == false)
            {
                result.Succeeded = false;
                result.Message = probe.Reason ?? probe.Error ?? "The tool reported that it did not succeed.";
                return result;
            }
            result.Succeeded = true;
            result.Message = Short(outcome.ResultText);
            return result;
        }

        #endregion

        #region Private-Methods

        private static BlackBoxToolResultProbe? Probe(string? text)
        {
            if (String.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("{", StringComparison.Ordinal)) return null;
            try
            {
                return JsonSerializer.Deserialize<BlackBoxToolResultProbe>(text, _JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Short(string? text)
        {
            if (String.IsNullOrEmpty(text)) return null;
            string flat = new StringBuilder(text).Replace('\r', ' ').Replace('\n', ' ').ToString();
            return flat.Length <= 300 ? flat : flat.Substring(0, 300) + "...";
        }

        #endregion
    }
}
