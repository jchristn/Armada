namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// The "Ask Armada" assistant: a lightweight, deterministic natural-language front end for
    /// interrogating fleet state. It maps recognized intents (status, captains, missions, failures,
    /// stalls, docks, voyages) onto live queries and returns a plain-text reply plus suggested
    /// navigation links. It performs read-only interrogation only; it deliberately does not take
    /// mutating actions. The intent layer is a clean seam for a future LLM/agent-backed upgrade.
    /// </summary>
    public class AskArmadaService
    {
        #region Private-Members

        private readonly string _Header = "[AskArmadaService] ";
        private readonly DatabaseDriver _Database;
        private readonly IAdmiralService _Admiral;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver. Required.</param>
        /// <param name="admiral">Admiral service for status aggregation. Required.</param>
        /// <param name="logging">Logging module. Required.</param>
        public AskArmadaService(DatabaseDriver database, IAdmiralService admiral, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Answer a natural-language question about fleet state.
        /// </summary>
        /// <param name="message">The user's message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The assistant response.</returns>
        public async Task<AskResponse> AskAsync(string message, AuthContext auth, CancellationToken token = default)
        {
            string text = (message ?? String.Empty).Trim().ToLowerInvariant();
            if (text.Length == 0)
                return BuildHelp();

            if (Contains(text, "help", "what can you", "capabilities", "commands"))
                return BuildHelp();

            try
            {
                if (Contains(text, "stall", "stuck", "hung", "frozen"))
                    return await AnswerStalledAsync(token).ConfigureAwait(false);

                if (Contains(text, "fail", "broke", "error"))
                    return await AnswerFailuresAsync(auth, token).ConfigureAwait(false);

                if (Contains(text, "captain", "agent", "worker"))
                    return await AnswerCaptainsAsync(token).ConfigureAwait(false);

                if (Contains(text, "dock", "worktree"))
                    return BuildDocksAnswer();

                if (Contains(text, "voyage"))
                    return await AnswerVoyagesAsync(token).ConfigureAwait(false);

                if (Contains(text, "mission", "task", "work"))
                    return await AnswerMissionsAsync(auth, token).ConfigureAwait(false);

                if (Contains(text, "status", "overview", "how are", "how's", "health", "summary", "going"))
                    return await AnswerStatusAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error answering '" + text + "': " + ex.Message);
                return new AskResponse
                {
                    Kind = AskResponseKindEnum.Unknown,
                    Reply = "I hit an error looking that up. Try again, or check the dashboard directly."
                };
            }

            AskResponse unknown = BuildHelp();
            unknown.Kind = AskResponseKindEnum.Unknown;
            unknown.Reply = "I did not understand that. " + unknown.Reply;
            return unknown;
        }

        #endregion

        #region Private-Methods

        private async Task<AskResponse> AnswerStatusAsync(CancellationToken token)
        {
            ArmadaStatus status = await _Admiral.GetStatusAsync(token).ConfigureAwait(false);
            string reply =
                "Fleet status: " + status.TotalCaptains + " captains (" +
                status.IdleCaptains + " idle, " + status.WorkingCaptains + " working, " +
                status.StalledCaptains + " stalled), " + status.ActiveVoyages + " active voyage(s).";
            if (status.StalledCaptains > 0)
                reply += " Some captains are stalled -- you may want to look into that.";
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Answer,
                Reply = reply,
                Links = new List<AskLink> { new AskLink("Captains", "/captains"), new AskLink("Voyages", "/voyages") }
            };
        }

        private async Task<AskResponse> AnswerCaptainsAsync(CancellationToken token)
        {
            ArmadaStatus status = await _Admiral.GetStatusAsync(token).ConfigureAwait(false);
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Answer,
                Reply = status.TotalCaptains + " captains: " + status.IdleCaptains + " idle, " +
                    status.WorkingCaptains + " working, " + status.StalledCaptains + " stalled.",
                Links = new List<AskLink> { new AskLink("Captains", "/captains") }
            };
        }

        private async Task<AskResponse> AnswerStalledAsync(CancellationToken token)
        {
            ArmadaStatus status = await _Admiral.GetStatusAsync(token).ConfigureAwait(false);
            if (status.StalledCaptains == 0)
                return new AskResponse
                {
                    Kind = AskResponseKindEnum.Answer,
                    Reply = "No captains are currently stalled. If a dock seems stuck, you can repair or unstick it from the Docks page.",
                    Links = new List<AskLink> { new AskLink("Docks", "/docks") }
                };
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Answer,
                Reply = status.StalledCaptains + " captain(s) are stalled. The health loop auto-recovers within the configured attempts; you can also reclaim their docks from the Docks page.",
                Links = new List<AskLink> { new AskLink("Captains", "/captains"), new AskLink("Docks", "/docks") }
            };
        }

        private async Task<AskResponse> AnswerFailuresAsync(AuthContext auth, CancellationToken token)
        {
            List<Mission> failed = await MissionsByStatusAsync(auth, MissionStatusEnum.Failed, token).ConfigureAwait(false);
            List<Mission> landingFailed = await MissionsByStatusAsync(auth, MissionStatusEnum.LandingFailed, token).ConfigureAwait(false);
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Answer,
                Reply = failed.Count + " failed mission(s) and " + landingFailed.Count + " landing-failed mission(s).",
                Links = new List<AskLink> { new AskLink("Missions", "/missions") }
            };
        }

        private async Task<AskResponse> AnswerMissionsAsync(AuthContext auth, CancellationToken token)
        {
            List<Mission> pending = await MissionsByStatusAsync(auth, MissionStatusEnum.Pending, token).ConfigureAwait(false);
            List<Mission> inProgress = await MissionsByStatusAsync(auth, MissionStatusEnum.InProgress, token).ConfigureAwait(false);
            List<Mission> review = await MissionsByStatusAsync(auth, MissionStatusEnum.Review, token).ConfigureAwait(false);
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Answer,
                Reply = "Missions: " + pending.Count + " pending, " + inProgress.Count + " in progress, " +
                    review.Count + " awaiting review.",
                Links = new List<AskLink> { new AskLink("Missions", "/missions") }
            };
        }

        private async Task<AskResponse> AnswerVoyagesAsync(CancellationToken token)
        {
            ArmadaStatus status = await _Admiral.GetStatusAsync(token).ConfigureAwait(false);
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Answer,
                Reply = status.ActiveVoyages + " active voyage(s).",
                Links = new List<AskLink> { new AskLink("Voyages", "/voyages") }
            };
        }

        private static AskResponse BuildDocksAnswer()
        {
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Answer,
                Reply = "Docks are git worktrees for captains. If one is stuck, use Repair or Unstick on the Docks page -- both are non-destructive.",
                Links = new List<AskLink> { new AskLink("Docks", "/docks") }
            };
        }

        private static AskResponse BuildHelp()
        {
            return new AskResponse
            {
                Kind = AskResponseKindEnum.Help,
                Reply = "I can answer questions about fleet state. Try: \"status\", \"how many captains?\", " +
                    "\"any failures?\", \"anything stalled?\", \"missions\", \"voyages\", or \"docks\".",
                Links = new List<AskLink>
                {
                    new AskLink("Dashboard", "/"),
                    new AskLink("Captains", "/captains"),
                    new AskLink("Missions", "/missions")
                }
            };
        }

        private static bool Contains(string text, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (text.Contains(needle, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private async System.Threading.Tasks.Task<List<Mission>> MissionsByStatusAsync(AuthContext auth, MissionStatusEnum status, CancellationToken token)
        {
            if (auth == null || auth.IsAdmin) return await _Database.Missions.EnumerateByStatusAsync(status, token).ConfigureAwait(false);
            List<Mission> list = await _Database.Missions.EnumerateByStatusAsync(auth.TenantId!, status, token).ConfigureAwait(false);
            return auth.IsTenantAdmin ? list : list.Where(m => String.Equals(m.UserId, auth.UserId, StringComparison.Ordinal)).ToList();
        }

        #endregion
    }
}
