namespace Armada.Core.Services
{
    /// <summary>
    /// The <c>mission.merge_conflict_avoidance</c> section. The current text names the mission's owned paths
    /// (<c>{OwnedPaths}</c>), the paths owned by missions that can run at the same time (<c>{ConcurrentSiblingPaths}</c>)
    /// and its parent's result (<c>{ParentSummary}</c>); the previous default is kept so untouched copies upgrade.
    /// </summary>
    public static class MergeConflictPrompt
    {
        /// <summary>Current default content.</summary>
        public const string Default =
            "## Avoiding Merge Conflicts (CRITICAL)\n" +
            "\n" +
            "You are one of several captains working on this repository. Other captains may be working on " +
            "other missions in parallel on separate branches. To prevent merge conflicts and landing failures, " +
            "you MUST follow these rules:\n" +
            "\n" +
            "1. **Change only your owned paths:** {OwnedPaths}. Do not refactor other files even if you notice " +
            "improvements.\n" +
            "\n" +
            "2. **These paths belong to missions running in parallel; do not change them even if it looks helpful:** " +
            "{ConcurrentSiblingPaths}. If you cannot finish without changing them, stop and report it as a blocker.\n" +
            "{ParentSummary}" +
            "\n" +
            "3. **Do not make \"helpful\" changes outside your scope.** Do not rename shared variables, " +
            "reorganize imports in files you were not asked to touch, reformat code in unrelated files, " +
            "update documentation files unless instructed, or modify configuration/project files " +
            "(e.g., .csproj, package.json, tsconfig.json) unless your mission specifically requires it.\n" +
            "\n" +
            "4. **Do not modify barrel/index export files** (e.g., index.ts, mod.rs) unless you own them. " +
            "These are high-conflict files that many missions may need to touch.\n" +
            "\n" +
            "5. **Keep changes minimal and focused.** The fewer files you touch, the lower the risk of " +
            "conflicts.\n" +
            "\n" +
            "6. **If you must create new files**, put them under your owned paths and prefer names specific to " +
            "your mission's feature.\n" +
            "\n" +
            "7. **Do not modify or delete files created by another mission's branch.** You are working in " +
            "an isolated worktree -- if you see files that seem unrelated to your mission, leave them alone.\n" +
            "\n" +
            "Violating these rules will cause your branch to conflict with other captains' branches during " +
            "landing, resulting in a LandingFailed status and wasted work.\n";

        /// <summary>The default shipped before v1.3.0; an untouched copy upgrades to <see cref="Default"/>.</summary>
        public const string LegacyDefault =
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
            "landing, resulting in a LandingFailed status and wasted work.\n";

        /// <summary>Text for <c>{OwnedPaths}</c> when the mission has no recorded ownership.</summary>
        public const string NoOwnedPaths = "the files named in your mission description";

        /// <summary>Text for <c>{ConcurrentSiblingPaths}</c> when no concurrent mission owns paths.</summary>
        public const string NoConcurrentPaths = "none recorded";
    }
}
