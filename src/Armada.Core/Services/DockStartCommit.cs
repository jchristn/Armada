namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using Armada.Core.Models;

    /// <summary>
    /// The commit a dock's worktree started from, durable in <see cref="Dock.GitAnchorsJson"/>
    /// (<see cref="GitAnchorsSnapshot.StartCommit"/>). DockService writes it at every provision, so it survives an
    /// Admiral restart, a lost log directory and a Harbor-hosted dock (whose ".start" file never existed on the Admiral).
    /// </summary>
    public static class DockStartCommit
    {
        #region Public-Methods

        /// <summary>
        /// Read the start commit recorded on a dock, or null when none is recorded or the snapshot cannot be read.
        /// </summary>
        /// <param name="dock">Dock, or null.</param>
        /// <returns>The start commit, or null.</returns>
        public static string? FromDock(Dock? dock)
        {
            GitAnchorsSnapshot? snapshot = Read(dock?.GitAnchorsJson);
            return String.IsNullOrWhiteSpace(snapshot?.StartCommit) ? null : snapshot!.StartCommit!.Trim();
        }

        /// <summary>
        /// Record a start commit on a dock's anchors snapshot, keeping every other anchor already recorded.
        /// </summary>
        /// <param name="dock">Dock to update in memory (the caller persists it).</param>
        /// <param name="startCommit">Commit the worktree started from.</param>
        /// <param name="targetBranch">Target branch, or null to keep the recorded one.</param>
        /// <exception cref="ArgumentNullException">Thrown when dock is null.</exception>
        public static void Write(Dock dock, string startCommit, string? targetBranch = null)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            if (String.IsNullOrWhiteSpace(startCommit)) return;
            GitAnchorsSnapshot snapshot = Read(dock.GitAnchorsJson) ?? new GitAnchorsSnapshot();
            snapshot.StartCommit = startCommit.Trim();
            if (!String.IsNullOrEmpty(targetBranch)) snapshot.TargetBranch = targetBranch;
            if (String.IsNullOrEmpty(snapshot.WorkingBranch)) snapshot.WorkingBranch = dock.BranchName;
            dock.GitAnchorsJson = JsonSerializer.Serialize(snapshot);
        }

        #endregion

        #region Private-Methods

        private static GitAnchorsSnapshot? Read(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<GitAnchorsSnapshot>(json!);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
