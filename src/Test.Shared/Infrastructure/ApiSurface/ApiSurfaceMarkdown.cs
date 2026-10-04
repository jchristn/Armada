namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Renders docs/API_SURFACE_1.0.md from an <see cref="ApiSurfaceDocument"/>.
    /// </summary>
    public static class ApiSurfaceMarkdown
    {
        #region Public-Methods

        /// <summary>
        /// Render the surface as Markdown.
        /// </summary>
        /// <param name="document">Surface.</param>
        /// <returns>Markdown text with LF line endings.</returns>
        public static string Render(ApiSurfaceDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            StringBuilder sb = new StringBuilder();

            sb.Append("# Armada API Surface " + document.SurfaceVersion + "\n\n");
            sb.Append("> **Type:** reference (generated). Do not edit by hand: run `" + document.Generator + "`, which boots a throwaway\n");
            sb.Append("> Admiral and rewrites this file and `docs/api-surface-1.0.json` from the running code. The `E2E.ApiContract`\n");
            sb.Append("> test compares the live surface to the JSON file and fails on removals and incompatible changes. What the\n");
            sb.Append("> promise covers, and how items are deprecated, marked experimental, and graduated, is in\n");
            sb.Append("> [COMPATIBILITY.md](COMPATIBILITY.md).\n\n");

            sb.Append("Items marked **experimental** are excluded from the 1.0 compatibility promise. Authorization levels are the\n");
            sb.Append("declared requirements from `RouteAuthorizationRegistry` and `McpToolAuthorizationRegistry`: `NoAuthRequired`,\n");
            sb.Append("`Authenticated`, `TenantAdmin` (tenant administrator or global administrator), `AdminOnly` (global\n");
            sb.Append("administrator).\n\n");

            sb.Append("## Contents\n\n");
            sb.Append("- [Counts](#counts)\n- [REST API](#rest-api)\n- [MCP tools](#mcp-tools)\n- [WebSocket](#websocket)\n- [CLI](#cli)\n- [Settings](#settings)\n\n");

            sb.Append("## Counts\n\n");
            sb.Append("| Surface | Total | Experimental |\n|---|---|---|\n");
            sb.Append("| REST routes | " + document.Rest.Count + " | " + document.Rest.Count(r => r.Experimental) + " |\n");
            sb.Append("| MCP tools | " + document.Mcp.Count + " | " + document.Mcp.Count(t => t.Experimental) + " |\n");
            sb.Append("| WebSocket endpoints | " + document.WebSocket.Endpoints.Count + " | " + document.WebSocket.Endpoints.Count(e => e.Experimental) + " |\n");
            sb.Append("| WebSocket commands | " + document.WebSocket.Commands.Count + " | 0 |\n");
            sb.Append("| WebSocket event types | " + document.WebSocket.Events.Count + " | 0 |\n");
            sb.Append("| CLI commands | " + document.Cli.Count + " | 0 |\n");
            sb.Append("| Settings keys | " + document.Settings.Count + " | " + document.Settings.Count(s => s.Experimental) + " |\n\n");

            RenderRest(sb, document.Rest);
            RenderMcp(sb, document.Mcp);
            RenderWebSocket(sb, document.WebSocket);
            RenderCli(sb, document.Cli);
            RenderSettings(sb, document.Settings);
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static string Cell(string? text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            return text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string Code(string? text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            return "`" + text.Replace("|", "\\|") + "`";
        }

        private static void RenderRest(StringBuilder sb, List<ApiRestRoute> routes)
        {
            sb.Append("## REST API\n\n");
            sb.Append("Base path `/api/v1`. Request and response bodies are PascalCase JSON; errors are `ApiErrorResponse` (see\n");
            sb.Append("[REST_API.md](REST_API.md)). Type names are the OpenAPI schema names (`GET /openapi.json`).\n\n");
            sb.Append("| Method | Route | Auth | Request | Responses | Status |\n|---|---|---|---|---|---|\n");
            foreach (ApiRestRoute route in routes)
            {
                string request = route.RequestType == null ? "" : Code(route.RequestType) + (route.RequestRequired ? "" : " (optional)");
                string responses = String.Join(", ", route.Responses.Select(r => r.Key + (String.IsNullOrEmpty(r.Value) ? "" : " " + Code(r.Value))));
                sb.Append("| " + route.Method + " | " + Code(route.Route) + " | " + route.Auth + " | " + request + " | " + responses + " | " + (route.Experimental ? "experimental" : "") + " |\n");
            }

            sb.Append("\n");
        }

        private static void RenderMcp(StringBuilder sb, List<ApiMcpTool> tools)
        {
            sb.Append("## MCP tools\n\n");
            sb.Append("Arguments are camelCase; `*` marks a required argument. See [MCP_API.md](MCP_API.md).\n\n");
            sb.Append("| Tool | Auth | Arguments | Status |\n|---|---|---|---|\n");
            foreach (ApiMcpTool tool in tools)
            {
                string arguments = String.Join(", ", tool.Arguments.Select(a => Code(a.Name + (a.Required ? "*" : "") + ": " + a.Type)));
                sb.Append("| " + Code(tool.Name) + " | " + tool.Auth + " | " + arguments + " | " + (tool.Experimental ? "experimental" : "") + " |\n");
            }

            sb.Append("\n");
        }

        private static void RenderWebSocket(StringBuilder sb, ApiWebSocketSurface ws)
        {
            sb.Append("## WebSocket\n\n");
            sb.Append("Server-to-client messages are camelCase JSON; client field names are matched case-insensitively. See\n");
            sb.Append("[WEBSOCKET_API.md](WEBSOCKET_API.md).\n\n");

            sb.Append("### Endpoints\n\n| Name | Path | Status |\n|---|---|---|\n");
            foreach (ApiWebSocketEndpoint endpoint in ws.Endpoints)
                sb.Append("| " + endpoint.Name + " | " + Code(endpoint.Path) + " | " + (endpoint.Experimental ? "experimental" : "") + " |\n");
            sb.Append("\n");

            sb.Append("### Messages\n\n");
            sb.Append("- Client routes: " + String.Join(", ", ws.Routes.Select(Code)) + "\n");
            sb.Append("- Client message fields: " + String.Join(", ", ws.ClientMessageFields.Select(Code)) + "\n");
            sb.Append("- Event envelope fields: " + String.Join(", ", ws.EventEnvelopeFields.Select(Code)) + "\n");
            sb.Append("- Command reply fields (`command.result`, `command.error`): " + String.Join(", ", ws.CommandReplyFields.Select(Code)) + "\n\n");

            sb.Append("### Commands\n\n");
            sb.Append(String.Join(", ", ws.Commands.Select(Code)) + "\n\n");

            sb.Append("### Events\n\n");
            sb.Append("Generic events carry a `message` and the payload " + Code("{ entityType, entityId, captainId, missionId, vesselId, voyageId }") + ".\n\n");
            sb.Append("| Type | Scope | Payload |\n|---|---|---|\n");
            foreach (ApiWebSocketEvent evt in ws.Events)
            {
                string payload;
                if (evt.Generic) payload = "generic";
                else if (evt.PayloadType != null) payload = Code(evt.PayloadType) + (evt.Fields.Count > 0 ? " + " + String.Join(", ", evt.Fields.Select(Code)) : "");
                else payload = String.Join(", ", evt.Fields.Select(Code));
                sb.Append("| " + Code(evt.Type) + " | " + evt.Scope + " | " + payload + " |\n");
            }

            sb.Append("\n");
        }

        private static void RenderCli(StringBuilder sb, List<ApiCliCommand> commands)
        {
            sb.Append("## CLI\n\n");
            sb.Append("Commands of the `armada` CLI (Helm). `*` marks a required argument or option. Global options (`--help`,\n");
            sb.Append("`--version`) are omitted.\n\n");
            sb.Append("| Command | Arguments | Options |\n|---|---|---|\n");
            foreach (ApiCliCommand command in commands)
            {
                string arguments = String.Join(" ", command.Parameters.Where(p => p.Kind == "argument").Select(p => Code("<" + p.Name + ">" + (p.Required ? "*" : ""))));
                string options = String.Join(", ", command.Parameters.Where(p => p.Kind == "option").Select(p => Code("--" + p.Name + (p.Short != null ? "|-" + p.Short : "") + (p.Required ? "*" : ""))));
                sb.Append("| " + Code("armada " + command.Command) + " | " + arguments + " | " + options + " |\n");
            }

            sb.Append("\n");
        }

        private static void RenderSettings(StringBuilder sb, List<ApiSettingKey> settings)
        {
            sb.Append("## Settings\n\n");
            sb.Append("Keys of `settings.json` (camelCase; `[]` marks the fields of list elements). Defaults are shown for a fresh\n");
            sb.Append("install with the home directory written as `~`; defaults are not frozen (see COMPATIBILITY.md).\n\n");
            sb.Append("| Key | Type | Default | Status |\n|---|---|---|---|\n");
            foreach (ApiSettingKey key in settings)
            {
                string def = key.Default == null ? "" : Code(key.Default.Length > 80 ? key.Default.Substring(0, 77) + "..." : key.Default);
                sb.Append("| " + Code(key.Key) + " | " + Cell(key.Type) + " | " + def + " | " + (key.Experimental ? "experimental" : "") + " |\n");
            }
        }

        #endregion
    }
}
