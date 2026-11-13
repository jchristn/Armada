namespace Armada.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>"--name value" arguments with typed getters. Every option a run used is recorded in its report.</summary>
    public sealed class BenchmarkArguments
    {
        #region Public-Members

        /// <summary>Command name (admission or compare).</summary>
        public string Command { get; private set; } = "";

        /// <summary>Positional arguments after the command.</summary>
        public List<string> Positional { get; } = new List<string>();

        #endregion

        #region Private-Members

        private readonly Dictionary<string, string> _Options = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>Parse arguments.</summary>
        /// <param name="args">Command line.</param>
        /// <returns>Parsed arguments.</returns>
        /// <exception cref="ArgumentException">Thrown when the command is missing or an option has no value.</exception>
        public static BenchmarkArguments Parse(string[] args)
        {
            if (args == null || args.Length == 0) throw new ArgumentException("A command is required: admission or compare.");
            BenchmarkArguments parsed = new BenchmarkArguments { Command = args[0] };
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value.");
                    parsed._Options[args[i].Substring(2)] = args[++i];
                }
                else parsed.Positional.Add(args[i]);
            }
            return parsed;
        }

        /// <summary>String option.</summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="fallback">Default.</param>
        /// <returns>The value.</returns>
        public string Text(string name, string fallback) => _Options.TryGetValue(name, out string? value) ? value : fallback;

        /// <summary>Integer option.</summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="fallback">Default.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentException">Thrown when the value is not an integer.</exception>
        public int Int(string name, int fallback)
        {
            if (!_Options.TryGetValue(name, out string? value)) return fallback;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) throw new ArgumentException("--" + name + " must be an integer.");
            return parsed;
        }

        /// <summary>Number option.</summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="fallback">Default.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentException">Thrown when the value is not a number.</exception>
        public double Number(string name, double fallback)
        {
            if (!_Options.TryGetValue(name, out string? value)) return fallback;
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) throw new ArgumentException("--" + name + " must be a number.");
            return parsed;
        }

        /// <summary>Comma-separated list option.</summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="fallback">Default.</param>
        /// <returns>The trimmed, non-empty items.</returns>
        public List<string> List(string name, string fallback)
            => Text(name, fallback).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        #endregion
    }
}
