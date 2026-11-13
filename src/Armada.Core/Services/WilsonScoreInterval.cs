namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// The 95% Wilson score interval of a binomial proportion, used for every scorecard rate. It stays inside [0, 1],
    /// is exactly 0 at the lower end when the count is 0 and exactly 1 at the upper end when the count equals the
    /// denominator, and with no observations it is the whole range [0, 1].
    /// </summary>
    public static class WilsonScoreInterval
    {
        #region Public-Members

        /// <summary>
        /// The normal quantile for a two-sided 95% interval.
        /// </summary>
        public const double Z95 = 1.959963984540054;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Lower bound of the 95% interval of <paramref name="count"/> successes in <paramref name="of"/> trials.
        /// </summary>
        /// <param name="count">Successes (0 to of).</param>
        /// <param name="of">Trials (0 or more).</param>
        /// <returns>The lower bound, rounded to four places; 0 when there are no trials.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when of is negative or count is outside 0 to of.</exception>
        public static double Lower(int count, int of)
        {
            Validate(count, of);
            if (of == 0 || count == 0) return 0.0;
            return Math.Round(Math.Max(0.0, Center(count, of) - HalfWidth(count, of)), 4);
        }

        /// <summary>
        /// Upper bound of the 95% interval of <paramref name="count"/> successes in <paramref name="of"/> trials.
        /// </summary>
        /// <param name="count">Successes (0 to of).</param>
        /// <param name="of">Trials (0 or more).</param>
        /// <returns>The upper bound, rounded to four places; 1 when there are no trials.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when of is negative or count is outside 0 to of.</exception>
        public static double Upper(int count, int of)
        {
            Validate(count, of);
            if (of == 0 || count == of) return 1.0;
            return Math.Round(Math.Min(1.0, Center(count, of) + HalfWidth(count, of)), 4);
        }

        #endregion

        #region Private-Methods

        private static void Validate(int count, int of)
        {
            if (of < 0) throw new ArgumentOutOfRangeException(nameof(of));
            if (count < 0 || count > of) throw new ArgumentOutOfRangeException(nameof(count));
        }

        private static double Center(int count, int of)
        {
            double n = of;
            double p = count / n;
            double z2 = Z95 * Z95;
            return (p + z2 / (2.0 * n)) / (1.0 + z2 / n);
        }

        private static double HalfWidth(int count, int of)
        {
            double n = of;
            double p = count / n;
            double z2 = Z95 * Z95;
            return Z95 * Math.Sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n)) / (1.0 + z2 / n);
        }

        #endregion
    }
}
