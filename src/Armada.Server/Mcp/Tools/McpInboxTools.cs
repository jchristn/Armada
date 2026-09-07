namespace Armada.Server.Mcp.Tools
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Registers the MCP inbox tool: the consolidated list of things across the fleet that require a
    /// human's attention or action. This is the tool an agent uses to answer an operator asking whether
    /// anything is waiting on them.
    /// </summary>
    public static class McpInboxTools
    {
        /// <summary>
        /// Register the inbox MCP tool with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver used to build the inbox.</param>
        /// <param name="logging">Logging module.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, LoggingModule logging)
        {
            InboxService inbox = new InboxService(database, logging);

            register(
                "inbox",
                "Return the operator's inbox: everything across the fleet that requires a human's attention or action right now, ordered most-urgent first. Use this to answer questions like \"Is there anything waiting on me?\", \"Is there anything that needs my attention?\", or \"Do I have any action items from Armada work?\". It surfaces two kinds of item: (a) work awaiting your decision (human-in-the-loop) -- missions in Review to approve or reject, and deployments pending approval; and (b) autonomous work that failed and needs intervention (human-out-of-the-loop) -- failed missions, missions whose work could not land (landing failed), failed merges, failed or verification-failed deployments, and stalled captains. Purely informational events such as completions or normal progress are deliberately excluded. Each item has: kind, severity (Critical, Warning, or Info), title, detail, the related entity (entityType and entityId), and a dashboard href. An empty items list means nothing currently needs the operator's attention.",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    List<InboxItem> items = await inbox.GetInboxAsync(McpToolHelpers.ResolveCallerContext()).ConfigureAwait(false);
                    int critical = 0;
                    int warning = 0;
                    foreach (InboxItem item in items)
                    {
                        if (item.Severity == Armada.Core.Enums.InboxSeverityEnum.Critical) critical++;
                        else if (item.Severity == Armada.Core.Enums.InboxSeverityEnum.Warning) warning++;
                    }

                    return (object)new
                    {
                        count = items.Count,
                        criticalCount = critical,
                        warningCount = warning,
                        items
                    };
                });
        }
    }
}
