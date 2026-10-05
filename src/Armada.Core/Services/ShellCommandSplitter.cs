namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// Splits a POSIX shell command line at its control operators (<c>&amp;&amp;</c>, <c>||</c>, <c>;</c>, <c>|</c>,
    /// <c>|&amp;</c>, <c>&amp;</c>, and newlines) outside quotes, the way Claude Code applies Bash permission rules to
    /// each subcommand independently. A quoted operator (<c>echo "a &amp;&amp; b"</c>) does not split. Redirections
    /// (<c>&gt;</c>, <c>2&gt;&amp;1</c>) stay part of their command.
    /// </summary>
    public static class ShellCommandSplitter
    {
        #region Public-Methods

        /// <summary>
        /// Split a command line.
        /// </summary>
        /// <param name="command">Command line, or null.</param>
        /// <returns>The split; never null.</returns>
        public static ShellCommandSplit Split(string? command)
        {
            ShellCommandSplit result = new ShellCommandSplit();
            if (String.IsNullOrWhiteSpace(command)) return result;

            string text = command!;
            StringBuilder current = new StringBuilder();
            bool inSingle = false;
            bool inDouble = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (inSingle)
                {
                    current.Append(c);
                    if (c == '\'') inSingle = false;
                    continue;
                }

                if (c == '\\' && i + 1 < text.Length)
                {
                    // An escaped character never starts an operator or a quote.
                    current.Append(c);
                    current.Append(next);
                    i++;
                    continue;
                }

                if (inDouble)
                {
                    if (c == '"') inDouble = false;
                    else if (c == '`' || (c == '$' && next == '(')) result.HasSubstitution = true;
                    current.Append(c);
                    continue;
                }

                if (c == '\'') { inSingle = true; current.Append(c); continue; }
                if (c == '"') { inDouble = true; current.Append(c); continue; }
                if (c == '`' || (c == '$' && next == '(') || ((c == '<' || c == '>') && next == '('))
                {
                    result.HasSubstitution = true;
                    current.Append(c);
                    continue;
                }

                if (c == '\n' || c == '\r' || c == ';')
                {
                    Flush(result, current);
                    continue;
                }

                if (c == '&')
                {
                    // "&>" and ">&" are redirections, not operators.
                    bool previousIsRedirect = current.Length > 0 && (current[current.Length - 1] == '>' || current[current.Length - 1] == '<');
                    if (previousIsRedirect || next == '>')
                    {
                        current.Append(c);
                        continue;
                    }

                    Flush(result, current);
                    if (next == '&') i++;
                    continue;
                }

                if (c == '|')
                {
                    bool previousIsRedirect = current.Length > 0 && current[current.Length - 1] == '>';
                    if (previousIsRedirect)
                    {
                        // ">|" is a redirection (noclobber override).
                        current.Append(c);
                        continue;
                    }

                    Flush(result, current);
                    if (next == '|' || next == '&') i++;
                    continue;
                }

                current.Append(c);
            }

            if (inSingle || inDouble) result.Unbalanced = true;
            Flush(result, current);
            return result;
        }

        #endregion

        #region Private-Methods

        private static void Flush(ShellCommandSplit result, StringBuilder current)
        {
            string segment = current.ToString().Trim();
            current.Clear();
            if (segment.Length > 0) result.Commands.Add(segment);
        }

        #endregion
    }
}
