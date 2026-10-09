namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// Ask slash command rules shared with the dashboard and the mobile app (<c>lib/askCommands.ts</c>): the built-in
    /// local commands, the <c>/</c> menu catalog (quick actions, then local commands), filtering, parsing the composer
    /// text into a command with arguments, captain name matching, and <c>/thinking</c> arguments. Thread-safe
    /// (stateless).
    /// </summary>
    public static class AskCommands
    {
        #region Public-Members

        /// <summary>
        /// The escape for a message that starts with a slash: a leading <c>//</c> sends the rest as text beginning with
        /// one <c>/</c> (matches the dashboard's and mobile app's shared catalog).
        /// </summary>
        public const string Escape = "//";

        /// <summary>
        /// Maximum conversation title length (the server's limit).
        /// </summary>
        public const int MaxTitleLength = 200;

        /// <summary>
        /// The hint for an unknown command (placeholder <c>command</c>).
        /// </summary>
        public const string UnknownCommandHint = "Unknown command {{command}}. Type / to see commands";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The message to send for composer text that is not a command: trimmed, with a leading <c>//</c> turned into
        /// <c>/</c>.
        /// </summary>
        /// <param name="input">Composer text.</param>
        /// <returns>Message text.</returns>
        public static string MessageText(string? input)
        {
            string text = (input ?? "").Trim();
            return text.StartsWith(Escape, StringComparison.Ordinal) ? text.Substring(1) : text;
        }

        /// <summary>
        /// The built-in local commands, in menu order.
        /// </summary>
        /// <returns>New list.</returns>
        public static List<AskLocalCommand> LocalCommands()
        {
            return new List<AskLocalCommand>
            {
                Make(AskLocalCommandEnum.New, "/new", "", false, "New conversation", "Start a new conversation with a fresh context (also /clear)", "/clear"),
                Make(AskLocalCommandEnum.Help, "/help", "", false, "Help", "List the commands you can type here"),
                Make(AskLocalCommandEnum.Summarize, "/summarize", "", false, "Summarize", "Post a short summary of this conversation"),
                Make(AskLocalCommandEnum.Rename, "/rename", "<title>", true, "Rename", "Rename this conversation"),
                Make(AskLocalCommandEnum.Archive, "/archive", "", false, "Archive", "Archive this conversation"),
                Make(AskLocalCommandEnum.Captain, "/captain", "<name>", false, "Captain", "Switch the captain by name"),
                Make(AskLocalCommandEnum.Thinking, "/thinking", "on|off", false, "Show thinking", "Turn Show thinking on or off")
            };
        }

        /// <summary>
        /// The <c>/</c> menu catalog: quick actions first (server order), then the local commands. A quick action whose
        /// command collides with a local command or alias is left out.
        /// </summary>
        /// <param name="quickActions">Merged quick actions.</param>
        /// <returns>Catalog.</returns>
        public static List<AskCommandItem> Catalog(IEnumerable<AskQuickAction>? quickActions)
        {
            List<AskLocalCommand> locals = LocalCommands();
            HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AskLocalCommand local in locals)
            {
                reserved.Add(local.Command);
                foreach (string alias in local.Aliases) reserved.Add(alias);
            }

            List<AskCommandItem> items = new List<AskCommandItem>();
            foreach (AskQuickAction action in quickActions ?? Enumerable.Empty<AskQuickAction>())
            {
                if (action == null) continue;
                string command = AskQuickActions.CommandOf(action);
                if (reserved.Contains(command)) continue;
                AskCommandItem item = new AskCommandItem();
                item.Key = "quick:" + action.Name;
                item.Command = command;
                item.Title = String.IsNullOrEmpty(action.Title) ? (action.Name ?? "") : action.Title;
                item.Description = action.Description ?? "";
                item.Action = action;
                items.Add(item);
            }

            foreach (AskLocalCommand local in locals)
            {
                AskCommandItem item = new AskCommandItem();
                item.Key = "local:" + local.Name.ToString().ToLowerInvariant();
                item.Command = local.Command;
                item.Aliases = new List<string>(local.Aliases);
                item.Usage = local.Usage;
                item.Title = local.Title;
                item.Description = local.Description;
                item.Local = local;
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Menu entries for the composer text: only while it is a single <c>/word</c>; exact matches sort first.
        /// </summary>
        /// <param name="items">Catalog.</param>
        /// <param name="input">Composer text.</param>
        /// <returns>Matches.</returns>
        public static List<AskCommandItem> Filter(IEnumerable<AskCommandItem> items, string? input)
        {
            if (String.IsNullOrEmpty(input) || !input.StartsWith("/", StringComparison.Ordinal) || input.StartsWith(Escape, StringComparison.Ordinal)) return new List<AskCommandItem>();
            if (input.Any(Char.IsWhiteSpace)) return new List<AskCommandItem>();
            string typed = input.ToLowerInvariant();
            List<AskCommandItem> all = items.ToList();
            List<AskCommandItem> exact = all.Where(i => Spellings(i).Contains(typed)).ToList();
            List<AskCommandItem> prefix = all.Where(i => !exact.Contains(i)
                && (Spellings(i).Any(s => s.StartsWith(typed, StringComparison.Ordinal))
                    || (i.Action != null && ("/" + (i.Action.Name ?? "").ToLowerInvariant()).StartsWith(typed, StringComparison.Ordinal)))).ToList();
            exact.AddRange(prefix);
            return exact;
        }

        /// <summary>
        /// Classify the composer text: plain text, a command with its arguments (case-insensitive, aliases included),
        /// or an unknown command.
        /// </summary>
        /// <param name="items">Catalog.</param>
        /// <param name="input">Composer text.</param>
        /// <returns>Parse result.</returns>
        public static AskCommandParse Parse(IEnumerable<AskCommandItem> items, string? input)
        {
            AskCommandParse result = new AskCommandParse();
            string text = (input ?? "").Trim();
            if (!text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith(Escape, StringComparison.Ordinal)) return result;
            int split = 0;
            while (split < text.Length && !Char.IsWhiteSpace(text[split])) split++;
            string word = text.Substring(0, split);
            string args = text.Substring(split).Trim();
            string lower = word.ToLowerInvariant();
            AskCommandItem? item = items.FirstOrDefault(i => Spellings(i).Contains(lower));
            if (item == null)
            {
                result.Kind = AskCommandParseKindEnum.Unknown;
                result.Typed = word;
                return result;
            }

            result.Kind = AskCommandParseKindEnum.Command;
            result.Item = item;
            result.Args = args;
            return result;
        }

        /// <summary>
        /// What Enter runs: the highlighted menu entry while the menu is open, otherwise the parsed text.
        /// </summary>
        /// <param name="items">Catalog.</param>
        /// <param name="input">Composer text.</param>
        /// <param name="highlighted">The highlighted menu entry, or null when the menu is closed.</param>
        /// <returns>Parse result.</returns>
        public static AskCommandParse Resolve(IEnumerable<AskCommandItem> items, string? input, AskCommandItem? highlighted)
        {
            AskCommandParse parsed = Parse(items, input);
            if (highlighted == null) return parsed;
            AskCommandParse result = new AskCommandParse();
            result.Kind = AskCommandParseKindEnum.Command;
            result.Item = highlighted;
            result.Args = parsed.Kind == AskCommandParseKindEnum.Command && parsed.Item != null && parsed.Item.Key == highlighted.Key ? parsed.Args : "";
            return result;
        }

        /// <summary>
        /// Captains matching a name: an exact id or name wins, then names that start with it, contain it, or contain its
        /// letters in order.
        /// </summary>
        /// <param name="captains">Captains.</param>
        /// <param name="query">Typed name.</param>
        /// <returns>Matches (empty for a blank query).</returns>
        public static List<Captain> MatchCaptains(IEnumerable<Captain> captains, string? query)
        {
            string q = (query ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return new List<Captain>();
            List<Captain> all = captains.Where(c => c != null).ToList();
            List<Captain> byId = all.Where(c => String.Equals(c.Id, q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byId.Count > 0) return byId;
            string squeezed = new string(q.Where(ch => !Char.IsWhiteSpace(ch)).ToArray());
            List<Func<string, bool>> tiers = new List<Func<string, bool>>
            {
                n => n == q,
                n => n.StartsWith(q, StringComparison.Ordinal),
                n => n.Contains(q, StringComparison.Ordinal),
                n => IsSubsequence(squeezed, n)
            };
            foreach (Func<string, bool> tier in tiers)
            {
                List<Captain> found = all.Where(c => tier((c.Name ?? "").ToLowerInvariant())).ToList();
                if (found.Count > 0) return found;
            }

            return new List<Captain>();
        }

        /// <summary>
        /// <c>/thinking</c> arguments: on or off, or empty to toggle.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="current">Current value.</param>
        /// <param name="value">New value.</param>
        /// <returns>False for anything else.</returns>
        public static bool TryParseThinking(string? args, bool current, out bool value)
        {
            string v = (args ?? "").Trim().ToLowerInvariant();
            value = current;
            if (v.Length == 0)
            {
                value = !current;
                return true;
            }

            if (v == "on" || v == "true" || v == "yes")
            {
                value = true;
                return true;
            }

            if (v == "off" || v == "false" || v == "no")
            {
                value = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The outcome for an unknown command.
        /// </summary>
        /// <param name="typed">The command word as typed.</param>
        /// <returns>Outcome.</returns>
        public static AskCommandOutcome Unknown(string? typed)
        {
            return new AskCommandOutcome(false, UnknownCommandHint, LocalizationArgs.Of("command", String.IsNullOrEmpty(typed) ? "/" : typed));
        }

        #endregion

        #region Private-Methods

        private static AskLocalCommand Make(AskLocalCommandEnum name, string command, string usage, bool requiresArgs, string title, string description, params string[] aliases)
        {
            AskLocalCommand c = new AskLocalCommand();
            c.Name = name;
            c.Command = command;
            c.Usage = usage;
            c.RequiresArgs = requiresArgs;
            c.Title = title;
            c.Description = description;
            c.Aliases = aliases.ToList();
            return c;
        }

        private static List<string> Spellings(AskCommandItem item)
        {
            List<string> list = new List<string> { item.Command.ToLowerInvariant() };
            foreach (string alias in item.Aliases) list.Add(alias.ToLowerInvariant());
            return list;
        }

        private static bool IsSubsequence(string needle, string haystack)
        {
            if (needle.Length == 0) return true;
            int i = 0;
            foreach (char ch in haystack)
            {
                if (ch == needle[i]) i++;
                if (i == needle.Length) return true;
            }

            return false;
        }

        #endregion
    }
}
