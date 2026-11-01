namespace Armada.Runtimes.Usage
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// The production read-only runtime registry on Admiral and Harbor credential hosts. Claude Code reads
    /// OAuth usage, Codex reads app-server limits, and Gemini reads Code Assist quota. Claude and Gemini may
    /// use the bounded interactive fallback when permitted. ApiEndpoint harvests only existing responses;
    /// other runtimes without a verified source remain Unsupported. No built-in probe sends a model prompt.
    /// </summary>
    public class UsageProbeRegistry
    {
        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly UsageAccountResolver _Accounts;
        private readonly string? _CodexExecutable;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="accounts">Host-local account resolver.</param>
        /// <param name="codexExecutable">Optional Codex executable override; defaults to <c>codex</c> on PATH.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logging"/> or <paramref name="accounts"/> is null.</exception>
        public UsageProbeRegistry(LoggingModule logging, UsageAccountResolver accounts, string? codexExecutable = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _CodexExecutable = codexExecutable;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The probe for a runtime. Never null: a runtime without a verified source gets an Unsupported probe.
        /// </summary>
        /// <param name="runtime">Captain runtime.</param>
        /// <returns>The probe.</returns>
        public IUsageProbe Resolve(AgentRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode:
                    return new ClaudeUsageProbe(_Accounts);
                case AgentRuntimeEnum.Codex:
                    return new CodexUsageProbe(_Accounts, _CodexExecutable);
                case AgentRuntimeEnum.Gemini:
                    return new GeminiUsageProbe(_Accounts);
                case AgentRuntimeEnum.OpenCode:
                    return new OpenCodeUsageProbe();
                case AgentRuntimeEnum.ApiEndpoint:
                    return new UnsupportedUsageProbe("PassiveOnly", "ApiEndpoint limits are harvested passively from rate-limit headers on existing inference calls; no active request is ever scheduled");
                case AgentRuntimeEnum.Custom:
                    return new UnsupportedUsageProbe("CustomCommandRequired", "the Custom runtime captures account usage only through a configured Custom usage command");
                default:
                    UsageCaptureCapability capability = AgentRuntimeCapabilities.UsageCapture(runtime);
                    return new UnsupportedUsageProbe("Unsupported" + runtime, capability.Reason ?? ("the " + runtime + " runtime has no verified account usage source"));
            }
        }

        #endregion
    }
}
