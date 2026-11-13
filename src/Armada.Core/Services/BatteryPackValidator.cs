namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Validates a battery pack manifest against the schema in battery/README.md: required fields, identifiers, known
    /// personas, files that exist inside the pack, and the fields each grader type needs.
    /// </summary>
    public static class BatteryPackValidator
    {
        #region Public-Members

        /// <summary>
        /// The only manifest schema version defined.
        /// </summary>
        public const int SchemaVersion = 1;

        /// <summary>
        /// The schema check a Rubric grader can apply to the output first.
        /// </summary>
        public const string ArmadaPlanSchema = "armada-plan";

        /// <summary>
        /// Personas a task may test.
        /// </summary>
        public static readonly IReadOnlyList<string> Personas = new List<string>
        {
            PersonaCatalog.Worker, PersonaCatalog.Architect, PersonaCatalog.ProductManager, PersonaCatalog.UsabilityEngineer,
            PersonaCatalog.TestEngineer, PersonaCatalog.Judge, PersonaCatalog.Linter, PersonaCatalog.Recorder
        };

        /// <summary>
        /// Judge verdicts a SeededSet grader may expect.
        /// </summary>
        public static readonly IReadOnlyList<string> Verdicts = new List<string> { "PASS", "FAIL", "NEEDS_REVISION", "SPEC_CONFLICT" };

        #endregion

        #region Private-Members

        private static readonly Regex _Identifier = new Regex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex _Branch = new Regex("^[A-Za-z0-9][A-Za-z0-9._/-]{0,99}$", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a manifest.
        /// </summary>
        /// <param name="manifest">Manifest.</param>
        /// <param name="packDirectory">Absolute pack directory (files are resolved against it).</param>
        /// <returns>Errors, at most <see cref="CollectionLimits.MaxConfigItems"/> (the last one says how many more were
        /// found); empty when the manifest is valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when manifest or packDirectory is null.</exception>
        public static List<string> Validate(BatteryPackManifest manifest, string packDirectory)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (packDirectory == null) throw new ArgumentNullException(nameof(packDirectory));

            // Every list in a manifest is a configuration list (docs/ENUMERATION.md): a pack over the limit is malformed
            // and is not validated further, so its errors stay bounded too.
            try
            {
                CollectionBounds.Validate(manifest);
            }
            catch (CollectionLimitExceededException limit)
            {
                return new List<string> { limit.Field + " has " + limit.Actual + " entries; the most allowed is " + limit.Limit + "." };
            }

            return Cap(ValidateWithinLimits(manifest, packDirectory));
        }

        private static List<string> ValidateWithinLimits(BatteryPackManifest manifest, string packDirectory)
        {
            List<string> errors = new List<string>();

            if (manifest.SchemaVersion != SchemaVersion)
                errors.Add("schemaVersion must be " + SchemaVersion + " (was " + manifest.SchemaVersion + ").");
            if (String.IsNullOrWhiteSpace(manifest.Id)) errors.Add("id is required.");
            else if (!_Identifier.IsMatch(manifest.Id)) errors.Add("id '" + manifest.Id + "' must be lowercase letters, digits and dashes.");
            if (String.IsNullOrWhiteSpace(manifest.Name)) errors.Add("name is required.");
            if (manifest.Version < 1) errors.Add("version must be 1 or more (was " + manifest.Version + ").");

            if (manifest.Fixture == null)
            {
                errors.Add("fixture is required.");
            }
            else
            {
                CheckDirectory(errors, packDirectory, manifest.Fixture.Path, "fixture.path", true);
                if (!_Branch.IsMatch(manifest.Fixture.DefaultBranch ?? String.Empty))
                    errors.Add("fixture.defaultBranch '" + manifest.Fixture.DefaultBranch + "' is not a valid branch name.");
            }

            HashSet<string> toolchain = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.Toolchain.Count; i++)
            {
                BatteryToolchainRequirement? tool = manifest.Toolchain[i];
                string where = "toolchain[" + i + "]";
                if (tool == null) { errors.Add(where + " is null."); continue; }
                if (String.IsNullOrWhiteSpace(tool.Name)) errors.Add(where + ".name is required.");
                else if (!toolchain.Add(tool.Name)) errors.Add(where + ".name '" + tool.Name + "' is used twice.");
                if (String.IsNullOrWhiteSpace(tool.Executable)) errors.Add(where + ".executable is required.");
            }

            if (manifest.Tasks.Count == 0) errors.Add("tasks must list at least one task.");
            HashSet<string> taskIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.Tasks.Count; i++)
            {
                BatteryTaskDefinition? task = manifest.Tasks[i];
                if (task == null) { errors.Add("tasks[" + i + "] is null."); continue; }
                string where = "tasks[" + i + "]" + (String.IsNullOrWhiteSpace(task.Id) ? String.Empty : " (" + task.Id + ")");
                ValidateTask(errors, task, where, packDirectory, toolchain, taskIds);
            }

            return errors;
        }

        /// <summary>
        /// Resolve a path relative to a directory, refusing empty, rooted and escaping paths.
        /// </summary>
        /// <param name="directory">Base directory.</param>
        /// <param name="relativePath">Relative path.</param>
        /// <returns>The absolute path, or null when the path is not inside the directory.</returns>
        public static string? ResolveInside(string directory, string? relativePath)
        {
            if (String.IsNullOrWhiteSpace(directory) || !IsSafeRelativePath(relativePath)) return null;
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, relativePath!));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
            return full;
        }

        /// <summary>
        /// Whether a path is relative and never steps outside its base (no rooted path, no ".." segment).
        /// </summary>
        /// <param name="relativePath">Path.</param>
        /// <returns>True when safe.</returns>
        public static bool IsSafeRelativePath(string? relativePath)
        {
            if (String.IsNullOrWhiteSpace(relativePath)) return false;
            if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal) || relativePath.StartsWith("\\", StringComparison.Ordinal))
                return false;
            string[] segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 && !segments.Any(s => s == "..");
        }

        #endregion

        #region Private-Methods

        private static List<string> Cap(List<string> errors)
        {
            if (errors.Count <= CollectionLimits.MaxConfigItems) return errors;
            List<string> capped = errors.Take(CollectionLimits.MaxConfigItems - 1).ToList();
            capped.Add("... and " + (errors.Count - capped.Count) + " more errors.");
            return capped;
        }

        private static void ValidateTask(List<string> errors, BatteryTaskDefinition task, string where, string packDirectory, HashSet<string> toolchain, HashSet<string> taskIds)
        {
            if (String.IsNullOrWhiteSpace(task.Id)) errors.Add(where + ".id is required.");
            else if (!_Identifier.IsMatch(task.Id)) errors.Add(where + ".id must be lowercase letters, digits and dashes.");
            else if (!taskIds.Add(task.Id)) errors.Add(where + ".id is used twice.");
            if (String.IsNullOrWhiteSpace(task.Title)) errors.Add(where + ".title is required.");
            if (String.IsNullOrWhiteSpace(task.Prompt)) errors.Add(where + ".prompt is required.");
            if (String.IsNullOrWhiteSpace(task.Persona)) errors.Add(where + ".persona is required.");
            else if (!Personas.Any(p => PersonaCatalog.Matches(p, task.Persona)))
                errors.Add(where + ".persona '" + task.Persona + "' is not one of " + String.Join(", ", Personas) + ".");

            for (int i = 0; i < task.Attachments.Count; i++)
                CheckFile(errors, packDirectory, task.Attachments[i], where + ".attachments[" + i + "]");
            if (task.Setup != null) CheckDirectory(errors, packDirectory, task.Setup, where + ".setup", false);
            if (task.Toolchain != null)
            {
                foreach (string name in task.Toolchain)
                {
                    if (String.IsNullOrWhiteSpace(name) || !toolchain.Contains(name))
                        errors.Add(where + ".toolchain names '" + name + "', which is not in the pack's toolchain.");
                }
            }

            if (task.TimeoutMinutes.HasValue && (task.TimeoutMinutes.Value < 1 || task.TimeoutMinutes.Value > 1440))
                errors.Add(where + ".timeoutMinutes must be 1 to 1440.");

            BatteryGraderDefinition? grader = task.Grader;
            if (grader == null) { errors.Add(where + ".grader is required."); return; }
            if (!grader.Type.HasValue || !Enum.IsDefined(typeof(BatteryGraderTypeEnum), grader.Type.Value))
            {
                errors.Add(where + ".grader.type is required (command, byteComparison, seededSet, mutation or rubric).");
                return;
            }

            string g = where + ".grader";
            switch (grader.Type.Value)
            {
                case BatteryGraderTypeEnum.Command:
                    CheckCommands(errors, grader, g);
                    if (grader.Overlay != null) CheckDirectory(errors, packDirectory, grader.Overlay, g + ".overlay", false);
                    break;
                case BatteryGraderTypeEnum.ByteComparison:
                    if (grader.Files.Count == 0) errors.Add(g + ".files must list at least one file.");
                    for (int i = 0; i < grader.Files.Count; i++)
                    {
                        BatteryExpectedFile? file = grader.Files[i];
                        if (file == null) { errors.Add(g + ".files[" + i + "] is null."); continue; }
                        if (!IsSafeRelativePath(file.Path)) errors.Add(g + ".files[" + i + "].path must be a relative path inside the worktree.");
                        CheckFile(errors, packDirectory, file.Expected, g + ".files[" + i + "].expected");
                    }
                    break;
                case BatteryGraderTypeEnum.SeededSet:
                    ValidateSeededSet(errors, grader, g);
                    break;
                case BatteryGraderTypeEnum.Mutation:
                    CheckCommands(errors, grader, g);
                    if (grader.Overlay != null) CheckDirectory(errors, packDirectory, grader.Overlay, g + ".overlay", false);
                    if (grader.Mutants.Count == 0) errors.Add(g + ".mutants must list at least one mutant.");
                    HashSet<string> mutantIds = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < grader.Mutants.Count; i++)
                    {
                        BatteryMutant? mutant = grader.Mutants[i];
                        if (mutant == null) { errors.Add(g + ".mutants[" + i + "] is null."); continue; }
                        if (String.IsNullOrWhiteSpace(mutant.Id) || !mutantIds.Add(mutant.Id)) errors.Add(g + ".mutants[" + i + "].id is required and must be unique.");
                        if (!IsSafeRelativePath(mutant.File)) errors.Add(g + ".mutants[" + i + "].file must be a relative path inside the worktree.");
                        if (String.IsNullOrEmpty(mutant.Find)) errors.Add(g + ".mutants[" + i + "].find is required.");
                        if (mutant.Find != null && mutant.Replace != null && String.Equals(mutant.Find, mutant.Replace, StringComparison.Ordinal))
                            errors.Add(g + ".mutants[" + i + "].replace must differ from find.");
                    }
                    break;
                case BatteryGraderTypeEnum.Rubric:
                    CheckFile(errors, packDirectory, grader.Rubric, g + ".rubric");
                    if (grader.Schema != null && !String.Equals(grader.Schema, ArmadaPlanSchema, StringComparison.Ordinal))
                        errors.Add(g + ".schema '" + grader.Schema + "' is unknown (the only schema is " + ArmadaPlanSchema + ").");
                    break;
            }
        }

        private static void ValidateSeededSet(List<string> errors, BatteryGraderDefinition grader, string g)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < grader.Seeded.Count; i++)
            {
                BatterySeededItem? item = grader.Seeded[i];
                if (item == null) { errors.Add(g + ".seeded[" + i + "] is null."); continue; }
                if (String.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)) errors.Add(g + ".seeded[" + i + "].id is required and must be unique.");
                if (item.Keywords.Count == 0 || item.Keywords.Any(k => String.IsNullOrWhiteSpace(k)))
                    errors.Add(g + ".seeded[" + i + "].keywords must list at least one non-empty phrase.");
            }

            if (grader.Seeded.Count == 0 && grader.ExpectedVerdicts.Count == 0)
                errors.Add(g + " needs seeded items or expectedVerdicts (a positive control expects a verdict).");
            if (grader.MaxFalsePositives < 0) errors.Add(g + ".maxFalsePositives must be 0 or more.");
            if (grader.MinMatched.HasValue && (grader.MinMatched.Value < 0 || grader.MinMatched.Value > grader.Seeded.Count))
                errors.Add(g + ".minMatched must be 0 to the number of seeded items.");
            foreach (string verdict in grader.ExpectedVerdicts)
            {
                if (!Verdicts.Contains(verdict ?? String.Empty, StringComparer.Ordinal))
                    errors.Add(g + ".expectedVerdicts '" + verdict + "' is not one of " + String.Join(", ", Verdicts) + ".");
            }

            foreach (string section in grader.BlockingSections)
            {
                if (String.IsNullOrWhiteSpace(section)) errors.Add(g + ".blockingSections has an empty heading.");
            }
        }

        private static void CheckCommands(List<string> errors, BatteryGraderDefinition grader, string g)
        {
            if (grader.Commands.Count == 0) errors.Add(g + ".commands must list at least one command.");
            for (int i = 0; i < grader.Commands.Count; i++)
            {
                BatteryCommand? command = grader.Commands[i];
                if (command == null) { errors.Add(g + ".commands[" + i + "] is null."); continue; }
                if (String.IsNullOrWhiteSpace(command.Executable)) errors.Add(g + ".commands[" + i + "].executable is required.");
                if (command.TimeoutSeconds.HasValue && (command.TimeoutSeconds.Value < 1 || command.TimeoutSeconds.Value > 7200))
                    errors.Add(g + ".commands[" + i + "].timeoutSeconds must be 1 to 7200.");
            }
        }

        private static void CheckFile(List<string> errors, string packDirectory, string? relativePath, string where)
        {
            string? full = ResolveInside(packDirectory, relativePath);
            if (full == null) errors.Add(where + " must be a relative path inside the pack (was '" + relativePath + "').");
            else if (!File.Exists(full)) errors.Add(where + " '" + relativePath + "' does not exist.");
        }

        private static void CheckDirectory(List<string> errors, string packDirectory, string? relativePath, string where, bool mustHaveFiles)
        {
            string? full = ResolveInside(packDirectory, relativePath);
            if (full == null) errors.Add(where + " must be a relative path inside the pack (was '" + relativePath + "').");
            else if (!Directory.Exists(full)) errors.Add(where + " '" + relativePath + "' is not a directory.");
            else if (mustHaveFiles && Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length == 0)
                errors.Add(where + " '" + relativePath + "' is empty.");
        }

        #endregion
    }
}
