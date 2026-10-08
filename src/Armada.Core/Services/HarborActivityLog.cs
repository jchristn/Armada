namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The Harbor app's activity log: a bounded list of rendered lines in which consecutive heartbeats collapse into
    /// one line, "09:36:01 -> Heartbeat (3 since 09:35:31)", so they do not crowd out the work being done.
    /// </summary>
    public class HarborActivityLog
    {
        #region Public-Members

        /// <summary>
        /// Number of lines currently held.
        /// </summary>
        public int Count
        {
            get { return _Lines.Count; }
        }

        #endregion

        #region Private-Members

        private readonly int _MaxLines;
        private readonly List<string> _Lines = new List<string>();
        private int _HeartbeatRun = 0;
        private DateTime _HeartbeatRunStartUtc = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="maxLines">Lines kept; older lines are dropped. Minimum 1.</param>
        public HarborActivityLog(int maxLines)
        {
            _MaxLines = maxLines < 1 ? 1 : maxLines;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add an entry. A heartbeat that follows a heartbeat replaces the previous line with a summary of the run.
        /// </summary>
        /// <param name="entry">Entry.</param>
        public void Add(HarborLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            if (entry.IsHeartbeat && _HeartbeatRun > 0 && _Lines.Count > 0)
            {
                _HeartbeatRun++;
                _Lines[_Lines.Count - 1] = entry.ToString() + " (" + _HeartbeatRun + " since "
                    + _HeartbeatRunStartUtc.ToLocalTime().ToString("HH:mm:ss") + ")";
                return;
            }

            if (entry.IsHeartbeat)
            {
                _HeartbeatRun = 1;
                _HeartbeatRunStartUtc = entry.TimestampUtc;
            }
            else
            {
                _HeartbeatRun = 0;
            }

            _Lines.Add(entry.ToString());
            if (_Lines.Count > _MaxLines) _Lines.RemoveRange(0, _Lines.Count - _MaxLines);
        }

        /// <summary>
        /// The most recent lines, oldest first.
        /// </summary>
        /// <param name="maxLines">Maximum number of lines.</param>
        /// <returns>Copy of the lines.</returns>
        public List<string> Recent(int maxLines)
        {
            int count = Math.Min(Math.Max(maxLines, 0), _Lines.Count);
            return _Lines.GetRange(_Lines.Count - count, count);
        }

        /// <summary>
        /// All lines joined with newlines.
        /// </summary>
        /// <returns>Text.</returns>
        public string ToText()
        {
            return String.Join("\n", _Lines);
        }

        /// <summary>
        /// Remove every line.
        /// </summary>
        public void Clear()
        {
            _Lines.Clear();
            _HeartbeatRun = 0;
        }

        #endregion
    }
}
