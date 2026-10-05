namespace Armada.Tui.Screens.Ask
{
    using System;

    /// <summary>
    /// A laid-out message block kept by <see cref="AskTranscriptBuilder"/> with the signature of everything it was built
    /// from, so unchanged messages are not laid out again (W8.5). Immutable.
    /// </summary>
    public class AskBlockCacheEntry
    {
        #region Public-Members

        /// <summary>
        /// Signature of the inputs the block was built from.
        /// </summary>
        public string Signature { get; }

        /// <summary>
        /// The block, or null when the message renders nothing.
        /// </summary>
        public AskBlock? Block { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="signature">Signature.</param>
        /// <param name="block">Block or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="signature"/> is null.</exception>
        public AskBlockCacheEntry(string signature, AskBlock? block)
        {
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
            Block = block;
        }

        #endregion
    }
}
