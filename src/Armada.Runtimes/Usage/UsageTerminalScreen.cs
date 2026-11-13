namespace Armada.Runtimes.Usage
{
    using System;
    using System.Text;
    /// <summary>Bounded 120 by 40 terminal screen for quota UI; OSC content is discarded.</summary>
    internal class UsageTerminalScreen
    {
        private readonly char[,] _Cells = new char[40, 120];
        private int _Row;
        private int _Column;
        private int _State;
        private readonly StringBuilder _Sequence = new StringBuilder();
        internal string Text
        {
            get
            {
                StringBuilder text = new StringBuilder();
                for (int row = 0; row < 40; row++)
                {
                    for (int col = 0; col < 120; col++) text.Append(_Cells[row, col] == '\0' ? ' ' : _Cells[row, col]);
                    text.Append('\n');
                }
                return text.ToString();
            }
        }
        internal void Append(char[] chars, int count)
        {
            for (int index = 0; index < count; index++)
            {
                char value = chars[index];
                if (_State == 3) { if (value == '\a') _State = 0; else if (value == '\u001b') _State = 4; continue; }
                if (_State == 4) { _State = value == '\\' ? 0 : 3; continue; }
                if (_State == 1) { _State = value == '[' ? 2 : value == ']' ? 3 : 0; _Sequence.Clear(); continue; }
                if (_State == 2)
                {
                    if (value >= '@' && value <= '~') { Apply(value); _State = 0; }
                    else if (_Sequence.Length < 64) _Sequence.Append(value);
                    else _State = 0;
                    continue;
                }
                if (value == '\u001b') { _State = 1; continue; }
                if (value == '\r') { _Column = 0; continue; }
                if (value == '\n') { NewLine(); continue; }
                if (value == '\b') { _Column = Math.Max(0, _Column - 1); continue; }
                if (Char.IsControl(value)) continue;
                _Cells[_Row, _Column] = value;
                if (++_Column >= 120) { _Column = 0; NewLine(); }
            }
        }
        private void NewLine()
        {
            if (_Row < 39) { _Row++; return; }
            for (int row = 0; row < 39; row++) for (int col = 0; col < 120; col++) _Cells[row, col] = _Cells[row + 1, col];
            for (int col = 0; col < 120; col++) _Cells[39, col] = ' ';
        }
        private void Apply(char command)
        {
            string[] parts = _Sequence.ToString().Split(';');
            int.TryParse(parts[0], out int first);
            int second = 1;
            if (parts.Length > 1) Int32.TryParse(parts[1], out second);
            int step = Math.Max(1, first);
            switch (command)
            {
                case 'H': case 'f': _Row = Math.Clamp(step - 1, 0, 39); _Column = Math.Clamp(second - 1, 0, 119); break;
                case 'A': _Row = Math.Max(0, _Row - step); break;
                case 'B': _Row = Math.Min(39, _Row + step); break;
                case 'C': _Column = Math.Min(119, _Column + step); break;
                case 'D': _Column = Math.Max(0, _Column - step); break;
                case 'G': _Column = Math.Clamp(step - 1, 0, 119); break;
                case 'J': if (first == 2 || first == 3) Array.Clear(_Cells); break;
                case 'K': for (int col = first == 2 || first == 1 ? 0 : _Column; col < (first == 1 ? _Column + 1 : 120); col++) _Cells[_Row, col] = ' '; break;
            }
        }
    }
}
