namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One section of a mission instructions file, in file order, as the <see cref="Armada.Core.Services.InstructionsComposer"/>
    /// assembles it. A section with a trim rank above zero may be replaced by its <see cref="TrimmedText"/> when the file
    /// exceeds the reader's budget; rank zero is never trimmed (the role and mission description, rules and the output
    /// contract).
    /// </summary>
    public class InstructionSection
    {
        #region Public-Members

        /// <summary>
        /// Short section name used in logs and in the list of trimmed sections, for example "skills".
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// The section text, including its heading and trailing newline.
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Trim rank: 0 is never trimmed; otherwise the section with the highest rank is trimmed first.
        /// </summary>
        public int TrimRank { get; set; } = 0;

        /// <summary>
        /// Text that replaces the section when it is trimmed (a one-line note), or empty to drop it.
        /// </summary>
        public string TrimmedText { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public InstructionSection()
        {
        }

        /// <summary>
        /// Instantiate a section.
        /// </summary>
        /// <param name="name">Section name.</param>
        /// <param name="text">Section text.</param>
        /// <param name="trimRank">Trim rank (0 is never trimmed).</param>
        /// <param name="trimmedText">Replacement text when trimmed.</param>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        public InstructionSection(string name, string text, int trimRank = 0, string trimmedText = "")
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            Name = name;
            Text = text ?? "";
            TrimRank = trimRank < 0 ? 0 : trimRank;
            TrimmedText = trimmedText ?? "";
        }

        #endregion
    }
}
