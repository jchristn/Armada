namespace Armada.Core.BlackBox
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Re-analyzes, in the background, analyzed entries whose rules version is older than the pipeline's advisor (a new
    /// <see cref="BlackBoxRuleCatalog.CurrentRulesVersion"/>), with <see cref="BlackBoxPipeline.ReanalyzeAsync"/> (advisor
    /// only, no enrichers). Each run reads at most <see cref="MaxPagesPerRun"/> pages of <see cref="PageSize"/> entries and
    /// re-analyzes at most <see cref="MaxPerRun"/>, resuming where the previous run stopped, so a large backlog is worked
    /// off over several sweeper ticks. Thread-safe (runs are serialized).
    /// </summary>
    public class BlackBoxReanalyzer
    {
        #region Public-Members

        /// <summary>Entries per page. Default 100, minimum 10, maximum 1000.</summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = Math.Clamp(value, 10, 1000);
        }

        /// <summary>Pages read per run. Default 5, minimum 1, maximum 100.</summary>
        public int MaxPagesPerRun
        {
            get => _MaxPagesPerRun;
            set => _MaxPagesPerRun = Math.Clamp(value, 1, 100);
        }

        /// <summary>Entries re-analyzed per run. Default 100, minimum 1, maximum 10000.</summary>
        public int MaxPerRun
        {
            get => _MaxPerRun;
            set => _MaxPerRun = Math.Clamp(value, 1, 10000);
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxReanalyzer] ";
        private readonly DatabaseDriver _Database;
        private readonly BlackBoxPipeline _Pipeline;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private int _PageSize = 100;
        private int _MaxPagesPerRun = 5;
        private int _MaxPerRun = 100;
        private int _NextPage = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="pipeline">Pipeline (its advisor sets the current rules version).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxReanalyzer(DatabaseDriver database, BlackBoxPipeline pipeline, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run once: re-analyze analyzed entries with an older rules version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of entries re-analyzed.</returns>
        public async Task<int> RunOnceAsync(CancellationToken token = default)
        {
            int current = _Pipeline.Advisor.RulesVersion;
            if (current <= 0) return 0;
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                int done = 0;
                for (int pages = 0; pages < _MaxPagesPerRun && done < _MaxPerRun; pages++)
                {
                    token.ThrowIfCancellationRequested();
                    BlackBoxEnumerateRequest query = new BlackBoxEnumerateRequest { State = BlackBoxStateEnum.Analyzed, PageNumber = _NextPage, PageSize = _PageSize, Order = EnumerationOrderEnum.CreatedDescending };
                    EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.EnumerateAsync(query, token).ConfigureAwait(false);
                    if (page.Objects.Count == 0)
                    {
                        _NextPage = 1;
                        break;
                    }
                    int before = done;
                    foreach (BlackBoxEntry stale in page.Objects.Where(e => e.RulesVersion < current))
                    {
                        if (done >= _MaxPerRun) break;
                        try
                        {
                            await _Pipeline.ReanalyzeAsync(stale.Id, token).ConfigureAwait(false);
                            done++;
                        }
                        catch (Exception e) when (!(e is OperationCanceledException))
                        {
                            _Logging.Warn(_Header + "re-analysis of " + stale.Id + " failed: " + e.Message);
                        }
                    }
                    // A page whose stale entries were all re-analyzed keeps its place (they leave the stale set but stay in
                    // the Analyzed listing), so move on; a page left with stale entries is read again next run.
                    if (done - before < page.Objects.Count(e => e.RulesVersion < current)) break;
                    _NextPage = page.PageNumber >= page.TotalPages ? 1 : _NextPage + 1;
                }
                if (done > 0) _Logging.Info(_Header + "re-analyzed " + done + " entries with rules older than version " + current);
                return done;
            }
            finally
            {
                _Gate.Release();
            }
        }

        #endregion
    }
}
