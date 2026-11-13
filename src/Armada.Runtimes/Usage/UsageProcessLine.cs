namespace Armada.Runtimes.Usage
{
    using System;

    /// <summary>
    /// One read from a <see cref="UsageProcessSession"/>: a complete line, or why there is none.
    /// </summary>
    public class UsageProcessLine
    {
        #region Public-Members

        /// <summary>
        /// What the read produced.
        /// </summary>
        public UsageProcessLineKindEnum Kind { get; set; } = UsageProcessLineKindEnum.EndOfStream;

        /// <summary>
        /// The line text when <see cref="Kind"/> is Line; empty otherwise.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageProcessLine()
        {
        }

        /// <summary>
        /// A complete line.
        /// </summary>
        /// <param name="text">Line text.</param>
        /// <returns>The read.</returns>
        public static UsageProcessLine Of(string text)
        {
            return new UsageProcessLine { Kind = UsageProcessLineKindEnum.Line, Text = text ?? String.Empty };
        }

        /// <summary>
        /// End of stream at a line boundary.
        /// </summary>
        /// <returns>The read.</returns>
        public static UsageProcessLine EndOfStream()
        {
            return new UsageProcessLine { Kind = UsageProcessLineKindEnum.EndOfStream };
        }

        /// <summary>
        /// End of stream inside a line.
        /// </summary>
        /// <returns>The read.</returns>
        public static UsageProcessLine Truncated()
        {
            return new UsageProcessLine { Kind = UsageProcessLineKindEnum.Truncated };
        }

        /// <summary>
        /// A byte cap was exceeded.
        /// </summary>
        /// <returns>The read.</returns>
        public static UsageProcessLine Oversize()
        {
            return new UsageProcessLine { Kind = UsageProcessLineKindEnum.Oversize };
        }

        #endregion
    }
}
