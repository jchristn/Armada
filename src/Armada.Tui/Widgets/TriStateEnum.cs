namespace Armada.Tui.Widgets
{
    /// <summary>
    /// Value of a <see cref="TriStateField"/> (filters such as Active, Dirty, Unread only).
    /// </summary>
    public enum TriStateEnum
    {
        /// <summary>
        /// No filter.
        /// </summary>
        Any = 0,

        /// <summary>
        /// True.
        /// </summary>
        Yes = 1,

        /// <summary>
        /// False.
        /// </summary>
        No = 2
    }
}
