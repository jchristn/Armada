namespace Armada.Core.Harbor
{
    /// <summary>
    /// Harbor-to-server event: a chunk of captain output on standard output or standard error.
    /// </summary>
    public class HarborOutput : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job identifier the output belongs to.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Which standard stream the chunk came from.
        /// </summary>
        public HarborOutputStreamEnum Stream { get; set; } = HarborOutputStreamEnum.Stdout;

        /// <summary>
        /// The output chunk.
        /// </summary>
        public string Data { get; set; } = string.Empty;

        #endregion
    }
}
