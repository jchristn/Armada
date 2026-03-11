namespace Armada.Core.Services
{
    using SyslogLogging;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Renders message templates with placeholder substitution for commit messages and PR descriptions.
    /// </summary>
    public class MessageTemplateService : IMessageTemplateService
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[MessageTemplateService] ";
        private LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public MessageTemplateService(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace placeholder parameters in a template string.
        /// </summary>
        public string RenderTemplate(string template, Dictionary<string, string> parameters)
        {
            if (String.IsNullOrEmpty(template)) return "";
            if (parameters == null || parameters.Count == 0) return template;

            string result = template;
            foreach (KeyValuePair<string, string> kvp in parameters)
            {
                result = result.Replace("{" + kvp.Key + "}", kvp.Value ?? "");
            }

            return result;
        }

        /// <summary>
        /// Build a context dictionary from domain objects.
        /// </summary>
        public Dictionary<string, string> BuildContext(Mission mission, Captain? captain = null, Vessel? vessel = null, Voyage? voyage = null, Dock? dock = null)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            Dictionary<string, string> context = new Dictionary<string, string>
            {
                ["MissionId"] = mission.Id,
                ["MissionTitle"] = mission.Title,
                ["VoyageId"] = mission.VoyageId ?? "",
                ["CaptainId"] = mission.CaptainId ?? "",
                ["VesselId"] = mission.VesselId ?? "",
                ["BranchName"] = mission.BranchName ?? "",
                ["Timestamp"] = DateTime.UtcNow.ToString("o")
            };

            if (captain != null)
            {
                context["CaptainId"] = captain.Id;
                context["CaptainName"] = captain.Name;
            }
            else
            {
                context["CaptainName"] = "";
            }

            if (vessel != null)
            {
                context["VesselId"] = vessel.Id;
                context["VesselName"] = vessel.Name;
                context["FleetId"] = vessel.FleetId ?? "";
            }
            else
            {
                context["VesselName"] = "";
                context["FleetId"] = "";
            }

            if (voyage != null)
            {
                context["VoyageId"] = voyage.Id;
                context["VoyageTitle"] = voyage.Title;
            }
            else
            {
                context["VoyageTitle"] = "";
            }

            if (dock != null)
            {
                context["DockId"] = dock.Id;
                if (!String.IsNullOrEmpty(dock.BranchName))
                    context["BranchName"] = dock.BranchName;
            }
            else
            {
                context["DockId"] = "";
            }

            return context;
        }

        /// <summary>
        /// Render commit message instructions for injection into an agent prompt.
        /// </summary>
        public string RenderCommitInstructions(MessageTemplateSettings settings, Dictionary<string, string> context)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!settings.EnableCommitMetadata) return "";

            string rendered = RenderTemplate(settings.CommitMessageTemplate, context);
            if (String.IsNullOrWhiteSpace(rendered)) return "";

            string instructions =
                "IMPORTANT: For every git commit you create, append the following trailers " +
                "at the end of your commit message (after a blank line):" +
                rendered;

            _Logging.Debug(_Header + "rendered commit instructions for agent prompt");
            return instructions;
        }

        /// <summary>
        /// Render a PR description by appending template metadata to the base body.
        /// </summary>
        public string RenderPrDescription(MessageTemplateSettings settings, string baseBody, Dictionary<string, string> context)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!settings.EnablePrMetadata) return baseBody ?? "";

            string rendered = RenderTemplate(settings.PrDescriptionTemplate, context);
            if (String.IsNullOrWhiteSpace(rendered)) return baseBody ?? "";

            _Logging.Debug(_Header + "rendered PR description template");
            return (baseBody ?? "") + rendered;
        }

        /// <summary>
        /// Render a merge commit message from the template.
        /// </summary>
        public string? RenderMergeCommitMessage(MessageTemplateSettings settings, Dictionary<string, string> context)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!settings.EnableCommitMetadata) return null;

            string rendered = RenderTemplate(settings.MergeCommitTemplate, context);
            if (String.IsNullOrWhiteSpace(rendered)) return null;

            _Logging.Debug(_Header + "rendered merge commit message template");
            return rendered;
        }

        #endregion
    }
}
