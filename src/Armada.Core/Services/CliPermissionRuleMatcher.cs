namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Matches CLI permission rules against a tool call with Claude Code's documented semantics, on typed input:
    /// <list type="bullet">
    /// <item>A bare tool name (or <c>Tool(*)</c>) matches every call of that tool. <c>mcp__server</c> and
    /// <c>mcp__server__*</c> match every tool of that MCP server.</item>
    /// <item><c>Bash(prefix:*)</c> matches a command equal to the prefix or starting with the prefix and a space;
    /// <c>Bash(text with *)</c> is a glob where <c>*</c> matches any characters; anything else must equal the command.
    /// The command is split at shell control operators (<see cref="ShellCommandSplitter"/>): a call is allowed only when
    /// every subcommand matches an allow rule, and denied when any subcommand matches a deny rule. A subcommand with
    /// command substitution is never allowed by a specifier rule.</item>
    /// <item><c>WebFetch(domain:example.com)</c> matches the URL host; <c>domain:*.example.com</c> matches subdomains.</item>
    /// <item><c>Read(...)</c> and <c>Edit(...)</c> take gitignore-style paths: <c>//abs/path</c> is absolute, <c>~/path</c>
    /// is under the home directory, and <c>/path</c>, <c>./path</c>, or <c>path</c> are relative to the working directory.
    /// <c>*</c> matches within a path segment and <c>**</c> across segments. Edit rules cover Edit, Write, MultiEdit, and
    /// NotebookEdit; Read rules cover Read, Glob, Grep, and LS.</item>
    /// <item>A specifier on any other tool is not interpreted and never matches.</item>
    /// </list>
    /// Deny rules win over allow rules.
    /// </summary>
    public static class CliPermissionRuleMatcher
    {
        #region Private-Members

        private static readonly HashSet<string> _EditTools = new HashSet<string>(StringComparer.Ordinal) { "Edit", "Write", "MultiEdit", "NotebookEdit" };
        private static readonly HashSet<string> _ReadTools = new HashSet<string>(StringComparer.Ordinal) { "Read", "Glob", "Grep", "LS" };
        private static readonly JsonSerializerOptions _InputOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse tool input JSON into its typed view; malformed or non-object input yields an empty view.
        /// </summary>
        /// <param name="inputJson">Tool input JSON text, or null.</param>
        /// <returns>The typed input; never null.</returns>
        public static CliToolInput ParseInput(string? inputJson)
        {
            if (String.IsNullOrWhiteSpace(inputJson)) return new CliToolInput();
            try
            {
                return JsonSerializer.Deserialize<CliToolInput>(inputJson!, _InputOptions) ?? new CliToolInput();
            }
            catch (JsonException)
            {
                return new CliToolInput();
            }
        }

        /// <summary>
        /// Evaluate rules against one tool call: the first matching deny rule denies; otherwise allow rules allow when
        /// they cover the call (every subcommand for Bash); otherwise no rule decides. Invalid rules are skipped.
        /// </summary>
        /// <param name="rules">Applicable rules.</param>
        /// <param name="toolName">Tool name as the CLI reported it.</param>
        /// <param name="inputJson">Tool input JSON text.</param>
        /// <param name="context">Path resolution context, or null.</param>
        /// <returns>The evaluation; never null.</returns>
        public static CliPermissionRuleEvaluation Evaluate(IEnumerable<CliPermissionRule> rules, string toolName, string? inputJson, CliPermissionMatchContext? context)
        {
            CliPermissionRuleEvaluation evaluation = new CliPermissionRuleEvaluation();
            if (rules == null || String.IsNullOrWhiteSpace(toolName)) return evaluation;
            CliToolInput input = ParseInput(inputJson);
            CliPermissionMatchContext ctx = context ?? new CliPermissionMatchContext();

            List<KeyValuePair<CliPermissionRule, CliPermissionRulePattern>> parsed = new List<KeyValuePair<CliPermissionRule, CliPermissionRulePattern>>();
            foreach (CliPermissionRule rule in rules)
            {
                if (rule == null) continue;
                if (CliPermissionRuleParser.TryParse(rule.Pattern, out CliPermissionRulePattern? pattern) && pattern != null)
                    parsed.Add(new KeyValuePair<CliPermissionRule, CliPermissionRulePattern>(rule, pattern));
            }

            List<KeyValuePair<CliPermissionRule, CliPermissionRulePattern>> denies = parsed.Where(p => p.Key.Action == CliPermissionRuleActionEnum.Deny && ToolMatches(p.Value, toolName)).ToList();
            List<KeyValuePair<CliPermissionRule, CliPermissionRulePattern>> allows = parsed.Where(p => p.Key.Action == CliPermissionRuleActionEnum.Allow && ToolMatches(p.Value, toolName)).ToList();
            bool isBash = String.Equals(toolName, "Bash", StringComparison.Ordinal);
            ShellCommandSplit split = isBash ? ShellCommandSplitter.Split(input.Command) : new ShellCommandSplit();

            foreach (KeyValuePair<CliPermissionRule, CliPermissionRulePattern> deny in denies)
            {
                bool matched;
                if (deny.Value.Specifier == null) matched = true;
                else if (isBash)
                {
                    // Deny broadly: any subcommand, or the whole line.
                    matched = split.Commands.Any(c => BashSpecifierMatches(deny.Value.Specifier!, c, true))
                        || (!String.IsNullOrWhiteSpace(input.Command) && BashSpecifierMatches(deny.Value.Specifier!, input.Command!.Trim(), true));
                }
                else matched = SpecifierMatches(deny.Value, toolName, input, ctx, true);

                if (matched)
                {
                    evaluation.Action = CliPermissionRuleActionEnum.Deny;
                    evaluation.Rule = deny.Key;
                    return evaluation;
                }
            }

            KeyValuePair<CliPermissionRule, CliPermissionRulePattern> bare = allows.FirstOrDefault(a => a.Value.Specifier == null);
            if (bare.Key != null)
            {
                evaluation.Action = CliPermissionRuleActionEnum.Allow;
                evaluation.Rule = bare.Key;
                return evaluation;
            }

            if (isBash)
            {
                if (split.Unbalanced || split.Commands.Count == 0) return evaluation;
                ShellCommandSplit perCommand;
                CliPermissionRule? first = null;
                foreach (string command in split.Commands)
                {
                    perCommand = ShellCommandSplitter.Split(command);
                    if (perCommand.HasSubstitution) return evaluation;
                    KeyValuePair<CliPermissionRule, CliPermissionRulePattern> match = allows.FirstOrDefault(a => BashSpecifierMatches(a.Value.Specifier!, command));
                    if (match.Key == null) return evaluation;
                    if (first == null) first = match.Key;
                }

                evaluation.Action = CliPermissionRuleActionEnum.Allow;
                evaluation.Rule = first;
                return evaluation;
            }

            foreach (KeyValuePair<CliPermissionRule, CliPermissionRulePattern> allow in allows)
            {
                if (SpecifierMatches(allow.Value, toolName, input, ctx, false))
                {
                    evaluation.Action = CliPermissionRuleActionEnum.Allow;
                    evaluation.Rule = allow.Key;
                    return evaluation;
                }
            }

            return evaluation;
        }

        /// <summary>
        /// Whether a rule's tool name part covers a tool (exact name, an MCP server prefix, or the Edit and Read families).
        /// </summary>
        /// <param name="pattern">Parsed rule.</param>
        /// <param name="toolName">Tool name.</param>
        /// <returns>True when the rule applies to the tool.</returns>
        public static bool ToolMatches(CliPermissionRulePattern pattern, string toolName)
        {
            if (pattern == null || String.IsNullOrEmpty(toolName)) return false;
            string ruleTool = pattern.ToolName;
            if (ruleTool.StartsWith("mcp__", StringComparison.Ordinal))
            {
                if (ruleTool.EndsWith("__*", StringComparison.Ordinal))
                    return toolName.StartsWith(ruleTool.Substring(0, ruleTool.Length - 1), StringComparison.Ordinal);
                string rest = ruleTool.Substring(5);
                if (rest.IndexOf("__", StringComparison.Ordinal) < 0 && pattern.Specifier == null)
                    return toolName.StartsWith(ruleTool + "__", StringComparison.Ordinal);
                return String.Equals(ruleTool, toolName, StringComparison.Ordinal);
            }

            if (String.Equals(ruleTool, toolName, StringComparison.Ordinal)) return true;
            if (String.Equals(ruleTool, "Edit", StringComparison.Ordinal) && _EditTools.Contains(toolName)) return true;
            if (String.Equals(ruleTool, "Read", StringComparison.Ordinal) && _ReadTools.Contains(toolName)) return true;
            return false;
        }

        /// <summary>
        /// Whether a Bash specifier matches one simple command (allow semantics).
        /// </summary>
        /// <param name="specifier">Specifier (for example <c>git status:*</c>, <c>npm run *</c>, or an exact command).</param>
        /// <param name="command">One simple command (already split).</param>
        /// <returns>True on a match.</returns>
        public static bool BashSpecifierMatches(string specifier, string command)
        {
            return BashSpecifierMatches(specifier, command, false);
        }

        /// <summary>
        /// Whether a Bash specifier matches one simple command. With <paramref name="broad"/> (deny rules) a
        /// <c>prefix:*</c> rule matches any command starting with the prefix, also without a following space, and an
        /// exact rule also matches the command followed by arguments.
        /// </summary>
        /// <param name="specifier">Specifier.</param>
        /// <param name="command">One simple command.</param>
        /// <param name="broad">True for deny semantics.</param>
        /// <returns>True on a match.</returns>
        public static bool BashSpecifierMatches(string specifier, string command, bool broad)
        {
            if (String.IsNullOrEmpty(specifier) || command == null) return false;
            string cmd = command.Trim();
            if (specifier.EndsWith(":*", StringComparison.Ordinal))
            {
                string prefix = specifier.Substring(0, specifier.Length - 2).TrimEnd();
                if (prefix.Length == 0) return true;
                if (broad) return cmd.StartsWith(prefix, StringComparison.Ordinal);
                return String.Equals(cmd, prefix, StringComparison.Ordinal) || cmd.StartsWith(prefix + " ", StringComparison.Ordinal);
            }

            if (specifier.IndexOf('*') >= 0) return GlobToRegex(specifier, false).IsMatch(cmd);
            string exact = specifier.Trim();
            if (broad) return String.Equals(cmd, exact, StringComparison.Ordinal) || cmd.StartsWith(exact + " ", StringComparison.Ordinal);
            return String.Equals(cmd, exact, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a WebFetch domain specifier matches a URL.
        /// </summary>
        /// <param name="specifier">Specifier (<c>domain:example.com</c> or <c>domain:*.example.com</c>).</param>
        /// <param name="url">URL from the tool input.</param>
        /// <returns>True on a match.</returns>
        public static bool DomainMatches(string specifier, string? url)
        {
            if (String.IsNullOrWhiteSpace(specifier) || String.IsNullOrWhiteSpace(url)) return false;
            if (!specifier.StartsWith("domain:", StringComparison.OrdinalIgnoreCase)) return false;
            string domain = specifier.Substring(7).Trim().TrimEnd('.');
            if (domain.Length == 0) return false;
            if (!Uri.TryCreate(url!.Trim(), UriKind.Absolute, out Uri? uri) || String.IsNullOrEmpty(uri.Host)) return false;
            string host = uri.Host.TrimEnd('.');
            if (domain.StartsWith("*.", StringComparison.Ordinal))
                return host.EndsWith(domain.Substring(1), StringComparison.OrdinalIgnoreCase) && host.Length > domain.Length - 1;
            return String.Equals(host, domain, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a path specifier (gitignore-style) matches a path.
        /// </summary>
        /// <param name="specifier">Path specifier.</param>
        /// <param name="path">Path from the tool input.</param>
        /// <param name="context">Resolution context.</param>
        /// <param name="forDeny">True for deny rules: a relative rule with no known working directory then matches any path suffix.</param>
        /// <returns>True on a match.</returns>
        public static bool PathMatches(string specifier, string? path, CliPermissionMatchContext context, bool forDeny)
        {
            if (String.IsNullOrWhiteSpace(specifier) || String.IsNullOrWhiteSpace(path)) return false;
            CliPermissionMatchContext ctx = context ?? new CliPermissionMatchContext();
            bool ignoreCase = OperatingSystem.IsWindows();

            string target = NormalizePath(path!);
            if (!IsAbsolute(target))
            {
                if (String.IsNullOrWhiteSpace(ctx.WorkingDirectory))
                {
                    if (!forDeny) return false;
                }
                else
                {
                    target = NormalizePath(ctx.WorkingDirectory!).TrimEnd('/') + "/" + target;
                }
            }

            target = CollapseSegments(target);
            string spec = specifier.Trim();
            if (spec.EndsWith("/", StringComparison.Ordinal)) spec += "**";
            string? absolutePattern = null;
            string? relativePattern = null;
            if (spec.StartsWith("//", StringComparison.Ordinal)) absolutePattern = "/" + spec.Substring(2);
            else if (spec.StartsWith("~/", StringComparison.Ordinal))
            {
                if (String.IsNullOrWhiteSpace(ctx.HomeDirectory)) return false;
                absolutePattern = NormalizePath(ctx.HomeDirectory!).TrimEnd('/') + spec.Substring(1);
            }
            else
            {
                string relative = spec.StartsWith("./", StringComparison.Ordinal) ? spec.Substring(2) : spec.TrimStart('/');
                if (!String.IsNullOrWhiteSpace(ctx.WorkingDirectory))
                    absolutePattern = NormalizePath(ctx.WorkingDirectory!).TrimEnd('/') + "/" + relative;
                else if (forDeny)
                    relativePattern = relative;
                else
                    return false;
            }

            if (absolutePattern != null)
            {
                if (OperatingSystem.IsWindows() && absolutePattern.StartsWith("/", StringComparison.Ordinal) && target.Length > 1 && target[1] == ':')
                    absolutePattern = absolutePattern.TrimStart('/');
                return GlobToRegex(absolutePattern, true, ignoreCase).IsMatch(target);
            }

            Regex suffix = GlobToRegex(relativePattern!, true, ignoreCase);
            string[] segments = target.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (suffix.IsMatch(String.Join("/", segments.Skip(i)))) return true;
            }

            return false;
        }

        /// <summary>
        /// Suggest an allow rule for a call, used as the starting pattern of "allow and remember": for Bash the first
        /// subcommand's program (and subcommand word) as a prefix rule, for WebFetch the URL's domain, for file tools the
        /// exact absolute path, otherwise the bare tool name.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="inputJson">Tool input JSON text.</param>
        /// <returns>Rule text.</returns>
        public static string SuggestRule(string toolName, string? inputJson)
        {
            if (String.IsNullOrWhiteSpace(toolName)) return String.Empty;
            CliToolInput input = ParseInput(inputJson);
            if (String.Equals(toolName, "Bash", StringComparison.Ordinal))
            {
                ShellCommandSplit split = ShellCommandSplitter.Split(input.Command);
                if (split.Commands.Count == 0) return "Bash";
                string[] tokens = split.Commands[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0 || !IsPlainWord(tokens[0])) return "Bash";
                if (tokens.Length >= 2 && IsPlainWord(tokens[1]) && !tokens[1].StartsWith("-", StringComparison.Ordinal))
                    return "Bash(" + tokens[0] + " " + tokens[1] + ":*)";
                return "Bash(" + tokens[0] + ":*)";
            }

            if (String.Equals(toolName, "WebFetch", StringComparison.Ordinal)
                && !String.IsNullOrWhiteSpace(input.Url)
                && Uri.TryCreate(input.Url!.Trim(), UriKind.Absolute, out Uri? uri)
                && !String.IsNullOrEmpty(uri.Host))
            {
                return "WebFetch(domain:" + uri.Host + ")";
            }

            string? path = input.TargetPath;
            bool isEdit = _EditTools.Contains(toolName);
            bool isRead = _ReadTools.Contains(toolName);
            if ((isEdit || isRead) && !String.IsNullOrWhiteSpace(path) && IsAbsolute(NormalizePath(path!)))
            {
                string normalized = NormalizePath(path!);
                return (isEdit ? "Edit" : "Read") + "(/" + (normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized) + ")";
            }

            return toolName;
        }

        /// <summary>
        /// One-line description of a call: the Bash command, the WebFetch URL, the WebSearch query, the file path, or the
        /// raw input (clipped to 500 characters).
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="inputJson">Tool input JSON text.</param>
        /// <returns>Summary text.</returns>
        public static string Summarize(string toolName, string? inputJson)
        {
            CliToolInput input = ParseInput(inputJson);
            string? text = null;
            if (!String.IsNullOrWhiteSpace(input.Command)) text = input.Command;
            else if (!String.IsNullOrWhiteSpace(input.Url)) text = input.Url;
            else if (!String.IsNullOrWhiteSpace(input.Query)) text = input.Query;
            else if (!String.IsNullOrWhiteSpace(input.TargetPath)) text = input.TargetPath + (String.IsNullOrWhiteSpace(input.Pattern) ? "" : " (" + input.Pattern + ")");
            else if (!String.IsNullOrWhiteSpace(input.Pattern)) text = input.Pattern;
            else text = inputJson;
            if (String.IsNullOrEmpty(text)) return toolName ?? String.Empty;
            return text!.Length <= 500 ? text : text.Substring(0, 500) + "...";
        }

        #endregion

        #region Private-Methods

        private static bool SpecifierMatches(CliPermissionRulePattern pattern, string toolName, CliToolInput input, CliPermissionMatchContext ctx, bool forDeny)
        {
            if (pattern.Specifier == null) return true;
            string spec = pattern.Specifier;
            if (String.Equals(pattern.ToolName, "WebFetch", StringComparison.Ordinal)) return DomainMatches(spec, input.Url);
            if (String.Equals(pattern.ToolName, "Edit", StringComparison.Ordinal) || String.Equals(pattern.ToolName, "Read", StringComparison.Ordinal)
                || _EditTools.Contains(pattern.ToolName) || _ReadTools.Contains(pattern.ToolName))
                return PathMatches(spec, input.TargetPath, ctx, forDeny);
            return false;
        }

        private static bool IsPlainWord(string token)
        {
            if (String.IsNullOrEmpty(token)) return false;
            foreach (char c in token)
            {
                if (!(Char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '/' || c == '+')) return false;
            }

            return true;
        }

        private static string NormalizePath(string path)
        {
            return path.Trim().Replace('\\', '/');
        }

        /// <summary>
        /// Resolve "." and ".." segments of a forward-slash path without touching the file system (host independent).
        /// A ".." above the root is dropped.
        /// </summary>
        private static string CollapseSegments(string path)
        {
            bool rooted = path.StartsWith("/", StringComparison.Ordinal);
            List<string> parts = new List<string>();
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                parts.Add(segment);
            }

            return (rooted ? "/" : "") + String.Join("/", parts);
        }

        private static bool IsAbsolute(string normalizedPath)
        {
            if (normalizedPath.StartsWith("/", StringComparison.Ordinal)) return true;
            return normalizedPath.Length > 2 && normalizedPath[1] == ':' && normalizedPath[2] == '/';
        }

        private static Regex GlobToRegex(string glob, bool pathSemantics)
        {
            return GlobToRegex(glob, pathSemantics, false);
        }

        private static Regex GlobToRegex(string glob, bool pathSemantics, bool ignoreCase)
        {
            StringBuilder builder = new StringBuilder("^");
            for (int i = 0; i < glob.Length; i++)
            {
                char c = glob[i];
                if (c == '*')
                {
                    bool doubleStar = i + 1 < glob.Length && glob[i + 1] == '*';
                    if (pathSemantics && doubleStar)
                    {
                        i++;
                        // "**/" matches zero or more directories.
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            i++;
                            builder.Append("(?:.*/)?");
                        }
                        else
                        {
                            builder.Append(".*");
                        }
                    }
                    else
                    {
                        builder.Append(pathSemantics ? "[^/]*" : ".*");
                    }
                }
                else if (c == '?' && pathSemantics)
                {
                    builder.Append("[^/]");
                }
                else
                {
                    builder.Append(Regex.Escape(c.ToString()));
                }
            }

            builder.Append('$');
            RegexOptions options = RegexOptions.Singleline | RegexOptions.CultureInvariant;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;
            return new Regex(builder.ToString(), options, TimeSpan.FromSeconds(1));
        }

        #endregion
    }
}
