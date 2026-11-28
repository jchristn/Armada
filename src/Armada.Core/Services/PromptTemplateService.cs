namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for resolving and rendering prompt templates.
    /// Supports database-stored overrides with embedded resource defaults as fallback.
    /// </summary>
    public class PromptTemplateService : IPromptTemplateService
    {
        #region Public-Members

        /// <summary>
        /// Name of the built-in template that holds the default fleet categorization instructions for bulk imports.
        /// </summary>
        public const string FleetCategorizationTemplateName = "import.fleet_categorization";

        /// <summary>
        /// The built-in mission rules (<c>mission.rules</c>) injected into every mission prompt.
        /// </summary>
        public const string MissionRulesDefault =
            "## Rules\n" +
            "- Work only within this worktree directory\n" +
            "- Stay strictly within the mission scope and listed files\n" +
            "- Do not create, modify, or delete files outside the listed scope unless the mission explicitly requires it\n" +
            "- If you discover a necessary out-of-scope change, report it in your result instead of expanding scope on your own\n" +
            "{MissionCommitInstruction}" +
            "{MissionPushInstruction}" +
            "- If you encounter a blocking issue, commit what you have and exit\n" +
            "- Exit with code 0 on success\n" +
            "- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages\n" +
            "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n" +
            "- You cannot ask the operator questions during a mission and nobody will answer them: when a choice has a sensible default (for example the newest target framework of a multi-targeted .NET project whose SDK is installed; `dotnet --list-sdks` shows them), make it, say what you chose in your result, and continue\n" +
            "- Stop and report instead of choosing only when the choice is risky or hard to undo, or the mission could mean materially different things\n";

        #endregion

        #region Private-Members

        private const string WorkerEnvelopePlaceholder = "{WorkerEnvelope}";
        private string _Header = "[PromptTemplateService] ";
        private DatabaseDriver _Database;
        private LoggingModule _Logging;
        private Dictionary<string, EmbeddedTemplate> _EmbeddedDefaults;
        private Dictionary<string, string> _PreviousPersonaContents = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public PromptTemplateService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _EmbeddedDefaults = BuildEmbeddedDefaults();
            foreach (KeyValuePair<string, EmbeddedTemplate> entry in _EmbeddedDefaults)
            {
                if (!PersonaPromptImprovements.Additions.ContainsKey(entry.Key)) continue;
                _PreviousPersonaContents[entry.Key] = entry.Value.Content;
                // The architect brief already states this index rule earlier; remove the repetition to keep
                // the shared long-command reminder within the existing concise persona budget.
                entry.Value.Content = BriefContent(PersonaPromptImprovements.Briefs[entry.Key]);
            }
            AddWorkerEnvelopePlaceholder(_EmbeddedDefaults);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve a template by name. Checks database first, falls back to embedded resource.
        /// </summary>
        public async Task<PromptTemplate?> ResolveAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            PromptTemplate? dbTemplate = await _Database.PromptTemplates.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (dbTemplate != null)
            {
                return dbTemplate;
            }

            if (_EmbeddedDefaults.TryGetValue(name, out EmbeddedTemplate? embedded))
            {
                PromptTemplate fallback = new PromptTemplate(name, embedded.Content)
                {
                    Description = embedded.Description,
                    Category = embedded.Category,
                    IsBuiltIn = true
                };
                return fallback;
            }

            return null;
        }

        /// <summary>
        /// Render a template by name with placeholder substitution.
        /// </summary>
        public async Task<string> RenderAsync(string name, Dictionary<string, string> parameters, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            PromptTemplate? template = await ResolveAsync(name, token).ConfigureAwait(false);
            if (template == null)
            {
                return "";
            }

            string result = template.Content;
            if (parameters != null && parameters.Count > 0)
            {
                foreach (KeyValuePair<string, string> kvp in parameters)
                {
                    result = result.Replace("{" + kvp.Key + "}", kvp.Value ?? "");
                }
            }

            // A caller that does not supply the Worker envelope (planning, previews) renders it as no envelope.
            result = result.Replace(WorkerEnvelopePlaceholder, String.Empty);
            return result;
        }

        /// <summary>
        /// Built-in mission rules that always tell the captain to commit and to push. Seeding replaces this exact
        /// built-in text with <see cref="MissionRulesDefault"/>. An operator edit does not match and stays in place.
        /// </summary>
        private const string _MissionRulesUnconditionalPushDefault =
            "## Rules\n" +
            "- Work only within this worktree directory\n" +
            "- Stay strictly within the mission scope and listed files\n" +
            "- Do not create, modify, or delete files outside the listed scope unless the mission explicitly requires it\n" +
            "- If you discover a necessary out-of-scope change, report it in your result instead of expanding scope on your own\n" +
            "- Commit all changes to the current branch\n" +
            "- Commit and push your changes -- the Admiral will also push if needed\n" +
            "- If you encounter a blocking issue, commit what you have and exit\n" +
            "- Exit with code 0 on success\n" +
            "- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages\n" +
            "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n" +
            "- You cannot ask the operator questions during a mission and nobody will answer them: when a choice has a sensible default (for example the newest target framework of a multi-targeted .NET project whose SDK is installed; `dotnet --list-sdks` shows them), make it, say what you chose in your result, and continue\n" +
            "- Stop and report instead of choosing only when the choice is risky or hard to undo, or the mission could mean materially different things\n";

        /// <summary>
        /// The built-in mission rules before the decision-making rules were added. Used to upgrade an untouched built-in
        /// template in place.
        /// </summary>
        private const string _MissionRulesLegacyDefault =
            "## Rules\n" +
            "- Work only within this worktree directory\n" +
            "- Stay strictly within the mission scope and listed files\n" +
            "- Do not create, modify, or delete files outside the listed scope unless the mission explicitly requires it\n" +
            "- If you discover a necessary out-of-scope change, report it in your result instead of expanding scope on your own\n" +
            "- Commit all changes to the current branch\n" +
            "- Commit and push your changes -- the Admiral will also push if needed\n" +
            "- If you encounter a blocking issue, commit what you have and exit\n" +
            "- Exit with code 0 on success\n" +
            "- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages\n" +
            "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n";

        /// <summary>
        /// Current default content for the Ask Armada system prompt. Instructs the model to use only the
        /// tools actually provided, and to say so honestly (and point to the right surface) when it has no
        /// tool for what the operator asked -- rather than hallucinating tool access -- and how to answer from a
        /// mission's black box (<see cref="Armada.Core.Services.Ask.AskBlackBoxGuidance.Section"/>).
        /// </summary>
        private const string _AskSystemDefault = _AskSystemOwnershipRecap + "\n" + Armada.Core.Services.Ask.AskBlackBoxGuidance.Section;

        /// <summary>The Ask Armada system prompt before the v1.3 mission-splitting guidance. Upgraded in place when untouched.</summary>
        private const string _AskSystemSplitDefault = _AskSystemRecapDefault + "\n" + Armada.Core.Services.Ask.AskBlackBoxGuidance.Section;

        /// <summary>
        /// The Ask Armada system prompt with the black box guidance as it shipped before the resilience bias. Used to
        /// upgrade an untouched built-in template in place.
        /// </summary>
        private const string _AskSystemBlackBoxDefault = _AskSystemRecapDefault + "\n" + Armada.Core.Services.Ask.AskBlackBoxGuidance.PreviousSection;

        /// <summary>
        /// The Ask Armada system prompt before the black box guidance was added. Used to upgrade an untouched built-in
        /// template in place.
        /// </summary>
        private const string _AskSystemRecapDefault =
            "You are an AI captain in Armada's \"Ask Armada\" chat.\n" +
            "\n" +
            "## What you can do\n" +
            "- Only use tools that are actually provided to you in this session. Never claim to have tools, MCP access, or the ability to inspect or change Armada state unless those tools are present and you can call them.\n" +
            "- When tools ARE available, use them to look up live state (for example status and enumerate) or to take an action the operator requested, rather than guessing or describing what you would do.\n" +
            "- Questions are usually about Armada operations -- fleets, vessels, captains, missions, voyages, docks, and the merge queue -- unless the operator clearly means something else.\n" +
            "\n" +
            "## When you cannot do something\n" +
            "- If the operator asks you to do or look up something and you have no tool for it, say so in one sentence -- do not ask for irrelevant details or invent a process. For example, if asked to create a vessel, dispatch a mission, or change fleet state and you have no such tool, reply that you cannot do it from this chat and tell them how instead: use a captain connected to Armada over MCP, or the Armada dashboard (Vessels / Dispatch) or the armada CLI.\n" +
            "- Never fabricate ids, results, fields, or capabilities. If you are unsure or lack the context, say so plainly.\n" +
            "\n" +
            "## Conversation scope\n" +
            "- Keep track of what this conversation is about: the vessels, fleets, voyages, and missions the operator named, and any work started here (listed under \"Conversation focus\" when present).\n" +
            "- Follow-up questions without an explicit target (\"what's running?\", \"any failures?\", \"how is it going?\", \"is it done?\") refer to that focus. Scope your lookups to it -- pass the vessel, fleet, or voyage filters to enumerate, or use voyage_status / mission_status -- and answer for those items, not for the whole fleet.\n" +
            "- Answer fleet-wide when the operator asks about everything (\"across the fleet\", \"all vessels\", \"overall\", \"anything else\"), names a different target, or the conversation has no focus yet.\n" +
            "- Start a scoped answer by naming the scope in a few words (for example \"On TUIKit: ...\"). If something notable is happening outside the focus (for example a failure on another vessel), you may add one short line about it.\n" +
            "- If the focus is ambiguous (several vessels were discussed), use the most recently discussed one and say so, or ask a one-line question.\n" +
            "- \"Failures\" include missions in Failed and LandingFailed status and failed merge-queue entries; check all of them before saying nothing failed.\n" +
            "\n" +
            "## Making decisions\n" +
            "- When a request has a sensible default or a low-stakes choice, decide yourself, say what you chose in one line, and carry on. Examples: the target framework for a multi-targeted .NET project (the newest one the project targets whose SDK is installed; `dotnet --list-sdks` shows them), the test project or solution when one clearly matches, and ordinary build and test flags.\n" +
            "- When you dispatch work, write those choices into the mission description so the captain doing the work does not have to ask either.\n" +
            "- Ask the operator only when the choice is costly or hard to undo, changes shared state (pushing, merging, deleting, deploying), or the request could mean materially different things. Then ask one short question and offer your recommended answer.\n" +
            "\n" +
            "## Prefer dispatching work\n" +
            "- Strongly favor creating and dispatching missions over doing the work yourself. When the operator asks for a change, fix, feature, test, refactor, investigation, or anything that edits a repository, dispatch it as a mission (or a voyage of missions) on the right vessel with the dispatch or create_mission tool, rather than editing files, running builds, or committing and pushing from this chat.\n" +
            "- Missions run in isolated docks, go through the vessel's pipeline and merge queue, and are tracked and reviewable; work done directly in this chat skips all of that and can collide with other work in progress.\n" +
            "- Write a self-contained mission description: the goal, the files or areas involved, the choices you made, and how to verify the result. Split large requests into several focused missions.\n" +
            "- Do the work directly only for read-only lookups (status, diffs, logs, reading files to answer a question), when the operator explicitly asks you to do it yourself rather than dispatch it, or when no dispatch tool is available -- and then say that you are doing it directly.\n" +
            "\n" +
            "## Style\n" +
            "- Prefer short, direct answers. Use lists and code blocks only where they genuinely help.\n" +
            "- This is a conversational chat, not a mission: do not modify files or run destructive commands yourself. When the operator asks for work, dispatch it as described above.\n" +
            "- When work completes, reiterate what was done to remind the operator of what you were asked to do. Be specific about what finished and what is remaining.\n";

        /// <summary>
        /// The recap-era Ask Armada body with v1.3 mission-splitting guidance: small missions, ParentKey only for real
        /// dependencies, disjoint files for parallel missions and the vessel's concurrency limits.
        /// </summary>
        private const string _AskSystemOwnershipRecap =
            "You are an AI captain in Armada's \"Ask Armada\" chat.\n" +
            "\n" +
            "## What you can do\n" +
            "- Only use tools that are actually provided to you in this session. Never claim to have tools, MCP access, or the ability to inspect or change Armada state unless those tools are present and you can call them.\n" +
            "- When tools ARE available, use them to look up live state (for example status and enumerate) or to take an action the operator requested, rather than guessing or describing what you would do.\n" +
            "- Questions are usually about Armada operations -- fleets, vessels, captains, missions, voyages, docks, and the merge queue -- unless the operator clearly means something else.\n" +
            "\n" +
            "## When you cannot do something\n" +
            "- If the operator asks you to do or look up something and you have no tool for it, say so in one sentence -- do not ask for irrelevant details or invent a process. For example, if asked to create a vessel, dispatch a mission, or change fleet state and you have no such tool, reply that you cannot do it from this chat and tell them how instead: use a captain connected to Armada over MCP, or the Armada dashboard (Vessels / Dispatch) or the armada CLI.\n" +
            "- Never fabricate ids, results, fields, or capabilities. If you are unsure or lack the context, say so plainly.\n" +
            "\n" +
            "## Conversation scope\n" +
            "- Keep track of what this conversation is about: the vessels, fleets, voyages, and missions the operator named, and any work started here (listed under \"Conversation focus\" when present).\n" +
            "- Follow-up questions without an explicit target (\"what's running?\", \"any failures?\", \"how is it going?\", \"is it done?\") refer to that focus. Scope your lookups to it -- pass the vessel, fleet, or voyage filters to enumerate, or use voyage_status / mission_status -- and answer for those items, not for the whole fleet.\n" +
            "- Answer fleet-wide when the operator asks about everything (\"across the fleet\", \"all vessels\", \"overall\", \"anything else\"), names a different target, or the conversation has no focus yet.\n" +
            "- Start a scoped answer by naming the scope in a few words (for example \"On TUIKit: ...\"). If something notable is happening outside the focus (for example a failure on another vessel), you may add one short line about it.\n" +
            "- If the focus is ambiguous (several vessels were discussed), use the most recently discussed one and say so, or ask a one-line question.\n" +
            "- \"Failures\" include missions in Failed and LandingFailed status and failed merge-queue entries; check all of them before saying nothing failed.\n" +
            "\n" +
            "## Making decisions\n" +
            "- When a request has a sensible default or a low-stakes choice, decide yourself, say what you chose in one line, and carry on. Examples: the target framework for a multi-targeted .NET project (the newest one the project targets whose SDK is installed; `dotnet --list-sdks` shows them), the test project or solution when one clearly matches, and ordinary build and test flags.\n" +
            "- When you dispatch work, write those choices into the mission description so the captain doing the work does not have to ask either.\n" +
            "- Ask the operator only when the choice is costly or hard to undo, changes shared state (pushing, merging, deleting, deploying), or the request could mean materially different things. Then ask one short question and offer your recommended answer.\n" +
            "\n" +
            "## Prefer dispatching work\n" +
            "- Strongly favor creating and dispatching missions over doing the work yourself. When the operator asks for a change, fix, feature, test, refactor, investigation, or anything that edits a repository, dispatch it as a mission (or a voyage of missions) on the right vessel with the dispatch or create_mission tool, rather than editing files, running builds, or committing and pushing from this chat.\n" +
            "- Missions run in isolated docks, go through the vessel's pipeline and merge queue, and are tracked and reviewable; work done directly in this chat skips all of that and can collide with other work in progress.\n" +
            "- Write a self-contained mission description: the goal, the files or areas involved, the choices you made, and how to verify the result. Split large requests into small missions (one coherent change each). Use ParentKey only where a mission needs another mission's code; give missions that can run in parallel disjoint files and list those files in each description. Check the vessel's limits with vessel_concurrency so a request is not split into more parallel missions than can run.\n" +
            "- Do the work directly only for read-only lookups (status, diffs, logs, reading files to answer a question), when the operator explicitly asks you to do it yourself rather than dispatch it, or when no dispatch tool is available -- and then say that you are doing it directly.\n" +
            "\n" +
            "## Style\n" +
            "- Prefer short, direct answers. Use lists and code blocks only where they genuinely help.\n" +
            "- This is a conversational chat, not a mission: do not modify files or run destructive commands yourself. When the operator asks for work, dispatch it as described above.\n" +
            "- When work completes, reiterate what was done to remind the operator of what you were asked to do. Be specific about what finished and what is remaining.\n";

        /// <summary>
        /// The Ask Armada system prompt before the completion-recap guidance was added. Used to upgrade an untouched
        /// built-in template in place.
        /// </summary>
        private const string _AskSystemDispatchDefault =
            "You are an AI captain in Armada's \"Ask Armada\" chat.\n" +
            "\n" +
            "## What you can do\n" +
            "- Only use tools that are actually provided to you in this session. Never claim to have tools, MCP access, or the ability to inspect or change Armada state unless those tools are present and you can call them.\n" +
            "- When tools ARE available, use them to look up live state (for example status and enumerate) or to take an action the operator requested, rather than guessing or describing what you would do.\n" +
            "- Questions are usually about Armada operations -- fleets, vessels, captains, missions, voyages, docks, and the merge queue -- unless the operator clearly means something else.\n" +
            "\n" +
            "## When you cannot do something\n" +
            "- If the operator asks you to do or look up something and you have no tool for it, say so in one sentence -- do not ask for irrelevant details or invent a process. For example, if asked to create a vessel, dispatch a mission, or change fleet state and you have no such tool, reply that you cannot do it from this chat and tell them how instead: use a captain connected to Armada over MCP, or the Armada dashboard (Vessels / Dispatch) or the armada CLI.\n" +
            "- Never fabricate ids, results, fields, or capabilities. If you are unsure or lack the context, say so plainly.\n" +
            "\n" +
            "## Conversation scope\n" +
            "- Keep track of what this conversation is about: the vessels, fleets, voyages, and missions the operator named, and any work started here (listed under \"Conversation focus\" when present).\n" +
            "- Follow-up questions without an explicit target (\"what's running?\", \"any failures?\", \"how is it going?\", \"is it done?\") refer to that focus. Scope your lookups to it -- pass the vessel, fleet, or voyage filters to enumerate, or use voyage_status / mission_status -- and answer for those items, not for the whole fleet.\n" +
            "- Answer fleet-wide when the operator asks about everything (\"across the fleet\", \"all vessels\", \"overall\", \"anything else\"), names a different target, or the conversation has no focus yet.\n" +
            "- Start a scoped answer by naming the scope in a few words (for example \"On TUIKit: ...\"). If something notable is happening outside the focus (for example a failure on another vessel), you may add one short line about it.\n" +
            "- If the focus is ambiguous (several vessels were discussed), use the most recently discussed one and say so, or ask a one-line question.\n" +
            "- \"Failures\" include missions in Failed and LandingFailed status and failed merge-queue entries; check all of them before saying nothing failed.\n" +
            "\n" +
            "## Making decisions\n" +
            "- When a request has a sensible default or a low-stakes choice, decide yourself, say what you chose in one line, and carry on. Examples: the target framework for a multi-targeted .NET project (the newest one the project targets whose SDK is installed; `dotnet --list-sdks` shows them), the test project or solution when one clearly matches, and ordinary build and test flags.\n" +
            "- When you dispatch work, write those choices into the mission description so the captain doing the work does not have to ask either.\n" +
            "- Ask the operator only when the choice is costly or hard to undo, changes shared state (pushing, merging, deleting, deploying), or the request could mean materially different things. Then ask one short question and offer your recommended answer.\n" +
            "\n" +
            "## Prefer dispatching work\n" +
            "- Strongly favor creating and dispatching missions over doing the work yourself. When the operator asks for a change, fix, feature, test, refactor, investigation, or anything that edits a repository, dispatch it as a mission (or a voyage of missions) on the right vessel with the dispatch or create_mission tool, rather than editing files, running builds, or committing and pushing from this chat.\n" +
            "- Missions run in isolated docks, go through the vessel's pipeline and merge queue, and are tracked and reviewable; work done directly in this chat skips all of that and can collide with other work in progress.\n" +
            "- Write a self-contained mission description: the goal, the files or areas involved, the choices you made, and how to verify the result. Split large requests into several focused missions.\n" +
            "- Do the work directly only for read-only lookups (status, diffs, logs, reading files to answer a question), when the operator explicitly asks you to do it yourself rather than dispatch it, or when no dispatch tool is available -- and then say that you are doing it directly.\n" +
            "\n" +
            "## Style\n" +
            "- Prefer short, direct answers. Use lists and code blocks only where they genuinely help.\n" +
            "- This is a conversational chat, not a mission: do not modify files or run destructive commands yourself. When the operator asks for work, dispatch it as described above.\n";


        /// <summary>
        /// The Ask Armada system prompt before the dispatch-first guidance was added. Used to upgrade an untouched
        /// built-in template in place.
        /// </summary>
        private const string _AskSystemDecisionsDefault =
            "You are an AI captain in Armada's \"Ask Armada\" chat.\n" +
            "\n" +
            "## What you can do\n" +
            "- Only use tools that are actually provided to you in this session. Never claim to have tools, MCP access, or the ability to inspect or change Armada state unless those tools are present and you can call them.\n" +
            "- When tools ARE available, use them to look up live state (for example status and enumerate) or to take an action the operator requested, rather than guessing or describing what you would do.\n" +
            "- Questions are usually about Armada operations -- fleets, vessels, captains, missions, voyages, docks, and the merge queue -- unless the operator clearly means something else.\n" +
            "\n" +
            "## When you cannot do something\n" +
            "- If the operator asks you to do or look up something and you have no tool for it, say so in one sentence -- do not ask for irrelevant details or invent a process. For example, if asked to create a vessel, dispatch a mission, or change fleet state and you have no such tool, reply that you cannot do it from this chat and tell them how instead: use a captain connected to Armada over MCP, or the Armada dashboard (Vessels / Dispatch) or the armada CLI.\n" +
            "- Never fabricate ids, results, fields, or capabilities. If you are unsure or lack the context, say so plainly.\n" +
            "\n" +
            "## Conversation scope\n" +
            "- Keep track of what this conversation is about: the vessels, fleets, voyages, and missions the operator named, and any work started here (listed under \"Conversation focus\" when present).\n" +
            "- Follow-up questions without an explicit target (\"what's running?\", \"any failures?\", \"how is it going?\", \"is it done?\") refer to that focus. Scope your lookups to it -- pass the vessel, fleet, or voyage filters to enumerate, or use voyage_status / mission_status -- and answer for those items, not for the whole fleet.\n" +
            "- Answer fleet-wide when the operator asks about everything (\"across the fleet\", \"all vessels\", \"overall\", \"anything else\"), names a different target, or the conversation has no focus yet.\n" +
            "- Start a scoped answer by naming the scope in a few words (for example \"On TUIKit: ...\"). If something notable is happening outside the focus (for example a failure on another vessel), you may add one short line about it.\n" +
            "- If the focus is ambiguous (several vessels were discussed), use the most recently discussed one and say so, or ask a one-line question.\n" +
            "- \"Failures\" include missions in Failed and LandingFailed status and failed merge-queue entries; check all of them before saying nothing failed.\n" +
            "\n" +
            "## Making decisions\n" +
            "- When a request has a sensible default or a low-stakes choice, decide yourself, say what you chose in one line, and carry on. Examples: the target framework for a multi-targeted .NET project (the newest one the project targets whose SDK is installed; `dotnet --list-sdks` shows them), the test project or solution when one clearly matches, and ordinary build and test flags.\n" +
            "- When you dispatch work, write those choices into the mission description so the captain doing the work does not have to ask either.\n" +
            "- Ask the operator only when the choice is costly or hard to undo, changes shared state (pushing, merging, deleting, deploying), or the request could mean materially different things. Then ask one short question and offer your recommended answer.\n" +
            "\n" +
            "## Style\n" +
            "- Prefer short, direct answers. Use lists and code blocks only where they genuinely help.\n" +
            "- This is a conversational chat, not a mission: do not modify files, run destructive commands, or dispatch work unless the operator explicitly asks you to.\n";

        /// <summary>
        /// The Ask Armada system prompt before the decision-making guidance was added. Used to upgrade an untouched
        /// built-in template in place.
        /// </summary>
        private const string _AskSystemScopeDefault =
            "You are an AI captain in Armada's \"Ask Armada\" chat.\n" +
            "\n" +
            "## What you can do\n" +
            "- Only use tools that are actually provided to you in this session. Never claim to have tools, MCP access, or the ability to inspect or change Armada state unless those tools are present and you can call them.\n" +
            "- When tools ARE available, use them to look up live state (for example status and enumerate) or to take an action the operator requested, rather than guessing or describing what you would do.\n" +
            "- Questions are usually about Armada operations -- fleets, vessels, captains, missions, voyages, docks, and the merge queue -- unless the operator clearly means something else.\n" +
            "\n" +
            "## When you cannot do something\n" +
            "- If the operator asks you to do or look up something and you have no tool for it, say so in one sentence -- do not ask for irrelevant details or invent a process. For example, if asked to create a vessel, dispatch a mission, or change fleet state and you have no such tool, reply that you cannot do it from this chat and tell them how instead: use a captain connected to Armada over MCP, or the Armada dashboard (Vessels / Dispatch) or the armada CLI.\n" +
            "- Never fabricate ids, results, fields, or capabilities. If you are unsure or lack the context, say so plainly.\n" +
            "\n" +
            "## Conversation scope\n" +
            "- Keep track of what this conversation is about: the vessels, fleets, voyages, and missions the operator named, and any work started here (listed under \"Conversation focus\" when present).\n" +
            "- Follow-up questions without an explicit target (\"what's running?\", \"any failures?\", \"how is it going?\", \"is it done?\") refer to that focus. Scope your lookups to it -- pass the vessel, fleet, or voyage filters to enumerate, or use voyage_status / mission_status -- and answer for those items, not for the whole fleet.\n" +
            "- Answer fleet-wide when the operator asks about everything (\"across the fleet\", \"all vessels\", \"overall\", \"anything else\"), names a different target, or the conversation has no focus yet.\n" +
            "- Start a scoped answer by naming the scope in a few words (for example \"On TUIKit: ...\"). If something notable is happening outside the focus (for example a failure on another vessel), you may add one short line about it.\n" +
            "- If the focus is ambiguous (several vessels were discussed), use the most recently discussed one and say so, or ask a one-line question.\n" +
            "- \"Failures\" include missions in Failed and LandingFailed status and failed merge-queue entries; check all of them before saying nothing failed.\n" +
            "\n" +
            "## Style\n" +
            "- Prefer short, direct answers. Use lists and code blocks only where they genuinely help.\n" +
            "- This is a conversational chat, not a mission: do not modify files, run destructive commands, or dispatch work unless the operator explicitly asks you to.\n";

        /// <summary>
        /// The Ask Armada system prompt before the conversation-scope guidance was added. Used to upgrade an
        /// untouched built-in template in place.
        /// </summary>
        private const string _AskSystemPreviousDefault =
            "You are an AI captain in Armada's \"Ask Armada\" chat.\n" +
            "\n" +
            "## What you can do\n" +
            "- Only use tools that are actually provided to you in this session. Never claim to have tools, MCP access, or the ability to inspect or change Armada state unless those tools are present and you can call them.\n" +
            "- When tools ARE available, use them to look up live state (for example status and enumerate) or to take an action the operator requested, rather than guessing or describing what you would do.\n" +
            "- Questions are usually about Armada operations -- fleets, vessels, captains, missions, voyages, docks, and the merge queue -- unless the operator clearly means something else.\n" +
            "\n" +
            "## When you cannot do something\n" +
            "- If the operator asks you to do or look up something and you have no tool for it, say so in one sentence -- do not ask for irrelevant details or invent a process. For example, if asked to create a vessel, dispatch a mission, or change fleet state and you have no such tool, reply that you cannot do it from this chat and tell them how instead: use a captain connected to Armada over MCP, or the Armada dashboard (Vessels / Dispatch) or the armada CLI.\n" +
            "- Never fabricate ids, results, fields, or capabilities. If you are unsure or lack the context, say so plainly.\n" +
            "\n" +
            "## Style\n" +
            "- Prefer short, direct answers. Use lists and code blocks only where they genuinely help.\n" +
            "- This is a conversational chat, not a mission: do not modify files, run destructive commands, or dispatch work unless the operator explicitly asks you to.\n";

        /// <summary>
        /// The original seeded Ask Armada system prompt. Used to detect an untouched built-in template so it
        /// can be upgraded in place without clobbering an operator's edits.
        /// </summary>
        private const string _AskSystemLegacyDefault =
            "You are an AI captain answering questions inside Armada's \"Ask Armada\" chat.\n" +
            "\n" +
            "- Assume questions are in general being asked about Armada MCP operations (fleets, vessels, captains, missions, voyages, docks, and the merge queue) unless the operator clearly indicates otherwise.\n" +
            "- Answer the operator's questions about the fleet, missions, voyages, captains, docks, and repositories clearly and concisely.\n" +
            "- When Armada MCP tools are available to you, use them to look up live state (for example status and enumerate) before answering rather than guessing.\n" +
            "- Prefer short, direct answers. Use lists and code blocks where they genuinely help.\n" +
            "- If you are unsure, or you lack the tools or context to answer accurately, say so plainly instead of inventing details.\n" +
            "- This is a conversational chat, not a mission: do not modify files, run destructive commands, or dispatch work unless the operator explicitly asks you to.\n";

        /// <summary>
        /// Heading marking the memory-recall section, used to detect whether a template already carries it.
        /// </summary>
        private const string _MemoryRecallMarker = "## Recall Existing Memory";

        /// <summary>
        /// Guidance appended to every working persona template so agents recall the vessel's durable memory
        /// before acting. The Recorder writes memory; everyone else should read it.
        /// </summary>
        private const string _MemoryRecallGuidance =
            "\n" +
            "## Recall Existing Memory\n" +
            "Before you start, recall what is already known about this vessel. Read the vessel model context " +
            "provided in this prompt, and use the `search_memory` MCP tool (when available) to find durable " +
            "memories relevant to this mission -- filter by this vessel and by keywords or topics from the " +
            "mission description, and skim episodic, semantic, and procedural memories alike. Reuse the " +
            "conventions, decisions, and procedures already recorded instead of re-deriving them; call out " +
            "anything you find that conflicts with what you now observe. If the memory tools are not available, " +
            "rely on the vessel model context.\n";

        /// <summary>
        /// Default content of the <c>commit.instructions_preamble</c> template: what every commit message a captain
        /// writes must contain. Injected into every mission prompt, whether or not commit trailers are enabled.
        /// </summary>
        public static readonly string CommitInstructionsPreambleDefault =
            "IMPORTANT: Every git commit you create MUST have a clear, descriptive commit message. The message MUST include: " +
            "(1) a concise summary line stating what the commit does, and (2) a full manifest and description of what was " +
            "changed -- list every file added, modified, or deleted and, for each, explain what changed and why.";

        /// <summary>
        /// Default content of the <c>commit.trailers_preamble</c> template: the sentence that introduces the Armada
        /// commit trailers when commit metadata is enabled.
        /// </summary>
        public static readonly string CommitTrailersPreambleDefault =
            "After that description, append the following trailers at the end of the commit message (after a blank line):";

        private static readonly string _CommitInstructionsPreambleLegacyDefault =
            "IMPORTANT: Every git commit you create MUST have a clear, descriptive commit message. The message MUST include: (1) a concise summary line stating what the commit does, and (2) a full manifest and description of what was changed -- list every file added, modified, or deleted and, for each, explain what changed and why. After that description, append the following trailers at the end of the commit message (after a blank line):";

        /// <summary>
        /// Seed all built-in templates into the database if they don't already exist.
        /// Called on startup.
        /// </summary>
        public async Task SeedDefaultsAsync(CancellationToken token = default)
        {
            foreach (KeyValuePair<string, EmbeddedTemplate> kvp in _EmbeddedDefaults)
            {
                string name = kvp.Key;
                EmbeddedTemplate embedded = kvp.Value;

                bool exists = await _Database.PromptTemplates.ExistsByNameAsync(name, token).ConfigureAwait(false);
                if (!exists)
                {
                    // Every working persona (everyone except the Recorder itself) is reminded to recall the
                    // vessel's durable memory before acting, so recorded knowledge is actually reused.
                    string content = embedded.Content;
                    if (String.Equals(embedded.Category, "persona", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(name, "persona.recorder", StringComparison.OrdinalIgnoreCase)
                        && !content.Contains(_MemoryRecallMarker, StringComparison.Ordinal))
                    {
                        content += _MemoryRecallGuidance;
                    }

                    PromptTemplate template = new PromptTemplate(name, content)
                    {
                        Description = embedded.Description,
                        Category = embedded.Category,
                        IsBuiltIn = true
                    };

                    await _Database.PromptTemplates.CreateAsync(template, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "seeded built-in template '" + name + "'");
                }
            }

            await UpgradeLegacyPersonaTemplateReferencesAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInPersonaExecutionGuidanceAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInPersonaMemoryRecallAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInAskSystemAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInCommitPreambleAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInMissionRulesAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInContextCompletionAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInProgressSignalsAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInWorkerEnvelopeAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInMergeConflictAvoidanceAsync(token).ConfigureAwait(false);
            await UpgradeBuiltInPersonaVariantsAsync(token).ConfigureAwait(false);
        }

        private async Task UpgradeBuiltInPersonaVariantsAsync(CancellationToken token)
        {
            // Lean and Compact variants are stored like any built-in template; an untouched copy of an earlier variant
            // upgrades to the current one, an edited copy is kept.
            foreach (string personaTemplateName in PersonaPromptVariants.PersonaTemplateNames)
            {
                foreach (PromptVariantEnum variant in new[] { PromptVariantEnum.Lean, PromptVariantEnum.Compact })
                {
                    token.ThrowIfCancellationRequested();
                    IReadOnlyList<string> previous = PersonaPromptVariants.GetPreviousContents(personaTemplateName, variant);
                    string? current = PersonaPromptVariants.GetContent(personaTemplateName, variant);
                    if (previous.Count == 0 || current == null) continue;
                    string name = MissionPromptBuilder.GetPersonaVariantTemplateName(personaTemplateName, variant);
                    PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync(name, token).ConfigureAwait(false);
                    if (existing == null || !existing.IsBuiltIn) continue;
                    string stored = NormalizeLineEndings(existing.Content);
                    if (!previous.Any(p => NormalizeLineEndings(p) == stored)) continue;
                    existing.Content = current;
                    existing.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "upgraded built-in variant template '" + name + "'");
                }
            }
        }

        private async Task UpgradeBuiltInMergeConflictAvoidanceAsync(CancellationToken token)
        {
            // v1.3.0 names owned and concurrently owned paths; an untouched built-in copy upgrades, an edited one is kept.
            const string name = "mission.merge_conflict_avoidance";
            PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (existing == null || !existing.IsBuiltIn) return;
            if (NormalizeLineEndings(existing.Content) != NormalizeLineEndings(MergeConflictPrompt.LegacyDefault)) return;
            existing.Content = MergeConflictPrompt.Default;
            existing.LastUpdateUtc = DateTime.UtcNow;
            await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
            _Logging.Debug(_Header + "upgraded built-in mission.merge_conflict_avoidance with owned and concurrent paths");
        }

        // The persona default built from a brief: the architect's repeated index rule is removed to stay within the
        // persona budget, and the shared long-command reminder is appended.
        private static string BriefContent(string brief)
            => brief.Replace("Use decisionIndexes for the 1-based indexes of only this slice's relevant decisions ([] if none).", String.Empty)
                + "\nPrint progress before long commands; skip the full test suite when Armada runs post-stage checks.\n";

        // What a seeded install stored for a brief, so an untouched copy of an earlier brief is recognized.
        internal static string ShippedPersonaContent(string name, string brief)
        {
            Dictionary<string, EmbeddedTemplate> one = new Dictionary<string, EmbeddedTemplate>
            {
                [name] = new EmbeddedTemplate { Name = name, Content = BriefContent(brief) }
            };
            AddWorkerEnvelopePlaceholder(one);
            return one[name].Content;
        }

        private static void AddWorkerEnvelopePlaceholder(Dictionary<string, EmbeddedTemplate> defaults)
        {
            // The Architect and Judge get the Worker eligibility envelope (docs/MODEL_HETEROGENEITY.md, 3.3). The value is
            // empty when no envelope is set, so the rendered prompt is unchanged. The Lean and Compact variants carry the
            // placeholder in PersonaPromptVariants.
            foreach (string name in new[] { "persona.architect", "persona.judge" })
            {
                if (!defaults.TryGetValue(name, out EmbeddedTemplate? embedded) || embedded.Content.Contains(WorkerEnvelopePlaceholder, StringComparison.Ordinal)) continue;
                const string judgeEvidence = "\n\n## Prior stage evidence";
                if (embedded.Content.Contains(judgeEvidence, StringComparison.Ordinal))
                    embedded.Content = embedded.Content.Replace(judgeEvidence, WorkerEnvelopePlaceholder + judgeEvidence);
                else
                    embedded.Content = embedded.Content.TrimEnd('\n') + WorkerEnvelopePlaceholder + "\n";
            }
        }

        private async Task UpgradeBuiltInWorkerEnvelopeAsync(CancellationToken token)
        {
            // An untouched built-in copy picks up the {WorkerEnvelope} placeholder; an edited one is kept as is.
            foreach (KeyValuePair<string, EmbeddedTemplate> entry in _EmbeddedDefaults)
            {
                token.ThrowIfCancellationRequested();
                string current = entry.Value.Content;
                if (!current.Contains(WorkerEnvelopePlaceholder, StringComparison.Ordinal)) continue;
                PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync(entry.Key, token).ConfigureAwait(false);
                if (existing == null || !existing.IsBuiltIn) continue;
                string previous = NormalizeLineEndings(current.Replace(WorkerEnvelopePlaceholder, String.Empty));
                string stored = NormalizeLineEndings(existing.Content);
                if (stored != previous) continue;
                existing.Content = current;
                existing.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "added the Worker envelope placeholder to built-in template '" + entry.Key + "'");
            }
        }

        private async Task UpgradeBuiltInProgressSignalsAsync(CancellationToken token)
        {
            // Earlier defaults listed the result and verdict lines (now only in StageOutputContract), and before that had
            // no blocked outcome line. An untouched built-in copy of either upgrades; an edited one is kept as is.
            const string name = "mission.progress_signals";
            string current = _EmbeddedDefaults[name].Content;
            PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (existing == null || !existing.IsBuiltIn) return;
            string withContract = PersonaPromptImprovements.PreviousTemplates[name + ".v1"];
            List<string> previous = new List<string> { withContract, withContract.Replace(CaptainBlockedContract.SignalLine, String.Empty) };
            string stored = NormalizeLineEndings(existing.Content);
            if (!previous.Any(p => NormalizeLineEndings(p) == stored)) return;
            existing.Content = current;
            existing.LastUpdateUtc = DateTime.UtcNow;
            await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
            _Logging.Debug(_Header + "upgraded built-in mission.progress_signals to the current default");
        }

        private async Task UpgradeBuiltInContextCompletionAsync(CancellationToken token)
        {
            const string name = "mission.context_conservation";
            string current = _EmbeddedDefaults[name].Content;
            const string oldRule = "5. **If the mission scope feels too large** (more than 8 files, or files with 500+ lines to read), commit what you have, report progress, and exit with code 0. Partial progress is better than crashing.\n";
            const string newRule = "5. **Complete the assigned scope.** Work incrementally and validate relevant changes. If a real blocker prevents completion, preserve your work and report the blocker explicitly. Never emit a success marker or claim completion for partial work.\n";
            PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (existing == null || !existing.IsBuiltIn || NormalizeLineEndings(existing.Content) != NormalizeLineEndings(current.Replace(newRule, oldRule))) return;
            existing.Content = current;
            existing.LastUpdateUtc = DateTime.UtcNow;
            await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        private async Task UpgradeBuiltInPersonaExecutionGuidanceAsync(CancellationToken token)
        {
            foreach (KeyValuePair<string, string> previous in _PreviousPersonaContents)
            {
                token.ThrowIfCancellationRequested();
                PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync(previous.Key, token).ConfigureAwait(false);
                if (existing == null || !existing.IsBuiltIn) continue;
                string recall = previous.Key == "persona.recorder" ? String.Empty : _MemoryRecallGuidance;
                string improved = PersonaPromptImprovements.Apply(previous.Key, previous.Value);
                string recallBeforeImprovement = PersonaPromptImprovements.Apply(previous.Key, previous.Value + recall);
                List<string> shippedVersions = new List<string> { previous.Value, previous.Value + recall, improved, improved + recall, recallBeforeImprovement };
                foreach (IReadOnlyDictionary<string, string> snapshot in new[] { PersonaPromptImprovements.PreviousBriefs, PersonaPromptImprovements.RecentBriefs, PersonaPromptImprovements.EvidenceBriefs, PersonaPromptImprovements.ScopedBriefs, PersonaPromptImprovements.DecisionBriefs, PersonaPromptImprovements.ConcurrencyBriefs, PersonaPromptImprovements.ContractBriefs, PersonaPromptImprovements.Briefs })
                {
                    if (!snapshot.TryGetValue(previous.Key, out string? brief)) continue;
                    // Both the brief as written and the content a seeded install stored from it count as untouched.
                    shippedVersions.Add(brief);
                    shippedVersions.Add(ShippedPersonaContent(previous.Key, brief));
                }
                string stored = NormalizeLineEndings(existing.Content);
                if (stored == NormalizeLineEndings(_EmbeddedDefaults[previous.Key].Content)) continue;
                if (!shippedVersions.Any(shipped => stored == NormalizeLineEndings(shipped))) continue;
                existing.Content = _EmbeddedDefaults[previous.Key].Content;
                existing.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "upgraded untouched built-in persona execution guidance for '" + previous.Key + "'");
            }
        }

        /// <summary>
        /// Replace an untouched built-in mission.rules row when its content is exactly a previously shipped default.
        /// An operator who has edited the template keeps that content.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        private async Task UpgradeBuiltInMissionRulesAsync(CancellationToken token)
        {
            PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync("mission.rules", token).ConfigureAwait(false);
            if (existing == null || !existing.IsBuiltIn) return;
            if (!String.Equals(existing.Content, _MissionRulesLegacyDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _MissionRulesUnconditionalPushDefault, StringComparison.Ordinal))
            {
                return;
            }

            existing.Content = MissionRulesDefault;
            existing.LastUpdateUtc = DateTime.UtcNow;
            await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
            _Logging.Debug(_Header + "upgraded built-in mission.rules to the current default");
        }

        /// <summary>
        /// Upgrade the built-in commit instructions preamble in place when it still holds the original seeded content,
        /// which ended by introducing the commit trailers. The trailer sentence now lives in
        /// <c>commit.trailers_preamble</c> so the commit message guidance applies even when trailers are disabled. An
        /// operator who has edited the template keeps their version untouched.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        private async Task UpgradeBuiltInCommitPreambleAsync(CancellationToken token)
        {
            PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync("commit.instructions_preamble", token).ConfigureAwait(false);
            if (existing == null || !existing.IsBuiltIn) return;
            if (!String.Equals(existing.Content, _CommitInstructionsPreambleLegacyDefault, StringComparison.Ordinal)) return;

            existing.Content = CommitInstructionsPreambleDefault;
            existing.Description = "Commit message instructions injected into every mission prompt: what a commit message must contain.";
            existing.LastUpdateUtc = DateTime.UtcNow;
            await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
            _Logging.Debug(_Header + "upgraded built-in commit.instructions_preamble to the current default");
        }

        /// <summary>
        /// Upgrade the built-in Ask Armada system prompt in place when it still holds the original seeded
        /// content, so existing deployments pick up the honesty guidance on the next startup. An operator who
        /// has edited the template keeps their version untouched.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        private async Task UpgradeBuiltInAskSystemAsync(CancellationToken token)
        {
            PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync("ask.system", token).ConfigureAwait(false);
            if (existing == null || !existing.IsBuiltIn) return;
            if (!String.Equals(existing.Content, _AskSystemLegacyDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _AskSystemPreviousDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _AskSystemScopeDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _AskSystemDecisionsDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _AskSystemDispatchDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _AskSystemRecapDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _AskSystemBlackBoxDefault, StringComparison.Ordinal)
                && !String.Equals(existing.Content, _AskSystemSplitDefault, StringComparison.Ordinal)) return;

            existing.Content = _AskSystemDefault;
            existing.LastUpdateUtc = DateTime.UtcNow;
            await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
            _Logging.Debug(_Header + "upgraded built-in ask.system prompt to the current default");
        }

        /// <summary>
        /// Bring existing built-in persona templates up to date with the memory-recall guidance. Seeding is
        /// existence-guarded, so a deployment created before the guidance was introduced would otherwise never
        /// receive it. This appends the guidance (idempotently) to every built-in persona template that lacks it
        /// and still holds the content Armada shipped, except the Recorder's own template. Templates an operator
        /// edited are left exactly as they are (resetting one to its default picks up the guidance).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        private async Task UpgradeBuiltInPersonaMemoryRecallAsync(CancellationToken token)
        {
            List<PromptTemplate> all = await _Database.PromptTemplates.EnumerateAsync(token).ConfigureAwait(false);
            foreach (PromptTemplate template in all)
            {
                if (!template.IsBuiltIn) continue;
                if (!String.Equals(template.Category, "persona", StringComparison.OrdinalIgnoreCase)) continue;
                if (String.Equals(template.Name, "persona.recorder", StringComparison.OrdinalIgnoreCase)) continue;
                if (!String.IsNullOrEmpty(template.Content) && template.Content.Contains(_MemoryRecallMarker, StringComparison.Ordinal)) continue;

                // Only upgrade content that is still exactly what Armada shipped. An operator who edited the template
                // keeps their version untouched (they can reset it to pick up the guidance).
                string? shipped = GetEmbeddedDefault(template.Name);
                if (shipped == null || !String.Equals(NormalizeLineEndings(template.Content), NormalizeLineEndings(shipped), StringComparison.Ordinal))
                {
                    _Logging.Debug(_Header + "kept edited built-in template '" + template.Name + "' as is (memory-recall guidance not appended)");
                    continue;
                }

                template.Content = (template.Content ?? String.Empty) + _MemoryRecallGuidance;
                template.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PromptTemplates.UpdateAsync(template, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "appended memory-recall guidance to built-in template '" + template.Name + "'");
            }
        }

        /// <summary>
        /// List all templates, optionally filtered by category.
        /// </summary>
        public async Task<List<PromptTemplate>> ListAsync(string? category = null, CancellationToken token = default)
        {
            List<PromptTemplate> templates = await _Database.PromptTemplates.EnumerateAsync(token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(category))
            {
                templates = templates.Where(t => String.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return templates;
        }

        /// <summary>
        /// Reset a template to its embedded resource default content.
        /// </summary>
        public async Task<PromptTemplate?> ResetToDefaultAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            if (!_EmbeddedDefaults.TryGetValue(name, out EmbeddedTemplate? embedded))
            {
                return null;
            }

            PromptTemplate? existing = await _Database.PromptTemplates.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (existing != null)
            {
                existing.Content = embedded.Content;
                existing.Description = embedded.Description;
                existing.Category = embedded.Category;
                existing.LastUpdateUtc = DateTime.UtcNow;

                PromptTemplate updated = await _Database.PromptTemplates.UpdateAsync(existing, token).ConfigureAwait(false);
                _Logging.Info(_Header + "reset template '" + name + "' to embedded default");
                return updated;
            }
            else
            {
                PromptTemplate template = new PromptTemplate(name, embedded.Content)
                {
                    Description = embedded.Description,
                    Category = embedded.Category,
                    IsBuiltIn = true
                };

                PromptTemplate created = await _Database.PromptTemplates.CreateAsync(template, token).ConfigureAwait(false);
                _Logging.Info(_Header + "created template '" + name + "' from embedded default");
                return created;
            }
        }

        /// <summary>
        /// Get the embedded resource default content for a template by name.
        /// Returns null if no embedded default exists.
        /// </summary>
        public string? GetEmbeddedDefault(string name)
        {
            if (String.IsNullOrEmpty(name)) return null;

            if (_EmbeddedDefaults.TryGetValue(name, out EmbeddedTemplate? embedded))
            {
                return embedded.Content;
            }

            return null;
        }

        /// <summary>
        /// Names of every built-in template this build ships (the templates <see cref="SeedDefaultsAsync"/> ensures
        /// exist), sorted ordinally.
        /// </summary>
        /// <returns>Built-in template names.</returns>
        public List<string> GetEmbeddedDefaultNames()
        {
            List<string> names = _EmbeddedDefaults.Keys.ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        #endregion

        #region Private-Methods

        private async Task UpgradeLegacyPersonaTemplateReferencesAsync(CancellationToken token)
        {
            List<PromptTemplate> templates = await _Database.PromptTemplates.EnumerateAsync(token).ConfigureAwait(false);
            foreach (PromptTemplate template in templates)
            {
                if (!template.IsBuiltIn) continue;

                string? updatedDescription = PersonaCatalog.ReplaceLegacyTestEngineer(template.Description);
                string updatedContent = PersonaCatalog.ReplaceLegacyTestEngineer(template.Content) ?? template.Content;
                bool changed =
                    !String.Equals(updatedDescription, template.Description, StringComparison.Ordinal) ||
                    !String.Equals(updatedContent, template.Content, StringComparison.Ordinal);

                if (!changed) continue;

                template.Description = updatedDescription;
                template.Content = updatedContent;
                template.LastUpdateUtc = DateTime.UtcNow;
                await _Database.PromptTemplates.UpdateAsync(template, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "updated built-in template references: '" + template.Name + "'");
            }
        }

        private static string NormalizeLineEndings(string? value)
        {
            return (value ?? String.Empty).Replace("\r\n", "\n").TrimEnd();
        }

        private Dictionary<string, EmbeddedTemplate> BuildEmbeddedDefaults()
        {
            Dictionary<string, EmbeddedTemplate> defaults = new Dictionary<string, EmbeddedTemplate>();

            defaults["ask.system"] = new EmbeddedTemplate
            {
                Name = "ask.system",
                Description = "System prompt prepended to every Ask Armada dashboard chat turn.",
                Category = "ask",
                Content = _AskSystemDefault
            };

            defaults["vessel.build_context"] = new EmbeddedTemplate
            {
                Name = "vessel.build_context",
                Description = "Prompt used by the Vessel \"Build Context\" action to generate or refine a vessel's Model Context.",
                Category = "vessel",
                Content =
                    "You are analyzing a git repository to build its \"Model Context\": a concise, durable reference\n" +
                    "document that future AI coding agents (captains) read before working in this repository.\n" +
                    "\n" +
                    "Inspect the repository in your current working directory -- read files as needed -- and produce a\n" +
                    "Model Context document that covers:\n" +
                    "- What the project is and its purpose.\n" +
                    "- Architecture and how the code is organized (key directories, entry points, and important files).\n" +
                    "- Build, test, and run commands.\n" +
                    "- Key dependencies, frameworks, languages, and external services.\n" +
                    "- Coding conventions, patterns, and constraints an agent should follow.\n" +
                    "- Anything non-obvious an agent must know to work here safely and effectively.\n" +
                    "\n" +
                    "Rules:\n" +
                    "- This is a READ-ONLY analysis. Do NOT modify, create, or delete any files, and do not run\n" +
                    "  destructive commands.\n" +
                    "- Be concise and factual. Prefer short sections and bullet points over prose.\n" +
                    "- Output ONLY the Model Context document itself -- no preamble, no sign-off, and do not wrap the\n" +
                    "  whole document in a code fence.\n"
            };

            defaults["mission.rules"] = new EmbeddedTemplate
            {
                Name = "mission.rules",
                Description = "Standard rules injected into every mission prompt.",
                Category = "mission",
                Content = MissionRulesDefault
            };

            defaults["mission.context_conservation"] = new EmbeddedTemplate
            {
                Name = "mission.context_conservation",
                Description = "Context conservation guidelines to prevent agents from exceeding their context window.",
                Category = "mission",
                Content =
                    "## Context Conservation (CRITICAL)\n" +
                    "\n" +
                    "You have a limited context window. Exceeding it will crash your process and fail the mission. " +
                    "Follow these rules to stay within limits:\n" +
                    "\n" +
                    "1. **NEVER read entire large files.** If a file is over 200 lines, read only the specific " +
                    "section you need using line offsets. Use grep/search to find the right section first.\n" +
                    "\n" +
                    "2. **Read before you write, but read surgically.** Read only the 10-30 lines around the code " +
                    "you need to change, not the whole file.\n" +
                    "\n" +
                    "3. **Do not explore the codebase broadly.** Only read files explicitly mentioned in your " +
                    "mission description. If the mission says to edit README.md, read only the section you need " +
                    "to edit, not the entire README.\n" +
                    "\n" +
                    "4. **Make your changes and finish.** Do not re-read files to verify your changes, do not " +
                    "read files for 'context' that isn't directly needed for your edit, and do not explore related " +
                    "files out of curiosity.\n" +
                    "\n" +
                    "5. **Complete the assigned scope.** Work incrementally and validate relevant changes. If a real " +
                    "blocker prevents completion, preserve your work and report the blocker explicitly. Never " +
                    "emit a success marker or claim completion for partial work.\n"
            };

            defaults["mission.merge_conflict_avoidance"] = new EmbeddedTemplate
            {
                Name = "mission.merge_conflict_avoidance",
                Description = "Rules for avoiding merge conflicts when multiple captains work in parallel: owned paths, paths owned by concurrent missions and the parent's result.",
                Category = "mission",
                Content = MergeConflictPrompt.Default
            };

            defaults["mission.progress_signals"] = new EmbeddedTemplate
            {
                Name = "mission.progress_signals",
                Description = "Instructions for reporting progress signals back to the Admiral.",
                Category = "mission",
                Content =
                    "## Runtime Signals\n" +
                    "If you emit Armada signals, print each signal on its own standalone line with no bullets, quoting, or extra Markdown:\n" +
                    "- `[ARMADA:PROGRESS] 50` -- report completion percentage (0-100)\n" +
                    "- `[ARMADA:STATUS] Testing` -- report that you are now running tests (informational; `[ARMADA:STATUS] InProgress` switches back)\n" +
                    "- `[ARMADA:MESSAGE] your message here` -- send a progress message\n" +
                    "- `[ARMADA:TOKENS] input=1234 output=567 cached=0` -- report the tokens you consumed this session (input/prompt, output/completion, and cache-read). Emit this once near the end if your runtime can determine the counts; it lets the Admiral record real token usage instead of an estimate.\n" +
                    CaptainBlockedContract.SignalLine
            };

            defaults["mission.model_context_updates"] = new EmbeddedTemplate
            {
                Name = "mission.model_context_updates",
                Description = "Instructions for updating model context when enabled on the vessel.",
                Category = "mission",
                Content =
                    "## Model Context Updates\n" +
                    "\n" +
                    "Model context accumulation is enabled for this vessel. Before you finish your mission, " +
                    "review the existing model context above (if any) and consider whether you have discovered " +
                    "key information that would help future agents work on this repository more effectively. " +
                    "Examples include: architectural insights, code style conventions, naming conventions, " +
                    "logging patterns, error handling patterns, testing patterns, build quirks, common pitfalls, " +
                    "important dependencies, interdependencies between modules, concurrency patterns, " +
                    "and performance considerations.\n" +
                    "\n" +
                    "If you have useful additions, call `update_vessel_context` with the `modelContext` " +
                    "parameter set to the COMPLETE updated model context (not just your additions -- include " +
                    "the existing content with your additions merged in). Be thorough -- this context is a " +
                    "goldmine for future agents. Focus on information that is not obvious from reading the code, " +
                    "and organize it clearly with sections or headings.\n" +
                    "\n" +
                    "If you have nothing to add, skip this step.\n"
            };

            defaults["agent.launch_prompt"] = new EmbeddedTemplate
            {
                Name = "agent.launch_prompt",
                Description = "Default launch prompt sent to the agent when starting a mission.",
                Category = "agent",
                Content = "Mission: {MissionTitle}\n\n{MissionDescription}"
            };

            defaults["commit.instructions_preamble"] = new EmbeddedTemplate
            {
                Name = "commit.instructions_preamble",
                Description = "Commit message instructions injected into every mission prompt: what a commit message must contain.",
                Category = "commit",
                Content = CommitInstructionsPreambleDefault
            };

            defaults["commit.trailers_preamble"] = new EmbeddedTemplate
            {
                Name = "commit.trailers_preamble",
                Description = "Sentence that introduces the Armada commit trailers; used only when commit metadata is enabled.",
                Category = "commit",
                Content = CommitTrailersPreambleDefault
            };

            defaults["persona.worker"] = new EmbeddedTemplate
            {
                Name = "persona.worker",
                Description = "Default worker persona for captains executing missions.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.worker"]
            };

            defaults["persona.architect"] = new EmbeddedTemplate
            {
                Name = "persona.architect",
                Description = "Architect persona for analyzing codebases and decomposing work into missions.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.architect"]
            };

            defaults["persona.product_manager"] = new EmbeddedTemplate
            {
                Name = "persona.product_manager",
                Description = "Product manager persona for turning dispatched work into a coherent product strategy and requirements picture.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.product_manager"]
            };

            defaults["persona.judge"] = new EmbeddedTemplate
            {
                Name = "persona.judge",
                Description = "Judge persona for reviewing mission diffs against requirements.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.judge"]
            };

            defaults["persona.recorder"] = new EmbeddedTemplate
            {
                Name = "persona.recorder",
                Description = "Recorder persona: reviews the voyage conversation and distills durable memories into all available memory facilities.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.recorder"]
            };

            defaults["persona.test_engineer"] = new EmbeddedTemplate
            {
                Name = "persona.test_engineer",
                Description = "Test engineer persona for analyzing diffs and writing test coverage.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.test_engineer"]
            };

            defaults["persona.usability_engineer"] = new EmbeddedTemplate
            {
                Name = "persona.usability_engineer",
                Description = "Usability engineer persona for improving user-facing clarity, completeness, and consistency.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.usability_engineer"]
            };

            defaults["persona.linter"] = new EmbeddedTemplate
            {
                Name = "persona.linter",
                Description = "Linter persona for evaluating changed code and documentation for style and correctness.",
                Category = "persona",
                Content = PersonaPromptImprovements.OriginalDefaults["persona.linter"]
            };

            // Structure/layout templates -- control how sections are framed in the CLAUDE.md
            defaults["mission.captain_instructions_wrapper"] = new EmbeddedTemplate
            {
                Name = "mission.captain_instructions_wrapper",
                Description = "Wrapper for captain system instructions section in CLAUDE.md",
                Category = "structure",
                Content =
                    "## Captain Instructions\n" +
                    "{CaptainInstructions}\n"
            };

            defaults["mission.project_context_wrapper"] = new EmbeddedTemplate
            {
                Name = "mission.project_context_wrapper",
                Description = "Wrapper for vessel project context section in CLAUDE.md",
                Category = "structure",
                Content =
                    "## Project Context\n" +
                    "{ProjectContext}\n"
            };

            defaults["mission.code_style_wrapper"] = new EmbeddedTemplate
            {
                Name = "mission.code_style_wrapper",
                Description = "Wrapper for vessel code style guide section in CLAUDE.md",
                Category = "structure",
                Content =
                    "## Code Style\n" +
                    "{StyleGuide}\n"
            };

            defaults["mission.model_context_wrapper"] = new EmbeddedTemplate
            {
                Name = "mission.model_context_wrapper",
                Description = "Wrapper for agent-accumulated model context section in CLAUDE.md",
                Category = "structure",
                Content =
                    "## Model Context\n" +
                    "The following context was accumulated by AI agents during previous missions on this repository. " +
                    "Use this information to work more effectively.\n" +
                    "\n" +
                    "{ModelContext}\n"
            };

            defaults["mission.playbooks_wrapper"] = new EmbeddedTemplate
            {
                Name = "mission.playbooks_wrapper",
                Description = "Wrapper for selected playbooks injected into mission instructions",
                Category = "structure",
                Content =
                    "## Playbooks\n" +
                    "These playbooks are part of the required instructions for this mission. Read and follow them.\n" +
                    "\n" +
                    "{SelectedPlaybooksMarkdown}\n"
            };

            defaults["mission.metadata"] = new EmbeddedTemplate
            {
                Name = "mission.metadata",
                Description = "Mission metadata layout in CLAUDE.md -- title, ID, voyage, description, repo info",
                Category = "structure",
                Content =
                    "# Mission Instructions\n" +
                    "\n" +
                    "{PersonaPrompt}\n" +
                    "\n" +
                    "## Mission\n" +
                    "- **Title:** {MissionTitle}\n" +
                    "- **ID:** {MissionId}\n" +
                    "- **Voyage:** {VoyageId}\n" +
                    "\n" +
                    "## Description\n" +
                    "{MissionDescription}\n" +
                    "\n" +
                    "## Repository\n" +
                    "- **Name:** {VesselName}\n" +
                    "- **Branch:** {BranchName}\n" +
                    "- **Default Branch:** {DefaultBranch}\n"
            };

            defaults["mission.existing_instructions_wrapper"] = new EmbeddedTemplate
            {
                Name = "mission.existing_instructions_wrapper",
                Description = "Wrapper for existing CLAUDE.md content from the repository",
                Category = "structure",
                Content =
                    "\n## Existing Project Instructions\n" +
                    "\n" +
                    "{ExistingClaudeMd}"
            };

            defaults["landing.pr_body"] = new EmbeddedTemplate
            {
                Name = "landing.pr_body",
                Description = "Pull request body template used when creating PRs for completed missions",
                Category = "landing",
                Content =
                    "## Mission\n" +
                    "**{MissionTitle}**\n" +
                    "\n" +
                    "{MissionDescription}"
            };

            defaults[FleetCategorizationTemplateName] = new EmbeddedTemplate
            {
                Name = FleetCategorizationTemplateName,
                Description = "Default instructions for the captain that recommends fleets for repositories imported in bulk. The Admiral appends the output-format contract, so edits here cannot break parsing.",
                Category = "import",
                Content =
                    "Look at all of the repositories listed in REPOSITORIES.md, figure out what each one does, then group them " +
                    "into a small set of recommended fleets of related repositories.\n" +
                    "\n" +
                    "- Prefer 3-10 fleets with short, descriptive names (for example \"Payments Platform\" or \"Developer Tooling\").\n" +
                    "- Group repositories that are built, deployed, or changed together, or that serve the same product area.\n" +
                    "- Give each fleet a one-sentence description and a short rationale explaining why its repositories belong together.\n" +
                    "- Use REPOSITORIES.md first. Open a repository at its path only when the manifest is not enough to tell what it does.\n" +
                    "- Put every repository in exactly one fleet.\n"
            };

            // Lean and Compact persona prompt variants (selected per captain or per voyage, see PromptVariantResolver).
            foreach (string personaTemplateName in PersonaPromptVariants.PersonaTemplateNames)
            {
                foreach (PromptVariantEnum variant in new[] { PromptVariantEnum.Lean, PromptVariantEnum.Compact })
                {
                    string variantName = MissionPromptBuilder.GetPersonaVariantTemplateName(personaTemplateName, variant);
                    defaults[variantName] = new EmbeddedTemplate
                    {
                        Name = variantName,
                        Description = variant.ToString() + " variant of " + personaTemplateName + " (prompt variant comparison; Current is " + personaTemplateName + ").",
                        Category = "persona",
                        Content = PersonaPromptVariants.GetContent(personaTemplateName, variant)!
                    };
                }
            }

            return defaults;
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// Represents a built-in template stored as an embedded resource default.
        /// </summary>
        private class EmbeddedTemplate
        {
            /// <summary>
            /// Template name.
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// Human-readable description.
            /// </summary>
            public string Description { get; set; } = "";

            /// <summary>
            /// Template category.
            /// </summary>
            public string Category { get; set; } = "mission";

            /// <summary>
            /// Template content with {Placeholder} parameters.
            /// </summary>
            public string Content { get; set; } = "";
        }

        #endregion
    }
}
