namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Change a model profile's tier, recovery policy, or timeouts. The current profile is read first so each flag
    /// changes only its own field.
    /// </summary>
    [Description("Change a model profile's tier, recovery policy or timeouts")]
    public class ModelProfileUpdateCommand : BaseCommand<ModelProfileUpdateSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ModelProfileUpdateSettings settings, CancellationToken cancellationToken)
        {
            bool recoveryFlags = settings.MaxResumes.HasValue || settings.UnclassifiedResumes.HasValue || settings.TransientGraceMinutes.HasValue
                || settings.JudgeReworkRounds.HasValue || settings.OutputRepairAttempts.HasValue || settings.EscalateAfterResumes.HasValue;
            bool timeoutFlags = settings.StallThresholdMinutes.HasValue || settings.ToolWaitHardLimitMinutes.HasValue
                || settings.MaxRuntimeMinutes.HasValue || settings.FirstOutputMinutes.HasValue;
            bool tierFlag = !String.IsNullOrWhiteSpace(settings.Tier);
            if (!recoveryFlags && !timeoutFlags && !tierFlag)
            {
                HelmConsole.Error.MarkupLine("[red]Nothing to change. Pass --tier, a recovery flag or a timeout flag.[/]");
                return 1;
            }

            string path = "/api/v1/model-profiles/" + Uri.EscapeDataString(settings.ProfileId);
            ModelProfile? current = await GetAsync<ModelProfile>(path).ConfigureAwait(false);
            if (current == null)
            {
                HelmConsole.Error.MarkupLine("[red]Model profile not found.[/]");
                return 1;
            }

            ModelProfileUpdateRequest request = new ModelProfileUpdateRequest();
            try
            {
                if (tierFlag)
                {
                    ModelProfileTiering tiering = new ModelProfileTiering();
                    if (!String.Equals(settings.Tier!.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Enum.TryParse<CaptainTierEnum>(settings.Tier.Trim(), true, out CaptainTierEnum tier) || !Enum.IsDefined(typeof(CaptainTierEnum), tier))
                        {
                            HelmConsole.Error.MarkupLine("[red]--tier must be Economy, Standard, Premium or none.[/]");
                            return 1;
                        }
                        tiering.Tier = tier;
                    }
                    request.Tiering = tiering;
                }

                if (recoveryFlags)
                {
                    ModelProfileRecovery recovery = CopyRecovery(current.Recovery);
                    if (settings.MaxResumes.HasValue) recovery.MaxResumes = Override(settings.MaxResumes.Value);
                    if (settings.UnclassifiedResumes.HasValue) recovery.UnclassifiedResumes = Override(settings.UnclassifiedResumes.Value);
                    if (settings.TransientGraceMinutes.HasValue) recovery.TransientGraceMinutes = Override(settings.TransientGraceMinutes.Value);
                    if (settings.JudgeReworkRounds.HasValue) recovery.JudgeReworkRounds = Override(settings.JudgeReworkRounds.Value);
                    if (settings.OutputRepairAttempts.HasValue) recovery.OutputRepairAttempts = Override(settings.OutputRepairAttempts.Value);
                    if (settings.EscalateAfterResumes.HasValue) recovery.EscalateAfterResumes = Override(settings.EscalateAfterResumes.Value);
                    request.Recovery = recovery;
                }

                if (timeoutFlags)
                {
                    ModelProfileTimeouts timeouts = CopyTimeouts(current.Timeouts);
                    if (settings.StallThresholdMinutes.HasValue) timeouts.StallThresholdMinutes = Override(settings.StallThresholdMinutes.Value);
                    if (settings.ToolWaitHardLimitMinutes.HasValue) timeouts.ToolWaitHardLimitMinutes = Override(settings.ToolWaitHardLimitMinutes.Value);
                    if (settings.MaxRuntimeMinutes.HasValue) timeouts.MaxRuntimeMinutes = Override(settings.MaxRuntimeMinutes.Value);
                    if (settings.FirstOutputMinutes.HasValue) timeouts.FirstOutputMinutes = Override(settings.FirstOutputMinutes.Value);
                    request.Timeouts = timeouts;
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                HelmConsole.Error.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                return 1;
            }

            ModelProfile? updated = await PutAsync<ModelProfile>(path, request).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(updated);
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Model profile updated:[/] [bold]{Markup.Escape(settings.ProfileId)}[/]");
            if (updated != null) ModelProfileRendering.Write(updated);
            return 0;
        }

        private static int? Override(int value)
        {
            return value < 0 ? null : value;
        }

        private static ModelProfileRecovery CopyRecovery(ModelProfileRecovery source)
        {
            return new ModelProfileRecovery
            {
                MaxResumes = source.MaxResumes,
                UnclassifiedResumes = source.UnclassifiedResumes,
                TransientGraceMinutes = source.TransientGraceMinutes,
                JudgeReworkRounds = source.JudgeReworkRounds,
                OutputRepairAttempts = source.OutputRepairAttempts,
                EscalateAfterResumes = source.EscalateAfterResumes
            };
        }

        private static ModelProfileTimeouts CopyTimeouts(ModelProfileTimeouts source)
        {
            return new ModelProfileTimeouts
            {
                StallThresholdMinutes = source.StallThresholdMinutes,
                ToolWaitHardLimitMinutes = source.ToolWaitHardLimitMinutes,
                MaxRuntimeMinutes = source.MaxRuntimeMinutes,
                FirstOutputMinutes = source.FirstOutputMinutes
            };
        }
    }
}
