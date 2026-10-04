namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// <see cref="IClock"/> over <see cref="DateTime.UtcNow"/>. Thread-safe.
    /// </summary>
    public class SystemClock : IClock
    {
        #region Public-Members

        /// <inheritdoc />
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }

        #endregion
    }
}
