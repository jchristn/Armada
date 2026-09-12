namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The classification of a durable agent memory. Working memory (the live context window, loaded files,
    /// and recent tool results) is deliberately absent: it is transient and never persisted. Only the three
    /// durable categories are stored.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MemoryTypeEnum
    {
        /// <summary>
        /// What happened and when: events, actions taken, and logs of key decisions and their reasoning.
        /// </summary>
        Episodic,

        /// <summary>
        /// Facts stripped of their episode, e.g. "Joel dislikes use of var in C# code".
        /// </summary>
        Semantic,

        /// <summary>
        /// Knowledge about how to do things: skills, workflows, checklists, task lists, and common groups of
        /// action items.
        /// </summary>
        Procedural
    }
}
