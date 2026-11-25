namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Reads and writes a mission's notes (RELIABILITY_SIMPLICITY_PLAN.md RS-3.4). A mission's description is written
    /// once, when it is created; everything Armada adds later (handoffs, review feedback, repair, resume and escalation
    /// notes) is a note, and the text a captain receives is the description followed by the notes in the order they
    /// were last set.
    /// </summary>
    public static class MissionNotes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the note of a kind, replacing an earlier one of that kind and moving it to the end. An empty body removes
        /// the note.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="kind">Note kind.</param>
        /// <param name="body">Note text.</param>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static void Set(Mission mission, MissionNoteKindEnum kind, string? body)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            mission.Notes ??= new List<MissionNote>();
            mission.Notes.RemoveAll(n => n.Kind == kind);
            string trimmed = (body ?? String.Empty).Trim('\r', '\n');
            if (String.IsNullOrWhiteSpace(trimmed)) return;
            mission.Notes.Add(new MissionNote { Kind = kind, Body = trimmed, UpdatedUtc = DateTime.UtcNow });
        }

        /// <summary>
        /// Remove the note of a kind.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="kind">Note kind.</param>
        /// <returns>True when a note was removed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static bool Remove(Mission mission, MissionNoteKindEnum kind)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Notes == null) return false;
            return mission.Notes.RemoveAll(n => n.Kind == kind) > 0;
        }

        /// <summary>
        /// The text of the note of a kind.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="kind">Note kind.</param>
        /// <returns>The note text, or null when the mission has none of that kind.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static string? Get(Mission mission, MissionNoteKindEnum kind)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return mission.Notes?.FirstOrDefault(n => n.Kind == kind)?.Body;
        }

        /// <summary>
        /// The text a captain receives for a mission: its description followed by its notes.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <returns>The description and notes; empty when there are neither.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static string EffectiveDescription(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Notes == null || !mission.Notes.Any(n => !String.IsNullOrWhiteSpace(n.Body))) return mission.Description ?? String.Empty;
            StringBuilder text = new StringBuilder((mission.Description ?? String.Empty).TrimEnd());
            foreach (MissionNote note in mission.Notes)
            {
                if (String.IsNullOrWhiteSpace(note.Body)) continue;
                if (text.Length > 0) text.Append("\n\n");
                text.Append(note.Body);
            }

            return text.ToString();
        }

        /// <summary>
        /// Serialize notes for storage.
        /// </summary>
        /// <param name="notes">Notes, or null.</param>
        /// <returns>JSON, or null when there are no notes.</returns>
        public static string? ToJson(List<MissionNote>? notes)
        {
            if (notes == null || notes.Count == 0) return null;
            return JsonSerializer.Serialize(notes, _Json);
        }

        /// <summary>
        /// Read stored notes. Unreadable JSON yields no notes rather than failing the mission read.
        /// </summary>
        /// <param name="json">Stored JSON, or null.</param>
        /// <returns>The notes; never null.</returns>
        public static List<MissionNote> FromJson(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new List<MissionNote>();
            try
            {
                return JsonSerializer.Deserialize<List<MissionNote>>(json, _Json) ?? new List<MissionNote>();
            }
            catch (JsonException)
            {
                return new List<MissionNote>();
            }
        }

        #endregion
    }
}
