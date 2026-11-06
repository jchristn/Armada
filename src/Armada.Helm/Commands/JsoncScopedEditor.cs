namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Replaces or removes one direct JSONC member, or one named object in a direct array, and leaves every other byte in place.
    /// </summary>
    internal static class JsoncScopedEditor
    {
        private static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Reject a document that is not one JSONC object.
        /// </summary>
        /// <param name="source">File text without a leading BOM.</param>
        internal static void Validate(string source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ParseDocument(source);
            JsonNode? node = JsonNode.Parse(source, null, ParseOptions);
            if (node is not JsonObject)
                throw new JsonException("JSONC root must be an object.");
        }

        /// <summary>
        /// Add or replace one direct property inside a direct object member.
        /// </summary>
        /// <param name="source">File text. Blank text creates a new object.</param>
        /// <param name="containerName">Direct object member that holds the property.</param>
        /// <param name="propertyName">Managed property name.</param>
        /// <param name="value">JSON text of the managed value.</param>
        /// <param name="changed">True when the returned text differs.</param>
        /// <returns>The edited text.</returns>
        internal static string UpsertObjectMember(string source, string containerName, string propertyName, string value, out bool changed)
        {
            if (String.IsNullOrWhiteSpace(source))
            {
                changed = true;
                return "{\n  \"" + containerName + "\": {\n    \"" + propertyName + "\": " + value + "\n  }\n}\n";
            }

            Validate(source);
            Document document = ParseDocument(source);
            MemberRange? containerMember = FindDirectMember(document.Root, containerName);
            if (containerMember == null)
            {
                string containerValue = "{\n" + ChildIndent(source, document.Root.Open) + "  \"" + propertyName + "\": " + value + "\n" + ParentIndent(source, document.Root.Open) + "}";
                return InsertObjectMember(source, document.Root, containerName, containerValue, out changed);
            }

            if (containerMember.ValueStart >= source.Length || source[containerMember.ValueStart] != '{')
                throw new JsonException("JSONC container '" + containerName + "' must be an object.");

            ObjectRange container = ParseObject(source, containerMember.ValueStart);
            MemberRange? managed = FindDirectMember(container, propertyName);
            if (managed == null)
                return InsertObjectMember(source, container, propertyName, value, out changed);

            string existing = source.Substring(managed.ValueStart, managed.ValueEnd - managed.ValueStart);
            if (SameJson(existing, value))
            {
                changed = false;
                return source;
            }

            changed = true;
            return source.Substring(0, managed.ValueStart) + value + source.Substring(managed.ValueEnd);
        }

        /// <summary>
        /// Remove one direct property inside a direct object member.
        /// </summary>
        /// <param name="source">File text.</param>
        /// <param name="containerName">Direct object member that holds the property.</param>
        /// <param name="propertyName">Managed property name.</param>
        /// <param name="changed">True when the returned text differs.</param>
        /// <returns>The edited text.</returns>
        internal static string RemoveObjectMember(string source, string containerName, string propertyName, out bool changed)
        {
            if (String.IsNullOrWhiteSpace(source))
            {
                changed = false;
                return source;
            }

            Validate(source);
            Document document = ParseDocument(source);
            MemberRange? containerMember = FindDirectMember(document.Root, containerName);
            if (containerMember == null)
            {
                changed = false;
                return source;
            }

            if (containerMember.ValueStart >= source.Length || source[containerMember.ValueStart] != '{')
                throw new JsonException("JSONC container '" + containerName + "' must be an object.");

            ObjectRange container = ParseObject(source, containerMember.ValueStart);
            MemberRange? managed = FindDirectMember(container, propertyName);
            if (managed == null)
            {
                changed = false;
                return source;
            }

            changed = true;
            return RemoveMemberText(source, managed);
        }

        /// <summary>
        /// Add or replace the one object in a direct array whose <c>name</c> property matches.
        /// </summary>
        /// <param name="source">File text. Blank text creates a new object.</param>
        /// <param name="containerName">Direct array member.</param>
        /// <param name="objectName">Value of the object's <c>name</c> property.</param>
        /// <param name="value">JSON text of the managed object.</param>
        /// <param name="changed">True when the returned text differs.</param>
        /// <returns>The edited text.</returns>
        internal static string UpsertArrayObject(string source, string containerName, string objectName, string value, out bool changed)
        {
            if (String.IsNullOrWhiteSpace(source))
            {
                changed = true;
                return "{\n  \"" + containerName + "\": [\n    " + value + "\n  ]\n}\n";
            }

            Validate(source);
            Document document = ParseDocument(source);
            MemberRange? containerMember = FindDirectMember(document.Root, containerName);
            if (containerMember == null)
            {
                string containerValue = "[\n" + ChildIndent(source, document.Root.Open) + "  " + value + "\n" + ParentIndent(source, document.Root.Open) + "]";
                return InsertObjectMember(source, document.Root, containerName, containerValue, out changed);
            }

            if (containerMember.ValueStart >= source.Length || source[containerMember.ValueStart] != '[')
                throw new JsonException("JSONC container '" + containerName + "' must be an array.");

            ArrayRange array = ParseArray(source, containerMember.ValueStart);
            ArrayElementRange? match = FindNamedArrayElement(source, array, objectName);
            if (match == null)
                return InsertArrayElement(source, array, value, out changed);

            string existing = source.Substring(match.ValueStart, match.ValueEnd - match.ValueStart);
            if (SameJson(existing, value))
            {
                changed = false;
                return source;
            }

            changed = true;
            return source.Substring(0, match.ValueStart) + value + source.Substring(match.ValueEnd);
        }

        /// <summary>
        /// Remove the one object in a direct array whose <c>name</c> property matches.
        /// </summary>
        /// <param name="source">File text.</param>
        /// <param name="containerName">Direct array member.</param>
        /// <param name="objectName">Value of the object's <c>name</c> property.</param>
        /// <param name="changed">True when the returned text differs.</param>
        /// <returns>The edited text.</returns>
        internal static string RemoveArrayObject(string source, string containerName, string objectName, out bool changed)
        {
            if (String.IsNullOrWhiteSpace(source))
            {
                changed = false;
                return source;
            }

            Validate(source);
            Document document = ParseDocument(source);
            MemberRange? containerMember = FindDirectMember(document.Root, containerName);
            if (containerMember == null)
            {
                changed = false;
                return source;
            }

            if (containerMember.ValueStart >= source.Length || source[containerMember.ValueStart] != '[')
                throw new JsonException("JSONC container '" + containerName + "' must be an array.");

            ArrayRange array = ParseArray(source, containerMember.ValueStart);
            ArrayElementRange? match = FindNamedArrayElement(source, array, objectName);
            if (match == null)
            {
                changed = false;
                return source;
            }

            changed = true;
            return RemoveSpan(source, LineStartIfIndented(source, match.ValueStart), EndOfInsertedLine(source, match.ValueEnd), match.CommaIndex);
        }

        private static bool SameJson(string existing, string value)
        {
            if (String.Equals(existing, value, StringComparison.Ordinal))
                return true;

            try
            {
                JsonNode? left = JsonNode.Parse(existing, null, ParseOptions);
                JsonNode? right = JsonNode.Parse(value, null, ParseOptions);
                return JsonNode.DeepEquals(left, right);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static ArrayElementRange? FindNamedArrayElement(string source, ArrayRange array, string objectName)
        {
            ArrayElementRange? found = null;
            foreach (ArrayElementRange element in array.Elements)
            {
                if (element.ValueStart >= source.Length || source[element.ValueStart] != '{')
                    continue;

                ObjectRange item = ParseObject(source, element.ValueStart);
                MemberRange? nameMember = FindDirectMember(item, "name");
                if (nameMember == null)
                    continue;

                string? name = ReadStringValue(source, nameMember.ValueStart, nameMember.ValueEnd);
                if (!String.Equals(name, objectName, StringComparison.Ordinal))
                    continue;

                if (found != null)
                    throw new InvalidOperationException("JSONC array entry '" + objectName + "' is ambiguous.");

                found = element;
            }

            return found;
        }

        private static string InsertObjectMember(string source, ObjectRange container, string key, string value, out bool changed)
        {
            if (FindDirectMember(container, key) != null)
                throw new InvalidOperationException("JSONC object entry '" + key + "' is ambiguous.");

            MemberRange? last = container.Members.Count == 0 ? null : container.Members[container.Members.Count - 1];
            StringBuilder result = new StringBuilder(source);
            int close = container.Close;
            if (last != null && last.CommaIndex < 0)
            {
                result.Insert(last.ValueEnd, ',');
                close++;
            }

            string indent = last == null ? ChildIndent(source, container.Open) : LineIndent(source, last.Start);
            result.Insert(close, BuildInsertion(source, close, indent, "\"" + key + "\": " + value));
            changed = true;
            return result.ToString();
        }

        private static string InsertArrayElement(string source, ArrayRange array, string value, out bool changed)
        {
            ArrayElementRange? last = array.Elements.Count == 0 ? null : array.Elements[array.Elements.Count - 1];
            StringBuilder result = new StringBuilder(source);
            int close = array.Close;
            if (last != null && last.CommaIndex < 0)
            {
                result.Insert(last.ValueEnd, ',');
                close++;
            }

            string indent = last == null ? ChildIndent(source, array.Open) : LineIndent(source, last.ValueStart);
            result.Insert(close, BuildInsertion(source, close, indent, value));
            changed = true;
            return result.ToString();
        }

        private static string RemoveMemberText(string source, MemberRange member)
        {
            return RemoveSpan(source, LineStartIfIndented(source, member.Start), EndOfInsertedLine(source, member.ValueEnd), member.CommaIndex);
        }

        private static string RemoveSpan(string source, int removeStart, int removeEnd, int commaIndex)
        {
            if (commaIndex >= removeStart && commaIndex < removeEnd)
                return source.Remove(removeStart, removeEnd - removeStart);

            StringBuilder result = new StringBuilder(source);
            if (commaIndex >= removeEnd)
            {
                result.Remove(removeStart, removeEnd - removeStart);
                result.Remove(commaIndex - (removeEnd - removeStart), 1);
                return result.ToString();
            }

            if (commaIndex >= 0)
            {
                result.Remove(commaIndex, 1);
                result.Remove(removeStart - 1, removeEnd - removeStart);
                return result.ToString();
            }

            result.Remove(removeStart, removeEnd - removeStart);
            return result.ToString();
        }

        private static int LineStartIfIndented(string source, int position)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            for (int i = lineStart; i < position; i++)
            {
                if (!Char.IsWhiteSpace(source[i]))
                    return position;
            }

            return lineStart;
        }

        private static int EndOfInsertedLine(string source, int position)
        {
            if (position + 1 < source.Length && source[position] == '\r' && source[position + 1] == '\n')
                return position + 2;

            if (position < source.Length && source[position] == '\n')
                return position + 1;

            return position;
        }

        private static string BuildInsertion(string source, int close, string indent, string content)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, close - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            bool onlyWhitespace = true;
            for (int i = lineStart; i < close; i++)
            {
                if (!Char.IsWhiteSpace(source[i]))
                {
                    onlyWhitespace = false;
                    break;
                }
            }

            string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            if (onlyWhitespace)
            {
                int existingIndentLength = close - lineStart;
                string prefix = existingIndentLength < indent.Length ? indent.Substring(existingIndentLength) : String.Empty;
                string closeIndent = source.Substring(lineStart, existingIndentLength);
                return prefix + content + newline + closeIndent;
            }

            return newline + indent + content + newline;
        }

        private static Document ParseDocument(string source)
        {
            int start = SkipTrivia(source, 0);
            if (start >= source.Length || source[start] != '{')
                throw new JsonException("JSONC root must be an object.");

            ObjectRange root = ParseObject(source, start);
            int after = SkipTrivia(source, root.Close + 1);
            if (after != source.Length)
                throw new JsonException("JSONC contains data after the root object.");

            return new Document(root);
        }

        private static ObjectRange ParseObject(string source, int open)
        {
            ObjectRange result = new ObjectRange(open, FindMatchingClose(source, open, '{', '}'));
            int cursor = SkipTrivia(source, open + 1);
            while (cursor < result.Close)
            {
                if (source[cursor] != '"')
                    throw new JsonException("JSONC object member must have a quoted name.");

                StringToken key = ReadString(source, cursor);
                int colon = SkipTrivia(source, key.End);
                if (colon >= result.Close || source[colon] != ':')
                    throw new JsonException("JSONC object member is missing a colon.");

                int valueStart = SkipTrivia(source, colon + 1);
                int valueEnd = ParseValue(source, valueStart, result.Close);
                MemberRange member = new MemberRange(key.Value, key.Start, valueStart, valueEnd);
                int separator = SkipTrivia(source, valueEnd);
                if (separator < result.Close && source[separator] == ',')
                {
                    member.CommaIndex = separator;
                    cursor = SkipTrivia(source, separator + 1);
                    result.Members.Add(member);
                    if (cursor >= result.Close)
                        break;
                }
                else
                {
                    result.Members.Add(member);
                    if (separator != result.Close)
                        throw new JsonException("JSONC object member is missing a comma.");

                    cursor = result.Close;
                }
            }

            return result;
        }

        private static ArrayRange ParseArray(string source, int open)
        {
            ArrayRange result = new ArrayRange(open, FindMatchingClose(source, open, '[', ']'));
            int cursor = SkipTrivia(source, open + 1);
            while (cursor < result.Close)
            {
                int valueStart = cursor;
                int valueEnd = ParseValue(source, valueStart, result.Close);
                ArrayElementRange element = new ArrayElementRange(valueStart, valueEnd);
                int separator = SkipTrivia(source, valueEnd);
                if (separator < result.Close && source[separator] == ',')
                {
                    element.CommaIndex = separator;
                    cursor = SkipTrivia(source, separator + 1);
                    result.Elements.Add(element);
                    if (cursor >= result.Close)
                        break;
                }
                else
                {
                    result.Elements.Add(element);
                    if (separator != result.Close)
                        throw new JsonException("JSONC array element is missing a comma.");

                    cursor = result.Close;
                }
            }

            return result;
        }

        private static int ParseValue(string source, int start, int limit)
        {
            if (start >= limit)
                throw new JsonException("JSONC value is missing.");

            if (source[start] == '"')
                return ReadString(source, start).End;

            if (source[start] == '{')
                return ParseObject(source, start).Close + 1;

            if (source[start] == '[')
                return ParseArray(source, start).Close + 1;

            int cursor = start;
            while (cursor < limit)
            {
                if (source[cursor] == ',' || source[cursor] == '}' || source[cursor] == ']')
                    break;

                if (source[cursor] == '/' && cursor + 1 < limit && (source[cursor + 1] == '/' || source[cursor + 1] == '*'))
                    break;

                if (Char.IsWhiteSpace(source[cursor]))
                    break;

                cursor++;
            }

            if (cursor == start)
                throw new JsonException("JSONC value is missing.");

            return cursor;
        }

        private static int FindMatchingClose(string source, int start, char open, char close)
        {
            int depth = 0;
            for (int i = start; i < source.Length; i++)
            {
                if (source[i] == '"')
                {
                    i = ReadString(source, i).End - 1;
                    continue;
                }

                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    int lineEnd = source.IndexOf('\n', i + 2);
                    i = lineEnd < 0 ? source.Length : lineEnd;
                    continue;
                }

                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    int commentEnd = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (commentEnd < 0)
                        throw new JsonException("JSONC block comment is unclosed.");

                    i = commentEnd + 1;
                    continue;
                }

                if (source[i] == open)
                {
                    depth++;
                }
                else if (source[i] == close)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            throw new JsonException("JSONC container is unclosed.");
        }

        private static StringToken ReadString(string source, int start)
        {
            if (source[start] != '"')
                throw new JsonException("JSONC string is missing its opening quote.");

            StringBuilder value = new StringBuilder();
            for (int i = start + 1; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '"')
                    return new StringToken(start, i + 1, value.ToString());

                if (c == '\\')
                {
                    if (++i >= source.Length)
                        throw new JsonException("JSONC string escape is unclosed.");

                    char escaped = source[i];
                    switch (escaped)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            if (i + 4 >= source.Length)
                                throw new JsonException("JSONC unicode escape is incomplete.");

                            int code = 0;
                            for (int digit = 1; digit <= 4; digit++)
                            {
                                int hex = HexValue(source[i + digit]);
                                if (hex < 0)
                                    throw new JsonException("JSONC unicode escape is invalid.");

                                code = (code * 16) + hex;
                            }

                            value.Append((char)code);
                            i += 4;
                            break;
                        default:
                            throw new JsonException("JSONC string escape is invalid.");
                    }

                    continue;
                }

                value.Append(c);
            }

            throw new JsonException("JSONC string is unclosed.");
        }

        private static string? ReadStringValue(string source, int start, int end)
        {
            if (start >= end || source[start] != '"')
                return null;

            StringToken token = ReadString(source, start);
            return token.End == end ? token.Value : null;
        }

        private static int SkipTrivia(string source, int start)
        {
            int cursor = start;
            while (cursor < source.Length)
            {
                if (Char.IsWhiteSpace(source[cursor]))
                {
                    cursor++;
                    continue;
                }

                if (source[cursor] == '/' && cursor + 1 < source.Length && source[cursor + 1] == '/')
                {
                    int lineEnd = source.IndexOf('\n', cursor + 2);
                    cursor = lineEnd < 0 ? source.Length : lineEnd + 1;
                    continue;
                }

                if (source[cursor] == '/' && cursor + 1 < source.Length && source[cursor + 1] == '*')
                {
                    int commentEnd = source.IndexOf("*/", cursor + 2, StringComparison.Ordinal);
                    if (commentEnd < 0)
                        throw new JsonException("JSONC block comment is unclosed.");

                    cursor = commentEnd + 2;
                    continue;
                }

                break;
            }

            return cursor;
        }

        private static MemberRange? FindDirectMember(ObjectRange container, string key)
        {
            MemberRange? found = null;
            foreach (MemberRange member in container.Members)
            {
                if (!String.Equals(member.Key, key, StringComparison.Ordinal))
                    continue;

                if (found != null)
                    throw new InvalidOperationException("JSONC object entry '" + key + "' is ambiguous.");

                found = member;
            }

            return found;
        }

        private static string ParentIndent(string source, int open)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, open - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int cursor = lineStart;
            while (cursor < open && (source[cursor] == ' ' || source[cursor] == '\t'))
                cursor++;

            return source.Substring(lineStart, cursor - lineStart);
        }

        private static string ChildIndent(string source, int open)
        {
            return ParentIndent(source, open) + "  ";
        }

        private static string LineIndent(string source, int position)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int cursor = lineStart;
            while (cursor < position && (source[cursor] == ' ' || source[cursor] == '\t'))
                cursor++;

            return source.Substring(lineStart, cursor - lineStart);
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private sealed class Document
        {
            internal Document(ObjectRange root)
            {
                Root = root;
            }

            internal ObjectRange Root { get; }
        }

        private sealed class ObjectRange
        {
            internal ObjectRange(int open, int close)
            {
                Open = open;
                Close = close;
            }

            internal int Open { get; }

            internal int Close { get; }

            internal List<MemberRange> Members { get; } = new List<MemberRange>();
        }

        private sealed class ArrayRange
        {
            internal ArrayRange(int open, int close)
            {
                Open = open;
                Close = close;
            }

            internal int Open { get; }

            internal int Close { get; }

            internal List<ArrayElementRange> Elements { get; } = new List<ArrayElementRange>();
        }

        private sealed class MemberRange
        {
            internal MemberRange(string key, int start, int valueStart, int valueEnd)
            {
                Key = key;
                Start = start;
                ValueStart = valueStart;
                ValueEnd = valueEnd;
                CommaIndex = -1;
            }

            internal string Key { get; }

            internal int Start { get; }

            internal int ValueStart { get; }

            internal int ValueEnd { get; }

            internal int CommaIndex { get; set; }
        }

        private sealed class ArrayElementRange
        {
            internal ArrayElementRange(int valueStart, int valueEnd)
            {
                ValueStart = valueStart;
                ValueEnd = valueEnd;
                CommaIndex = -1;
            }

            internal int ValueStart { get; }

            internal int ValueEnd { get; }

            internal int CommaIndex { get; set; }
        }

        private sealed class StringToken
        {
            internal StringToken(int start, int end, string value)
            {
                Start = start;
                End = end;
                Value = value;
            }

            internal int Start { get; }

            internal int End { get; }

            internal string Value { get; }
        }
    }
}
