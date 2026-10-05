namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Parses the Prometheus text exposition format into samples so tests match metric names and label values exactly
    /// instead of searching the scrape text.
    /// </summary>
    public static class PrometheusTextParser
    {
        #region Public-Methods

        /// <summary>
        /// Parse every sample line; comment (# HELP, # TYPE) and blank lines are skipped.
        /// </summary>
        /// <param name="text">Scrape body.</param>
        /// <returns>Samples in order.</returns>
        /// <exception cref="FormatException">Thrown for a malformed label set.</exception>
        public static List<PrometheusTextSample> Parse(string? text)
        {
            List<PrometheusTextSample> result = new List<PrometheusTextSample>();
            if (String.IsNullOrEmpty(text)) return result;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                PrometheusTextSample sample = new PrometheusTextSample();
                int i = 0;
                while (i < line.Length && line[i] != '{' && line[i] != ' ') i++;
                sample.Name = line.Substring(0, i);
                if (i < line.Length && line[i] == '{') i = ParseLabels(line, i + 1, sample.Labels);
                string rest = line.Substring(Math.Min(i, line.Length)).Trim();
                int space = rest.IndexOf(' ');
                sample.Value = space >= 0 ? rest.Substring(0, space) : rest;
                result.Add(sample);
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static int ParseLabels(string line, int i, Dictionary<string, string> labels)
        {
            while (i < line.Length)
            {
                while (i < line.Length && (line[i] == ' ' || line[i] == ',')) i++;
                if (i < line.Length && line[i] == '}') return i + 1;
                int eq = line.IndexOf('=', i);
                if (eq < 0 || eq + 1 >= line.Length || line[eq + 1] != '"') throw new FormatException("Malformed label set: " + line);
                string name = line.Substring(i, eq - i).Trim();
                StringBuilder value = new StringBuilder();
                int j = eq + 2;
                while (j < line.Length && line[j] != '"')
                {
                    if (line[j] == '\\' && j + 1 < line.Length)
                    {
                        char next = line[j + 1];
                        value.Append(next == 'n' ? '\n' : next);
                        j += 2;
                        continue;
                    }

                    value.Append(line[j]);
                    j++;
                }

                if (j >= line.Length) throw new FormatException("Unterminated label value: " + line);
                labels[name] = value.ToString();
                i = j + 1;
            }

            throw new FormatException("Unterminated label set: " + line);
        }

        #endregion
    }
}
