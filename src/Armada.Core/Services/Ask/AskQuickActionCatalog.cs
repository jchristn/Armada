namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Armada.Core.Models;

    /// <summary>
    /// The built-in Ask Armada quick actions offered by the composer's slash menu. Each submits an MCP tool call through
    /// POST /api/v1/ask/threads/{id}/actions with the tool's own argument names, so the inline forms work even when no
    /// captain is connected to MCP.
    /// </summary>
    public static class AskQuickActionCatalog
    {
        #region Public-Methods

        /// <summary>
        /// The quick action catalog in menu order.
        /// </summary>
        /// <returns>Quick actions.</returns>
        public static List<AskQuickAction> All()
        {
            List<AskQuickAction> actions = new List<AskQuickAction>();

            actions.Add(Action("/dispatch", "Dispatch a voyage", "Dispatch missions to a vessel as a new voyage and follow it here.", "dispatch", false,
                Arg("title", "Title", "string", true, "Voyage title"),
                Arg("vesselId", "Vessel", "vessel", true, "Target vessel (vsl_ prefix)"),
                Arg("missions", "Missions", "missions", true, "One or more missions, each with a title and description"),
                Arg("description", "Description", "text", false, "Optional voyage description"),
                Arg("pipelineId", "Pipeline", "pipeline", false, "Optional pipeline (overrides the vessel default)")));

            actions.Add(Action("/fleet-action", "Run a fleet action", "Run a saved fleet action across vessels and follow every target here.", "run_fleet_action", true,
                Arg("actionId", "Fleet action", "fleetAction", true, "Saved fleet action (fac_ prefix)"),
                Arg("vesselIds", "Vessels", "vessels", true, "Target vessels (vsl_ prefix)"),
                Arg("concurrency", "Concurrency", "integer", false, "Targets run at once (1-32); defaults to the action's")));

            actions.Add(Action("/status", "Armada status", "Show the current fleet, captain, and mission status.", "status", false));

            actions.Add(Action("/health", "Evaluate vessel health", "Evaluate the health of selected vessels (or all) and follow the evaluation job here.", "evaluate_vessel_health", true,
                Arg("vesselIds", "Vessels", "vessels", false, "Vessels to evaluate; omit for every vessel"),
                Arg("fleetId", "Fleet", "fleet", false, "Evaluate every vessel of a fleet"),
                Arg("force", "Force", "boolean", false, "Re-evaluate even when results are fresh", "false")));

            actions.Add(Action("/import", "Discover repositories", "Scan directories on the Admiral host for git repositories to import as vessels.", "discover_vessels", true,
                Arg("roots", "Roots", "paths", false, "Directories to scan recursively"),
                Arg("directories", "Directories", "paths", false, "Specific repository directories"),
                Arg("maxDepth", "Max depth", "integer", false, "Maximum scan depth"),
                Arg("runInBackground", "Run in background", "boolean", false, "Run discovery as a background job", "true")));

            foreach (AskQuickAction action in actions) action.ReadOnly = AskToolPolicy.IsReadOnly(action.ToolName);
            return actions;
        }

        /// <summary>
        /// Find a quick action by slash command or tool name.
        /// </summary>
        /// <param name="commandOrTool">Slash command (with or without the slash) or tool name.</param>
        /// <returns>The quick action, or null.</returns>
        public static AskQuickAction? Find(string? commandOrTool)
        {
            if (String.IsNullOrWhiteSpace(commandOrTool)) return null;
            string key = commandOrTool.Trim();
            string command = key.StartsWith("/", StringComparison.Ordinal) ? key : "/" + key;
            return All().FirstOrDefault(a => String.Equals(a.Command, command, StringComparison.OrdinalIgnoreCase) || String.Equals(a.ToolName, key, StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        private static AskQuickAction Action(string command, string title, string description, string toolName, bool requiresTenantAdmin, params AskQuickActionArgument[] args)
        {
            AskQuickAction action = new AskQuickAction();
            action.Name = command.TrimStart('/');
            action.Command = command;
            action.Title = title;
            action.Description = description;
            action.ToolName = toolName;
            action.RequiresTenantAdmin = requiresTenantAdmin;
            action.Arguments = args.ToList();
            action.ArgumentsSchema = BuildSchema(action.Arguments);
            return action;
        }

        private static JsonObject BuildSchema(List<AskQuickActionArgument> args)
        {
            JsonObject properties = new JsonObject();
            JsonArray required = new JsonArray();
            foreach (AskQuickActionArgument arg in args)
            {
                JsonObject property = new JsonObject();
                switch (arg.Type)
                {
                    case "boolean":
                        property["type"] = "boolean";
                        break;
                    case "integer":
                        property["type"] = "integer";
                        break;
                    case "vessels":
                    case "paths":
                        property["type"] = "array";
                        property["items"] = new JsonObject { ["type"] = "string" };
                        break;
                    case "missions":
                        property["type"] = "array";
                        property["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["title"] = new JsonObject { ["type"] = "string" },
                                ["description"] = new JsonObject { ["type"] = "string" }
                            },
                            ["required"] = new JsonArray("title")
                        };
                        break;
                    default:
                        property["type"] = "string";
                        break;
                }

                property["title"] = arg.Label;
                property["x-armada-field"] = arg.Type;
                if (!String.IsNullOrEmpty(arg.Description)) property["description"] = arg.Description;
                if (!String.IsNullOrEmpty(arg.DefaultValue)) property["default"] = arg.DefaultValue;
                properties[arg.Name] = property;
                if (arg.Required) required.Add(arg.Name);
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            };
        }

        private static AskQuickActionArgument Arg(string name, string label, string type, bool required, string description, string? defaultValue = null)
        {
            AskQuickActionArgument arg = new AskQuickActionArgument();
            arg.Name = name;
            arg.Label = label;
            arg.Type = type;
            arg.Required = required;
            arg.Description = description;
            arg.DefaultValue = defaultValue;
            return arg;
        }

        #endregion
    }
}
