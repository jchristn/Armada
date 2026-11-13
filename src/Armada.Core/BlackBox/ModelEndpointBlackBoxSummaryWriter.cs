namespace Armada.Core.BlackBox
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Writes the model-written black box summary through a configured Armada model endpoint (the same PolyPrompt
    /// clients <see cref="ModelEndpointClientFactory"/> builds for the ApiEndpoint runtime and the endpoint probes). The
    /// prompt carries the record's identity, site, mechanism, reason and the rules' findings and triage, never the full
    /// evidence. Disabled or embedding endpoints, provider errors and empty replies return null. Thread-safe.
    /// </summary>
    public class ModelEndpointBlackBoxSummaryWriter : IBlackBoxSummaryWriter
    {
        #region Public-Members

        /// <summary>
        /// Most tokens the model may answer with. Default 400, minimum 64, maximum 4000.
        /// </summary>
        public int MaxTokens
        {
            get => _MaxTokens;
            set => _MaxTokens = Math.Clamp(value, 64, 4000);
        }

        /// <summary>
        /// Most characters of record facts sent in the prompt. Default 12000, minimum 1000, maximum 64000.
        /// </summary>
        public int MaxPromptChars
        {
            get => _MaxPromptChars;
            set => _MaxPromptChars = Math.Clamp(value, 1000, 64000);
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[ModelEndpointBlackBoxSummaryWriter] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly Func<ModelEndpoint, LoggingModule, CompletionClientBase> _ClientFactory;
        private int _MaxTokens = 400;
        private int _MaxPromptChars = 12000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver (reads the endpoint).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="clientFactory">Client factory, or null for <see cref="ModelEndpointClientFactory.CreateCompletion"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when database or logging is null.</exception>
        public ModelEndpointBlackBoxSummaryWriter(DatabaseDriver database, LoggingModule logging, Func<ModelEndpoint, LoggingModule, CompletionClientBase>? clientFactory = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _ClientFactory = clientFactory ?? ((endpoint, log) => ModelEndpointClientFactory.CreateCompletion(endpoint, log));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string?> WriteSummaryAsync(string endpointId, BlackBoxRecord record, BlackBoxAnalysis analysis, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(endpointId) || record == null || analysis == null) return null;
            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(endpointId.Trim(), token).ConfigureAwait(false);
            if (endpoint == null || !endpoint.Enabled || endpoint.Kind != ModelEndpointKindEnum.Inference)
            {
                _Logging.Debug(_Header + "summary endpoint " + endpointId + " is missing, disabled or not an inference endpoint");
                return null;
            }
            string prompt = BuildPrompt(record, analysis);
            using (CompletionClientBase client = _ClientFactory(endpoint, _Logging))
            {
                CompletionOptions options = new CompletionOptions();
                options.MaxTokens = MaxTokens;
                ChatResponse response = await client.ChatAsync(prompt, options, token).ConfigureAwait(false);
                if (!response.Success || String.IsNullOrWhiteSpace(response.Text))
                {
                    _Logging.Debug(_Header + "summary endpoint " + endpointId + " returned no text: " + (response.Error ?? "empty"));
                    return null;
                }
                return Clean(response.Text);
            }
        }

        /// <summary>
        /// The prompt sent to the model.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="analysis">Rules analysis.</param>
        /// <returns>The prompt text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public string BuildPrompt(BlackBoxRecord record, BlackBoxAnalysis analysis)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));
            StringBuilder facts = new StringBuilder();
            BlackBoxSubject? s = record.Subject;
            facts.Append("Subject: ").Append(s?.SubjectType.ToString() ?? "Mission").Append(' ').Append(s?.MissionId ?? s?.VoyageId ?? "?").Append(" \"").Append(s?.Title ?? "").Append("\" on vessel ").Append(s?.VesselName ?? "?").Append('\n');
            facts.Append("Where: ").Append(BlackBoxRuleFacts.Phase(record)).Append(" / ").Append(BlackBoxRuleFacts.Step(record)).Append(", kind ").Append(BlackBoxRuleFacts.Kind(record)?.ToString() ?? "none").Append('\n');
            if (record.Classification?.RawReason != null) facts.Append("Reason: ").Append(BlackBoxLimits.Clip(record.Classification.RawReason, 2000)).Append('\n');
            if (record.Mechanism?.ExceptionMessage != null) facts.Append("Exception: ").Append(record.Mechanism.ExceptionType).Append(": ").Append(BlackBoxLimits.Clip(record.Mechanism.ExceptionMessage, 1000)).Append('\n');
            if (record.Mechanism?.FailingCommand != null) facts.Append("Failing command: ").Append(record.Mechanism.FailingCommand).Append(" exit ").Append(record.Mechanism.CommandExitCode).Append('\n');
            foreach (BlackBoxFinding f in analysis.Why) facts.Append("Finding ").Append(f.RuleId).Append(" (").Append(f.Confidence).Append("): ").Append(f.Statement).Append('\n');
            if (analysis.Triage != null) facts.Append("Triage: ").Append(analysis.Triage.Verdict).Append(analysis.Triage.TiedWith.Count > 1 ? " (tie: " + String.Join(", ", analysis.Triage.TiedWith) + ")" : String.Empty).Append('\n');
            foreach (BlackBoxRecoveryPath p in analysis.RecoveryPaths.Take(3)) facts.Append("Recovery ").Append(p.Rank).Append(": ").Append(p.Title).Append('\n');
            BlackBoxResilience? res = analysis.Resilience;
            bool inScope = res?.InScope == true;
            if (res != null)
            {
                facts.Append("Armada handoff and landing scope: ").Append(inScope ? "yes (" + (res.ScopeReason ?? "in scope") + ")" : "no").Append('\n');
                facts.Append("Self-healable by Armada: ").Append(res.SelfHealable).Append(res.SelfHealingProposal != null ? "; Armada could have " + res.SelfHealingProposal : String.Empty).Append('\n');
                if (res.DetectionGap != null) facts.Append("Detection gap: ").Append(res.DetectionGap).Append('\n');
                foreach (BlackBoxProposedChange c in res.ProposedChanges.Take(2)) facts.Append("Proposed Armada change ").Append(c.Rank).Append(" (").Append(c.Severity).Append("): ").Append(c.Title).Append('\n');
                if (analysis.Triage?.Suspected == true) facts.Append("Triage note: a suspected Armada defect (only the handoff and landing prior voted for it).\n");
            }
            string body = facts.ToString();
            if (body.Length > MaxPromptChars) body = body.Substring(0, MaxPromptChars);
            return "You summarize Armada black box entries (a record of why an AI coding mission failed). "
                + "Write 3 to 6 plain sentences for the person who owns the mission: what failed, why (quote the findings, do not invent causes), "
                + "the triage verdict, and the first thing to do next. No headings, no lists, no em-dashes. "
                + (inScope
                    ? "This failure is in Armada's own handoff and landing: assume Armada could have been more resilient. Lead with the Armada angle (\"Armada's landing could not ...; Armada could have ...\"), "
                      + "using the self-healing and proposed Armada change facts, before any user-side advice.\n\n"
                    : "If the facts list a proposed Armada change, mention it after the user-side advice.\n\n") + body;
        }

        #endregion

        #region Private-Methods

        private static string? Clean(string text)
        {
            string t = text.Replace('—', '-').Replace('–', '-').Trim();
            if (t.Length == 0) return null;
            return BlackBoxLimits.Clip(t, BlackBoxLimits.MaxSummaryChars);
        }

        #endregion
    }
}
