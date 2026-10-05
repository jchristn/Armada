namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// CLI permission rules in Claude Code permission rule syntax: parsing, shell splitting, and matching on typed tool
    /// input (Bash prefix, glob, exact, compound commands, substitution; WebFetch domains; gitignore-style paths; MCP
    /// server patterns; tool families; run_process with Bash semantics on its command line or argument vector),
    /// deny-over-allow evaluation, the suggested rule and summary of a call, and unrestricted shell rules.
    /// </summary>
    public sealed class CliPermissionRuleMatcherSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CliPermissionRules";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("parser_accepts_documented_forms", "The parser accepts bare names, specifiers, Tool(*), and MCP server wildcards", () =>
            {
                CliPermissionRulePattern bash = CliPermissionRuleParser.Parse("  Bash(git status:*) ");
                AssertEqual("Bash", bash.ToolName);
                AssertEqual("git status:*", bash.Specifier);
                AssertEqual("Bash(git status:*)", bash.Raw);
                AssertNull(CliPermissionRuleParser.Parse("Bash(*)").Specifier, "Tool(*) is the bare tool");
                AssertEqual("Bash", CliPermissionRuleParser.Parse("Bash(*)").Raw);
                AssertEqual("echo (a)", CliPermissionRuleParser.Parse("Bash(echo (a))").Specifier, "inner parentheses kept");
                AssertEqual("mcp__github__*", CliPermissionRuleParser.Parse("mcp__github__*").ToolName);
                AssertEqual("mcp__github", CliPermissionRuleParser.Parse("mcp__github").ToolName);
                AssertEqual("domain:example.com", CliPermissionRuleParser.Parse("WebFetch(domain:example.com)").Specifier);
            }));

            cases.Add(Case("parser_rejects_malformed_rules", "Empty, unterminated, empty-specifier, multi-line, and misplaced wildcard rules are ArgumentException", () =>
            {
                foreach (string bad in new[] { "", "   ", "Bash(", "Bash()", "Bash(ls", "Ba sh", "*", "Bash*", "Bash__*", "mcp__x__*(y)", "Bash(a)\nRead", new string('a', 1001) })
                {
                    AssertThrows<ArgumentException>(() => CliPermissionRuleParser.Parse(bad), "rejects: " + bad.Replace("\n", "\\n"));
                    AssertFalse(CliPermissionRuleParser.TryParse(bad, out CliPermissionRulePattern? _), "TryParse false: " + bad.Replace("\n", "\\n"));
                }
            }));

            cases.Add(Case("splitter_splits_operators_outside_quotes", "Commands split at &&, ||, ;, |, &, and newlines, but not inside quotes or redirections", () =>
            {
                AssertEqual("git status|git diff|wc -l|echo a|sleep 1|ls", String.Join("|", ShellCommandSplitter.Split("git status && git diff | wc -l; echo a & sleep 1\nls").Commands));
                AssertEqual("echo \"a && b\"", String.Join("|", ShellCommandSplitter.Split("echo \"a && b\"").Commands), "double-quoted operator");
                AssertEqual("echo 'x; y'", String.Join("|", ShellCommandSplitter.Split("echo 'x; y'").Commands), "single-quoted operator");
                AssertEqual("make 2>&1|tee log", String.Join("|", ShellCommandSplitter.Split("make 2>&1 | tee log").Commands), "2>&1 is a redirection");
                AssertEqual("a|b", String.Join("|", ShellCommandSplitter.Split("a || b").Commands));
                AssertEqual("echo a\\;b", String.Join("|", ShellCommandSplitter.Split("echo a\\;b").Commands), "escaped separator");
                AssertTrue(ShellCommandSplitter.Split("echo $(rm -rf x)").HasSubstitution, "$( substitution");
                AssertTrue(ShellCommandSplitter.Split("echo `id`").HasSubstitution, "backtick substitution");
                AssertTrue(ShellCommandSplitter.Split("echo \"$(id)\"").HasSubstitution, "substitution inside double quotes");
                AssertFalse(ShellCommandSplitter.Split("echo '$(id)'").HasSubstitution, "single quotes are literal");
                AssertTrue(ShellCommandSplitter.Split("echo \"abc").Unbalanced, "unbalanced quote");
                AssertEqual(0, ShellCommandSplitter.Split("  ").Commands.Count);
            }));

            cases.Add(Case("bash_prefix_glob_and_exact", "Bash prefix rules need a word boundary, globs match anywhere, and plain specifiers are exact", () =>
            {
                AssertTrue(CliPermissionRuleMatcher.BashSpecifierMatches("git status:*", "git status"));
                AssertTrue(CliPermissionRuleMatcher.BashSpecifierMatches("git status:*", "git status --short"));
                AssertFalse(CliPermissionRuleMatcher.BashSpecifierMatches("git status:*", "git statusx"), "no word boundary");
                AssertFalse(CliPermissionRuleMatcher.BashSpecifierMatches("git status:*", "git stash"));
                AssertTrue(CliPermissionRuleMatcher.BashSpecifierMatches("git status:*", "git statusx", true), "deny prefix is broad");
                AssertTrue(CliPermissionRuleMatcher.BashSpecifierMatches("npm run *", "npm run test"));
                AssertFalse(CliPermissionRuleMatcher.BashSpecifierMatches("npm run *", "npm install"));
                AssertTrue(CliPermissionRuleMatcher.BashSpecifierMatches("* --version", "node --version"));
                AssertTrue(CliPermissionRuleMatcher.BashSpecifierMatches("npm test", "npm test"));
                AssertFalse(CliPermissionRuleMatcher.BashSpecifierMatches("npm test", "npm test --watch"), "exact for allow");
                AssertTrue(CliPermissionRuleMatcher.BashSpecifierMatches("rm", "rm -rf /", true), "exact deny also covers arguments");
            }));

            cases.Add(Case("compound_commands_need_every_part_allowed", "A compound command is allowed only when every subcommand matches an allow rule; any denied part denies", () =>
            {
                List<CliPermissionRule> rules = new List<CliPermissionRule>
                {
                    Rule("Bash(git status:*)", CliPermissionRuleActionEnum.Allow),
                    Rule("Bash(git diff:*)", CliPermissionRuleActionEnum.Allow)
                };
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(rules, "Bash", "{\"command\":\"git status && git diff HEAD\"}").Action, "both parts allowed");
                AssertNull(Evaluate(rules, "Bash", "{\"command\":\"git status && rm -rf build\"}").Action, "one part not allowed: prompt");
                AssertNull(Evaluate(rules, "Bash", "{\"command\":\"git status $(rm -rf x)\"}").Action, "substitution never allowed by a specifier rule");
                AssertNull(Evaluate(rules, "Bash", "{\"command\":\"git status \\\"unbalanced\"}").Action, "unbalanced quotes never allowed");

                rules.Add(Rule("Bash(rm:*)", CliPermissionRuleActionEnum.Deny));
                CliPermissionRuleEvaluation denied = Evaluate(rules, "Bash", "{\"command\":\"git status; rm -rf build\"}");
                AssertEqual(CliPermissionRuleActionEnum.Deny, denied.Action, "denied part denies the whole call");
                AssertEqual("Bash(rm:*)", denied.Rule!.Pattern);
            }));

            cases.Add(Case("deny_wins_over_allow", "A matching deny rule wins over a bare allow rule", () =>
            {
                List<CliPermissionRule> rules = new List<CliPermissionRule>
                {
                    Rule("Bash", CliPermissionRuleActionEnum.Allow),
                    Rule("Bash(curl:*)", CliPermissionRuleActionEnum.Deny)
                };
                AssertEqual(CliPermissionRuleActionEnum.Deny, Evaluate(rules, "Bash", "{\"command\":\"curl https://x\"}").Action);
                CliPermissionRuleEvaluation allowed = Evaluate(rules, "Bash", "{\"command\":\"ls\"}");
                AssertEqual(CliPermissionRuleActionEnum.Allow, allowed.Action);
                AssertEqual("Bash", allowed.Rule!.Pattern);
                AssertNull(Evaluate(rules, "WebFetch", "{\"url\":\"https://x\"}").Action, "rules for another tool do not apply");
            }));

            cases.Add(Case("webfetch_domains", "WebFetch domain rules match the URL host exactly or as a subdomain wildcard", () =>
            {
                AssertTrue(CliPermissionRuleMatcher.DomainMatches("domain:example.com", "https://EXAMPLE.com/a?b"));
                AssertFalse(CliPermissionRuleMatcher.DomainMatches("domain:example.com", "https://api.example.com/"));
                AssertTrue(CliPermissionRuleMatcher.DomainMatches("domain:*.example.com", "https://api.example.com/"));
                AssertFalse(CliPermissionRuleMatcher.DomainMatches("domain:*.example.com", "https://example.com/"), "apex is not a subdomain");
                AssertFalse(CliPermissionRuleMatcher.DomainMatches("domain:*.example.com", "https://evilexample.com/"));
                AssertFalse(CliPermissionRuleMatcher.DomainMatches("domain:example.com", "https://example.com.evil.net/"));
                AssertFalse(CliPermissionRuleMatcher.DomainMatches("example.com", "https://example.com/"), "needs the domain: prefix");
                AssertFalse(CliPermissionRuleMatcher.DomainMatches("domain:example.com", "not a url"));
                List<CliPermissionRule> rules = new List<CliPermissionRule> { Rule("WebFetch(domain:docs.example.com)", CliPermissionRuleActionEnum.Allow) };
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(rules, "WebFetch", "{\"url\":\"https://docs.example.com/x\",\"prompt\":\"p\"}").Action);
                AssertNull(Evaluate(rules, "WebFetch", "{\"url\":\"https://other.example.com/x\"}").Action);
            }));

            cases.Add(Case("paths_are_gitignore_style", "Path rules: //absolute, ~/home, and relative to the working directory; * stays in a segment, ** crosses segments", () =>
            {
                CliPermissionMatchContext ctx = new CliPermissionMatchContext { WorkingDirectory = "/work/dock", HomeDirectory = "/home/u" };
                AssertTrue(CliPermissionRuleMatcher.PathMatches("//work/dock/src/**", "/work/dock/src/a/b.cs", ctx, false));
                AssertFalse(CliPermissionRuleMatcher.PathMatches("//work/dock/src/**", "/work/dock/test/a.cs", ctx, false));
                AssertTrue(CliPermissionRuleMatcher.PathMatches("src/*.cs", "/work/dock/src/a.cs", ctx, false), "relative to the working directory");
                AssertFalse(CliPermissionRuleMatcher.PathMatches("src/*.cs", "/work/dock/src/x/a.cs", ctx, false), "* stays in one segment");
                AssertTrue(CliPermissionRuleMatcher.PathMatches("./src/**", "src/x/a.cs", ctx, false), "relative input path resolved against the working directory");
                AssertTrue(CliPermissionRuleMatcher.PathMatches("~/.config/**", "/home/u/.config/app.json", ctx, false));
                AssertTrue(CliPermissionRuleMatcher.PathMatches("//work/dock/docs/", "/work/dock/docs/readme.md", ctx, false), "a trailing slash means the directory's contents");
                AssertFalse(CliPermissionRuleMatcher.PathMatches("//work/dock/../etc/**", "/etc/passwd", ctx, false), "no path traversal through the pattern");

                CliPermissionMatchContext unknown = new CliPermissionMatchContext();
                AssertFalse(CliPermissionRuleMatcher.PathMatches("src/**", "/anywhere/src/a.cs", unknown, false), "relative allow without a working directory never matches");
                AssertTrue(CliPermissionRuleMatcher.PathMatches(".env", "/anywhere/x/.env", unknown, true), "relative deny without a working directory matches any suffix");
            }));

            cases.Add(Case("tool_families_and_mcp_patterns", "Edit rules cover Write/MultiEdit/NotebookEdit, Read rules cover Glob/Grep/LS, and MCP server patterns cover their tools", () =>
            {
                CliPermissionRulePattern edit = CliPermissionRuleParser.Parse("Edit");
                foreach (string tool in new[] { "Edit", "Write", "MultiEdit", "NotebookEdit" }) AssertTrue(CliPermissionRuleMatcher.ToolMatches(edit, tool), "Edit covers " + tool);
                AssertFalse(CliPermissionRuleMatcher.ToolMatches(edit, "Read"));
                CliPermissionRulePattern read = CliPermissionRuleParser.Parse("Read");
                foreach (string tool in new[] { "Read", "Glob", "Grep", "LS" }) AssertTrue(CliPermissionRuleMatcher.ToolMatches(read, tool), "Read covers " + tool);
                AssertTrue(CliPermissionRuleMatcher.ToolMatches(CliPermissionRuleParser.Parse("mcp__github"), "mcp__github__create_issue"));
                AssertTrue(CliPermissionRuleMatcher.ToolMatches(CliPermissionRuleParser.Parse("mcp__github__*"), "mcp__github__create_issue"));
                AssertFalse(CliPermissionRuleMatcher.ToolMatches(CliPermissionRuleParser.Parse("mcp__github"), "mcp__githubber__x"));
                AssertTrue(CliPermissionRuleMatcher.ToolMatches(CliPermissionRuleParser.Parse("mcp__github__create_issue"), "mcp__github__create_issue"));
                AssertFalse(CliPermissionRuleMatcher.ToolMatches(CliPermissionRuleParser.Parse("mcp__github__create_issue"), "mcp__github__delete_repo"));
                AssertFalse(CliPermissionRuleMatcher.ToolMatches(CliPermissionRuleParser.Parse("Bash"), "bash"), "tool names are case-sensitive");

                List<CliPermissionRule> rules = new List<CliPermissionRule> { Rule("Edit(//repo/src/**)", CliPermissionRuleActionEnum.Allow), Rule("Task(anything)", CliPermissionRuleActionEnum.Allow) };
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(rules, "Write", "{\"file_path\":\"/repo/src/a.txt\",\"content\":\"x\"}").Action, "Write under an Edit rule");
                AssertNull(Evaluate(rules, "Write", "{\"file_path\":\"/repo/README.md\"}").Action);
                AssertNull(Evaluate(rules, "Task", "{\"description\":\"anything\"}").Action, "a specifier on an uninterpreted tool never matches");
            }));

            cases.Add(Case("malformed_input_and_rules_are_safe", "Malformed tool input and invalid stored rules never allow", () =>
            {
                List<CliPermissionRule> rules = new List<CliPermissionRule> { Rule("Bash(", CliPermissionRuleActionEnum.Allow), Rule("Bash(ls:*)", CliPermissionRuleActionEnum.Allow) };
                AssertNull(Evaluate(rules, "Bash", "not json").Action, "unparseable input");
                AssertNull(Evaluate(rules, "Bash", "[1,2]").Action, "non-object input");
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(rules, "Bash", "{\"command\":\"ls -la\"}").Action, "the invalid rule is skipped, the valid one applies");
            }));

            cases.Add(Case("suggested_rules_and_summaries", "The suggested rule and summary come from typed input", () =>
            {
                AssertEqual("Bash(git status:*)", CliPermissionRuleMatcher.SuggestRule("Bash", "{\"command\":\"git status --short && ls\"}"));
                AssertEqual("Bash(ls:*)", CliPermissionRuleMatcher.SuggestRule("Bash", "{\"command\":\"ls -la\"}"));
                AssertEqual("Bash", CliPermissionRuleMatcher.SuggestRule("Bash", "{\"command\":\"$(evil)\"}"));
                AssertEqual("WebFetch(domain:docs.example.com)", CliPermissionRuleMatcher.SuggestRule("WebFetch", "{\"url\":\"https://docs.example.com/a\"}"));
                AssertEqual("Edit(//repo/a.txt)", CliPermissionRuleMatcher.SuggestRule("Write", "{\"file_path\":\"/repo/a.txt\"}"));
                AssertEqual("WebSearch", CliPermissionRuleMatcher.SuggestRule("WebSearch", "{\"query\":\"q\"}"));
                AssertEqual("git push", CliPermissionRuleMatcher.Summarize("Bash", "{\"command\":\"git push\",\"description\":\"d\"}"));
                AssertEqual("https://x.test/", CliPermissionRuleMatcher.Summarize("WebFetch", "{\"url\":\"https://x.test/\"}"));
                AssertEqual("/repo/a.txt", CliPermissionRuleMatcher.Summarize("Read", "{\"file_path\":\"/repo/a.txt\"}"));
                AssertTrue(CliPermissionRuleMatcher.Summarize("Bash", "{\"command\":\"" + new string('x', 600) + "\"}").Length <= 503, "clipped");
            }));

            cases.Add(Case("run_process_rules_match_the_command", "run_process rules take Bash specifiers: shell command lines split like Bash; an argument vector is one command; deny wins", () =>
            {
                List<CliPermissionRule> rules = new List<CliPermissionRule>
                {
                    Rule("run_process(git status:*)", CliPermissionRuleActionEnum.Allow),
                    Rule("run_process(npm run *)", CliPermissionRuleActionEnum.Allow)
                };
                CliPermissionRuleEvaluation allowed = Evaluate(rules, "run_process", "{\"command\":\"git status --short\"}");
                AssertEqual(CliPermissionRuleActionEnum.Allow, allowed.Action, "shell command line matches the prefix rule");
                AssertEqual("run_process(git status:*)", allowed.Rule!.Pattern, "deciding rule");
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(rules, "run_process", "{\"command\":\"git\",\"args\":[\"status\",\"-s\"]}").Action, "argument vector joins into the command line");
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(rules, "run_process", "{\"command\":\"npm run test\"}").Action, "glob rule");
                AssertNull(Evaluate(rules, "run_process", "{\"command\":\"git push origin main\"}").Action, "another command still asks");
                AssertNull(Evaluate(rules, "run_process", "{\"command\":\"git status && rm -rf build\"}").Action, "every shell part must be allowed");
                AssertNull(Evaluate(rules, "run_process", "{\"command\":\"git status $(rm -rf x)\"}").Action, "shell substitution is never allowed");
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(rules, "run_process", "{\"command\":\"git\",\"args\":[\"status\",\"$(literal)\",\"&&\",\"x\"]}").Action, "no shell runs for an argument vector, so operators and $() in it are literal arguments");
                AssertNull(Evaluate(rules, "Bash", "{\"command\":\"git status\"}").Action, "run_process rules do not cover Bash");
                AssertNull(Evaluate(new List<CliPermissionRule> { Rule("Bash(git status:*)", CliPermissionRuleActionEnum.Allow) }, "run_process", "{\"command\":\"git status\"}").Action, "Bash rules do not cover run_process");

                rules.Add(Rule("run_process(rm:*)", CliPermissionRuleActionEnum.Deny));
                AssertEqual(CliPermissionRuleActionEnum.Deny, Evaluate(rules, "run_process", "{\"command\":\"git status; rm -rf build\"}").Action, "a denied shell part denies");
                AssertEqual(CliPermissionRuleActionEnum.Deny, Evaluate(rules, "run_process", "{\"command\":\"rm\",\"args\":[\"-rf\",\"build\"]}").Action, "deny matches an argument vector");
                rules.Add(Rule("run_process", CliPermissionRuleActionEnum.Allow));
                AssertEqual(CliPermissionRuleActionEnum.Deny, Evaluate(rules, "run_process", "{\"command\":\"rm -rf x\"}").Action, "deny wins over a bare allow");
            }));

            cases.Add(Case("run_process_suggestions_never_allow_everything", "run_process suggests a prefix rule from its command line, or the exact command, never the bare tool; its summary includes the arguments", () =>
            {
                AssertEqual("run_process(git status:*)", CliPermissionRuleMatcher.SuggestRule("run_process", "{\"command\":\"git status --short\"}"));
                AssertEqual("run_process(git log:*)", CliPermissionRuleMatcher.SuggestRule("run_process", "{\"command\":\"git\",\"args\":[\"log\",\"-1\"]}"));
                AssertEqual("run_process(ls:*)", CliPermissionRuleMatcher.SuggestRule("run_process", "{\"command\":\"ls -la && pwd\"}"));
                AssertEqual("run_process(\"/opt/my tool/run\" --help)", CliPermissionRuleMatcher.SuggestRule("run_process", "{\"command\":\"\\\"/opt/my tool/run\\\" --help\"}"), "exact command when the program is not a plain word");
                AssertEqual("Bash", CliPermissionRuleMatcher.SuggestRule("Bash", "{\"command\":\"\\\"/opt/my tool/run\\\" --help\"}"), "Bash keeps its bare fallback");
                AssertEqual("git log -1", CliPermissionRuleMatcher.Summarize("run_process", "{\"command\":\"git\",\"args\":[\"log\",\"-1\"]}"), "summary with arguments");
                string suggested = CliPermissionRuleMatcher.SuggestRule("run_process", "{\"command\":\"git status\"}");
                AssertEqual(CliPermissionRuleActionEnum.Allow, Evaluate(new List<CliPermissionRule> { Rule(suggested, CliPermissionRuleActionEnum.Allow) }, "run_process", "{\"command\":\"git status\"}").Action, "the suggested rule allows the call it came from");
                AssertNull(Evaluate(new List<CliPermissionRule> { Rule(suggested, CliPermissionRuleActionEnum.Allow) }, "run_process", "{\"command\":\"curl https://x\"}").Action, "and nothing else");
            }));

            cases.Add(Case("unrestricted_shell_rules", "Bare and wildcard shell rules are flagged as allowing every command; specific and non-shell rules are not", () =>
            {
                foreach (string open in new[] { "Bash", "run_process", "Bash(*)", "run_process(*)", "Bash(:*)", "run_process( :* )", "Bash(**)" })
                    AssertTrue(CliPermissionRuleMatcher.IsUnrestrictedShellRule(open), "unrestricted: " + open);
                foreach (string narrow in new[] { "Bash(git status:*)", "run_process(npm run *)", "WebFetch", "Read", "mcp__github", "", "Bash(" })
                    AssertFalse(CliPermissionRuleMatcher.IsUnrestrictedShellRule(narrow), "not unrestricted: " + narrow);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "CLI permission rules", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static CliPermissionRule Rule(string pattern, CliPermissionRuleActionEnum action)
        {
            return new CliPermissionRule { Pattern = pattern, Action = action };
        }

        private static CliPermissionRuleEvaluation Evaluate(List<CliPermissionRule> rules, string tool, string input)
        {
            return CliPermissionRuleMatcher.Evaluate(rules, tool, input, new CliPermissionMatchContext { WorkingDirectory = "/repo" });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
