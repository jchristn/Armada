namespace Armada.Tui.Widgets
{
    /// <summary>
    /// Load state of an <see cref="ArmadaGrid{T}"/>.
    /// </summary>
    public enum GridStateEnum
    {
        /// <summary>
        /// Rows are shown.
        /// </summary>
        Ready = 0,

        /// <summary>
        /// A page is loading.
        /// </summary>
        Loading = 1,

        /// <summary>
        /// The last load failed.
        /// </summary>
        Error = 2
    }
}
