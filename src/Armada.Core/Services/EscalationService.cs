namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for evaluating escalation rules and sending notifications.
    /// </summary>
    public class EscalationService : IEscalationService
    {
        #region Private-Members

        private string _Header = "[EscalationService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private HttpClient _HttpClient;

        private ConcurrentDictionary<string, DateTime> _Cooldowns = new ConcurrentDictionary<string, DateTime>();

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        public EscalationService(LoggingModule logging, DatabaseDriver database, ArmadaSettings settings)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _HttpClient = new HttpClient();
            _HttpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EvaluateAsync(CancellationToken token = default)
        {
            if (_Settings.EscalationRules.Count == 0) return;

            foreach (EscalationRule rule in _Settings.EscalationRules)
            {
                if (!rule.Enabled) continue;

                switch (rule.Trigger)
                {
                    case EscalationTriggerEnum.CaptainStalled:
                        await EvaluateCaptainStalledAsync(rule, token).ConfigureAwait(false);
                        break;

                    case EscalationTriggerEnum.MissionOverdue:
                        await EvaluateMissionOverdueAsync(rule, token).ConfigureAwait(false);
                        break;

                    case EscalationTriggerEnum.PoolExhausted:
                        await EvaluatePoolExhaustedAsync(rule, token).ConfigureAwait(false);
                        break;
                }
            }
        }

        /// <inheritdoc />
        public async Task FireAsync(EscalationTriggerEnum trigger, string entityId, string message, CancellationToken token = default)
        {
            List<EscalationRule> matchingRules = _Settings.EscalationRules
                .Where(r => r.Enabled && r.Trigger == trigger)
                .ToList();

            foreach (EscalationRule rule in matchingRules)
            {
                await ExecuteActionAsync(rule, entityId, message, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private async Task EvaluateCaptainStalledAsync(EscalationRule rule, CancellationToken token)
        {
            List<Captain> stalledCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Stalled, token).ConfigureAwait(false);

            foreach (Captain captain in stalledCaptains)
            {
                TimeSpan stalledDuration = DateTime.UtcNow - captain.LastUpdateUtc;
                if (stalledDuration.TotalMinutes >= rule.ThresholdMinutes)
                {
                    string message = "Captain " + captain.Id + " (" + captain.Name + ") stalled for " + stalledDuration.TotalMinutes.ToString("F0") + " minutes";
                    await ExecuteActionAsync(rule, captain.Id, message, token).ConfigureAwait(false);
                }
            }
        }

        private async Task EvaluateMissionOverdueAsync(EscalationRule rule, CancellationToken token)
        {
            List<Mission> activeMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress, token).ConfigureAwait(false);

            foreach (Mission mission in activeMissions)
            {
                if (!mission.StartedUtc.HasValue) continue;

                TimeSpan elapsed = DateTime.UtcNow - mission.StartedUtc.Value;
                if (elapsed.TotalMinutes >= rule.ThresholdMinutes)
                {
                    string message = "Mission " + mission.Id + " (" + mission.Title + ") in progress for " + elapsed.TotalMinutes.ToString("F0") + " minutes";
                    await ExecuteActionAsync(rule, mission.Id, message, token).ConfigureAwait(false);
                }
            }
        }

        private async Task EvaluatePoolExhaustedAsync(EscalationRule rule, CancellationToken token)
        {
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            List<Mission> pendingMissions = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);

            if (idleCaptains.Count == 0 && pendingMissions.Count > 0)
            {
                string message = "No idle captains available; " + pendingMissions.Count + " mission(s) pending";
                await ExecuteActionAsync(rule, "pool", message, token).ConfigureAwait(false);
            }
        }

        private async Task ExecuteActionAsync(EscalationRule rule, string entityId, string message, CancellationToken token)
        {
            // Check cooldown
            string cooldownKey = rule.Trigger.ToString() + ":" + entityId;
            if (_Cooldowns.TryGetValue(cooldownKey, out DateTime lastFired))
            {
                if ((DateTime.UtcNow - lastFired).TotalMinutes < rule.CooldownMinutes)
                    return;
            }

            _Cooldowns[cooldownKey] = DateTime.UtcNow;

            switch (rule.Action)
            {
                case EscalationActionEnum.Log:
                    _Logging.Warn(_Header + "ESCALATION [" + rule.Trigger + "] " + message);
                    break;

                case EscalationActionEnum.Webhook:
                    await SendWebhookAsync(rule, message, token).ConfigureAwait(false);
                    break;
            }

            // Also log a signal for audit trail
            Signal signal = new Signal(SignalTypeEnum.Error, "Escalation: " + message);
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);
        }

        private async Task SendWebhookAsync(EscalationRule rule, string message, CancellationToken token)
        {
            if (String.IsNullOrEmpty(rule.WebhookUrl))
            {
                _Logging.Warn(_Header + "webhook URL not configured for escalation rule");
                return;
            }

            try
            {
                object payload = new
                {
                    trigger = rule.Trigger.ToString(),
                    message = message,
                    timestamp = DateTime.UtcNow,
                    source = "armada"
                };

                string json = JsonSerializer.Serialize(payload, _JsonOptions);
                StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _HttpClient.PostAsync(rule.WebhookUrl, content, token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _Logging.Warn(_Header + "webhook returned " + (int)response.StatusCode + " for " + rule.WebhookUrl);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "webhook error for " + rule.WebhookUrl + ": " + ex.Message);
            }
        }

        #endregion
    }
}
