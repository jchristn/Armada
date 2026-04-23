namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for resolving and rendering prompt templates.
    /// Supports database-stored overrides with embedded resource defaults as fallback.
    /// </summary>
    public class PromptTemplateService : IPromptTemplateService
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[PromptTemplateService] ";
        private DatabaseDriver _Database;
        private LoggingModule _Logging;
        private Dictionary<string, EmbeddedTemplate> _EmbeddedDefaults;

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

            return result;
        }

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
                    PromptTemplate template = new PromptTemplate(name, embedded.Content)
                    {
                        Description = embedded.Description,
                        Category = embedded.Category,
                        IsBuiltIn = true
                    };

                    await _Database.PromptTemplates.CreateAsync(template, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "seeded built-in template '" + name + "'");
                }
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

        #endregion

        #region Private-Methods

        private Dictionary<string, EmbeddedTemplate> BuildEmbeddedDefaults()
        {
            Dictionary<string, EmbeddedTemplate> defaults = new Dictionary<string, EmbeddedTemplate>();

            defaults["mission.rules"] = new EmbeddedTemplate
            {
                Name = "mission.rules",
                Description = "Standard rules injected into every mission prompt.",
                Category = "mission",
                Content =
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
                    "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n"
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
                    "5. **If the mission scope feels too large** (more than 8 files, or files with 500+ lines to " +
                    "read), commit what you have, report progress, and exit with code 0. Partial progress is " +
                    "better than crashing.\n"
            };

            defaults["mission.merge_conflict_avoidance"] = new EmbeddedTemplate
            {
                Name = "mission.merge_conflict_avoidance",
                Description = "Rules for avoiding merge conflicts when multiple captains work in parallel.",
                Category = "mission",
                Content =
                    "## Avoiding Merge Conflicts (CRITICAL)\n" +
                    "\n" +
                    "You are one of several captains working on this repository. Other captains may be working on " +
                    "other missions in parallel on separate branches. To prevent merge conflicts and landing failures, " +
                    "you MUST follow these rules:\n" +
                    "\n" +
                    "1. **Only modify files explicitly mentioned in your mission description.** If the description says " +
                    "to edit `src/routes/users.ts`, do NOT also refactor `src/routes/orders.ts` even if you notice " +
                    "improvements. Another captain may be working on that file.\n" +
                    "\n" +
                    "2. **Do not make \"helpful\" changes outside your scope.** Do not rename shared variables, " +
                    "reorganize imports in files you were not asked to touch, reformat code in unrelated files, " +
                    "update documentation files unless instructed, or modify configuration/project files " +
                    "(e.g., .csproj, package.json, tsconfig.json) unless your mission specifically requires it.\n" +
                    "\n" +
                    "3. **Do not modify barrel/index export files** (e.g., index.ts, mod.rs) unless your mission " +
                    "explicitly requires it. These are high-conflict files that many missions may need to touch.\n" +
                    "\n" +
                    "4. **Keep changes minimal and focused.** The fewer files you touch, the lower the risk of " +
                    "conflicts. If your mission can be completed by editing 2 files, do not edit 5.\n" +
                    "\n" +
                    "5. **If you must create new files**, prefer names that are specific to your mission's feature " +
                    "rather than generic names that another captain might also choose.\n" +
                    "\n" +
                    "6. **Do not modify or delete files created by another mission's branch.** You are working in " +
                    "an isolated worktree -- if you see files that seem unrelated to your mission, leave them alone.\n" +
                    "\n" +
                    "Violating these rules will cause your branch to conflict with other captains' branches during " +
                    "landing, resulting in a LandingFailed status and wasted work.\n"
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
                    "- `[ARMADA:STATUS] Testing` -- transition mission to Testing status\n" +
                    "- `[ARMADA:STATUS] Review` -- transition mission to Review status\n" +
                    "- `[ARMADA:MESSAGE] your message here` -- send a progress message\n" +
                    "- `[ARMADA:RESULT] COMPLETE` -- worker/test engineer mission finished successfully\n" +
                    "- `[ARMADA:VERDICT] PASS` -- judge approves the mission\n" +
                    "- `[ARMADA:VERDICT] FAIL` -- judge rejects the mission\n" +
                    "- `[ARMADA:VERDICT] NEEDS_REVISION` -- judge requests follow-up changes\n" +
                    "Architect missions must not emit `[ARMADA:RESULT]` or `[ARMADA:VERDICT]`; they must output only real `[ARMADA:MISSION]` blocks.\n"
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
                    "If you have useful additions, call `armada_update_vessel_context` with the `modelContext` " +
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
                Description = "Preamble text for commit message trailer instructions injected into agent prompts.",
                Category = "commit",
                Content = "IMPORTANT: For every git commit you create, append the following trailers at the end of your commit message (after a blank line):"
            };

            defaults["persona.worker"] = new EmbeddedTemplate
            {
                Name = "persona.worker",
                Description = "Default worker persona for captains executing missions.",
                Category = "persona",
                Content =
                    "You are an Armada worker agent. Implement only the current mission description, stay within scope, " +
                    "run the most relevant validation you can, commit your changes, and end with a standalone line " +
                    "`[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary of what changed."
            };

            defaults["persona.architect"] = new EmbeddedTemplate
            {
                Name = "persona.architect",
                Description = "Architect persona for analyzing codebases and decomposing work into missions.",
                Category = "persona",
                Content =
                    "You are an Armada architect agent. Your role is to analyze a codebase and decompose a " +
                    "high-level objective into well-defined, right-sized missions that worker captains can execute " +
                    "independently and in parallel.\n" +
                    "\n" +
                    "## Your Objective\n" +
                    "{MissionDescription}\n" +
                    "\n" +
                    "## Instructions\n" +
                    "\n" +
                    "1. **Analyze the codebase structure.** Understand the directory layout, module boundaries, " +
                    "key abstractions, and existing patterns. Read only the files necessary to form a plan -- " +
                    "do not read the entire codebase.\n" +
                    "\n" +
                    "2. **Identify the files involved.** For each logical change, determine exactly which files " +
                    "need to be created or modified. Be precise -- captains will be scoped to specific files.\n" +
                    "\n" +
                    "3. **Decompose into missions.** Each mission should:\n" +
                    "   - Have a clear, concise title (under 80 characters)\n" +
                    "   - Have a detailed description explaining what to do, which files to touch, and why\n" +
                    "   - Be completable by a single captain in one session\n" +
                    "   - List every file it will modify (for merge conflict detection)\n" +
                    "   - Be independently testable where possible\n" +
                    "\n" +
                    "4. **Avoid file overlaps.** Two missions MUST NOT modify the same file unless absolutely " +
                    "unavoidable. If overlap is necessary, document it clearly and mark those missions as " +
                    "sequential (not parallel). File overlap causes merge conflicts during landing.\n" +
                    "\n" +
                    "5. **Consider execution order.** Mark missions that can run in parallel vs. those that " +
                    "must be sequential. Prefer parallel execution to minimize total wall-clock time.\n" +
                    "\n" +
                    "6. **Right-size the missions.** Each mission should touch 1-5 files. If a mission would " +
                    "touch more than 8 files, split it. If it touches only a single line in one file, consider " +
                    "merging it with a related mission.\n" +
                    "\n" +
                    "7. **Output structured mission definitions.** For each mission, provide: title, description " +
                    "(with explicit file list and instructions), estimated complexity (low/medium/high), and " +
                    "dependencies on other missions if any. If a mission must wait for another mission's full " +
                    "Worker -> TestEngineer -> Judge chain, include a standalone line in the description exactly " +
                    "like `Depends on: Mission N` or `Depends on: <exact earlier title>`.\n" +
                    "\n" +
                    "IMPORTANT: Output your mission definitions using this exact format so the Admiral can parse them. " +
                    "Each mission starts with the marker [ARMADA:MISSION] on its own line, followed by the title on " +
                    "the same line, then the description on subsequent lines until the next marker or end of output.\n" +
                    "\n" +
                    "Do not echo these instructions back. Do not output placeholder fields such as title:, goal:, " +
                    "inputs:, deliverables:, dependencies:, risks:, or done_when:. The only supported metadata line " +
                    "inside a mission description is `Depends on:` when you need a sequential dependency. Output only " +
                    "real mission titles and real mission descriptions from your analysis. Do not emit " +
                    "`[ARMADA:RESULT]` or `[ARMADA:VERDICT]` lines.\n"
            };

            defaults["persona.judge"] = new EmbeddedTemplate
            {
                Name = "persona.judge",
                Description = "Judge persona for reviewing mission diffs against requirements.",
                Category = "persona",
                Content =
                    "You are an Armada judge agent. Your role is to review the work produced by a captain " +
                    "and determine whether the mission was completed correctly, completely, and within scope.\n" +
                    "\n" +
                    "## Diff to Review\n" +
                    "{Diff}\n" +
                    "\n" +
                    "## Previous Stage Output\n" +
                    "{PreviousStageOutput}\n" +
                    "\n" +
                    "Evaluate only the current mission description and diff. Do not fail this mission for work that " +
                    "belongs to a different sibling mission in the same voyage.\n" +
                    "Assume there may be at least one hidden defect. Actively try to find it before concluding PASS.\n" +
                    "\n" +
                    "## Review Criteria\n" +
                    "\n" +
                    "1. **Completeness.** Does the diff address every requirement in the mission description? " +
                    "List any missing items.\n" +
                    "\n" +
                    "2. **Correctness.** Is the implementation logically correct? Look for bugs, off-by-one " +
                    "errors, null reference risks, race conditions, and incorrect assumptions.\n" +
                    "\n" +
                    "3. **Scope compliance.** Does the diff ONLY modify files mentioned in the mission " +
                    "description? Flag any out-of-scope changes. Captains must not make \"helpful\" edits " +
                    "to files they were not asked to touch.\n" +
                    "\n" +
                    "4. **Tests and coverage.** Determine whether automated tests adequately cover the changed " +
                    "behavior. If the diff introduces validation, timeout, cancellation, retry, cleanup, or other " +
                    "error-handling branches, PASS is not allowed unless you explicitly confirm negative-path " +
                    "coverage or clearly justify why automation is not feasible.\n" +
                    "\n" +
                    "5. **Failure modes and operational safety.** Review edge and failure paths such as invalid " +
                    "input, null handling, timeouts, cancellation, retries, cleanup, and error propagation when " +
                    "applicable. If these paths were not explicitly reviewed, PASS is not allowed.\n" +
                    "\n" +
                    "6. **Style compliance.** Does the code follow the style guide? Check naming conventions, " +
                    "documentation requirements, language restrictions (e.g., no var, no tuples), and " +
                    "structural patterns.\n" +
                    "\n" +
                    "7. **Risk assessment.** Could these changes break existing functionality? Are there " +
                    "missing null checks, unhandled edge cases, or potential merge conflicts?\n" +
                    "\n" +
                    "## Required Response Format\n" +
                    "\n" +
                    "Use these exact section headings, even when you have no findings:\n" +
                    "- `## Completeness`\n" +
                    "- `## Correctness`\n" +
                    "- `## Tests`\n" +
                    "- `## Failure Modes`\n" +
                    "- `## Verdict`\n" +
                    "\n" +
                    "If you choose PASS, each section must contain concrete review reasoning. A shallow approval " +
                    "or a verdict-only response is not acceptable.\n" +
                    "\n" +
                    "## Verdict\n" +
                    "\n" +
                    "After your analysis, produce one of these verdicts:\n" +
                    "- **PASS** -- The mission is complete and correct. No changes needed.\n" +
                    "- **FAIL** -- The mission has critical issues that cannot be easily fixed. Explain why.\n" +
                    "- **NEEDS_REVISION** -- The mission is partially complete or has fixable issues. Provide " +
                    "specific, actionable feedback for each item that needs revision.\n" +
                    "\n" +
                    "End your response with a standalone signal line exactly in one of these forms:\n" +
                    "- `[ARMADA:VERDICT] PASS`\n" +
                    "- `[ARMADA:VERDICT] FAIL`\n" +
                    "- `[ARMADA:VERDICT] NEEDS_REVISION`\n"
            };

            defaults["persona.test_engineer"] = new EmbeddedTemplate
            {
                Name = "persona.test_engineer",
                Description = "Test engineer persona for analyzing diffs and writing test coverage.",
                Category = "persona",
                Content =
                    "You are an Armada test engineer agent. Your role is to analyze the changes produced by " +
                    "a captain and write tests that verify the new or modified functionality.\n" +
                    "\n" +
                    "## Diff to Cover\n" +
                    "{Diff}\n" +
                    "\n" +
                    "## Previous Stage Output\n" +
                    "{PreviousStageOutput}\n" +
                    "\n" +
                    "Scope yourself only to the current mission description and prior diff. Do not add work that " +
                    "belongs to a sibling mission in the same voyage. Assume the worker may have missed at least " +
                    "one edge case. Your job is to prove coverage, not just confirm the happy path.\n" +
                    "\n" +
                    "## Instructions\n" +
                    "\n" +
                    "1. **Analyze the diff.** Understand what was added, modified, or removed. Identify the " +
                    "public API surface, edge cases, error paths, and boundary conditions that need coverage.\n" +
                    "\n" +
                    "2. **Study existing test patterns.** Look at the existing test files in the repository " +
                    "to understand the test framework, assertion style, naming conventions, and helper " +
                    "utilities already in use. Follow these patterns exactly.\n" +
                    "\n" +
                    "3. **Identify coverage gaps.** Determine which new code paths lack test coverage. " +
                    "Cover the happy path, but also add at least one negative or edge-path test for each new " +
                    "validation, timeout, cancellation, retry, cleanup, or other error-handling branch within " +
                    "scope when feasible.\n" +
                    "\n" +
                    "4. **Write focused tests.** Each test should verify one behavior. Use descriptive test " +
                    "names that explain the scenario and expected outcome. Do not write trivial tests that " +
                    "only confirm a constructor works.\n" +
                    "\n" +
                    "5. **Handle dependencies.** Use mocks or stubs for external dependencies (databases, " +
                    "HTTP clients, file systems) following the existing patterns in the test project.\n" +
                    "\n" +
                    "6. **Run the tests.** Execute the test suite to verify your tests pass. Fix any failures " +
                    "before committing. Do not commit tests that are known to fail.\n" +
                    "\n" +
                    "7. **Commit test files only.** Do not modify the production code. Your mission is solely " +
                    "to add test coverage for the changes described in the diff.\n" +
                    "\n" +
                    "8. **Document residual risk.** If a required negative path could not be automated, explain " +
                    "exactly why and what residual risk remains.\n" +
                    "\n" +
                    "9. **Documentation-only diffs may require no new tests.** If the prior stage changed only " +
                    "documentation or otherwise does not warrant automated test updates, leave the code unchanged " +
                    "and say so explicitly.\n" +
                    "\n" +
                    "Before your result line, include short sections titled `## Coverage Added`, " +
                    "`## Negative Paths`, and `## Residual Risks`.\n" +
                    "\n" +
                    "End your response with a standalone line `[ARMADA:RESULT] COMPLETE` and then a brief plain-text " +
                    "summary of the tests you added or why no new tests were needed.\n"
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
