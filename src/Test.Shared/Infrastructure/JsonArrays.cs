namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Finds every array in a JSON document with <see cref="Utf8JsonReader"/>, for wire-shape guards (which collections
    /// a response really carries, at any depth). Assert values on a deserialized model instead.
    /// </summary>
    public static class JsonArrays
    {
        #region Public-Methods

        /// <summary>
        /// Every array in the document, in document order of where each ends.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Arrays.</returns>
        /// <exception cref="JsonException">Thrown when the text is not valid JSON.</exception>
        public static List<JsonArrayShape> Find(string json)
        {
            List<JsonArrayShape> result = new List<JsonArrayShape>();
            if (String.IsNullOrWhiteSpace(json)) return result;
            List<JsonArrayFrame> parents = new List<JsonArrayFrame>();

            Utf8JsonReader reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
            Stack<JsonArrayFrame> frames = new Stack<JsonArrayFrame>();
            string? pendingName = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        pendingName = reader.GetString() ?? "";
                        if (frames.Count > 0 && !frames.Peek().IsArray) frames.Peek().PropertyNames.Add(pendingName);
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        CountItem(frames);
                        JsonArrayFrame frame = new JsonArrayFrame();
                        frame.IsArray = reader.TokenType == JsonTokenType.StartArray;
                        frame.Name = pendingName ?? "[]";
                        frame.Parent = frames.Count > 0 ? frames.Peek() : null;
                        frame.Path = PathOf(frame.Parent, frame.Name);
                        frames.Push(frame);
                        pendingName = null;
                        break;
                    case JsonTokenType.EndObject:
                        frames.Pop();
                        break;
                    case JsonTokenType.EndArray:
                        JsonArrayFrame done = frames.Pop();
                        JsonArrayShape shape = new JsonArrayShape();
                        shape.Name = done.Name;
                        shape.Path = done.Path;
                        shape.Count = done.Count;
                        result.Add(shape);
                        parents.Add(done);
                        break;
                    default:
                        CountItem(frames);
                        pendingName = null;
                        break;
                }
            }

            // Decided after the whole document is read, so page properties after Objects count too.
            for (int i = 0; i < result.Count; i++)
            {
                JsonArrayFrame done = parents[i];
                result[i].IsPageObjects = String.Equals(done.Name, "Objects", StringComparison.OrdinalIgnoreCase)
                    && done.Parent != null
                    && !done.Parent.IsArray
                    && IsPage(done.Parent.PropertyNames);
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static void CountItem(Stack<JsonArrayFrame> frames)
        {
            if (frames.Count > 0 && frames.Peek().IsArray) frames.Peek().Count++;
        }

        private static string PathOf(JsonArrayFrame? parent, string name)
        {
            if (parent == null) return name == "[]" ? "" : name;
            if (parent.IsArray) return parent.Path + "[]";
            return parent.Path.Length == 0 ? name : parent.Path + "." + name;
        }

        private static bool IsPage(List<string> names)
        {
            HashSet<string> set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return set.Contains("PageNumber") && set.Contains("PageSize") && set.Contains("TotalRecords");
        }

        #endregion
    }
}
