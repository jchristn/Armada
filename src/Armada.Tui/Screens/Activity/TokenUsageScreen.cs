namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Activity, Token Usage tab (dashboard <c>TokenUsage.tsx</c>, route <c>/activity?source=tokens</c>): range (last
    /// hour, day, week, month), metric (total by model, or by token type), shape (stacked bars or lines), totals
    /// (total, input, output, cached) with the estimated-records note, a "Usage over time" chart and a "Usage by
    /// model" chart. The dashboard copies each chart as an image; the TUI copies a text table of each chart and
    /// exports the buckets as CSV to a file. Not thread-safe.
    /// </summary>
    public class TokenUsageScreen : StackScreen
    {
        #region Public-Members

        /// <summary>
        /// Header.
        /// </summary>
        public ScreenHeader Header { get; }

        /// <summary>
        /// Range, metric, and shape pickers.
        /// </summary>
        public FilterStrip Controls { get; } = new FilterStrip();

        /// <summary>
        /// Range picker (hour, day, week, month).
        /// </summary>
        public SelectField<string> RangeField { get; } = new SelectField<string>();

        /// <summary>
        /// Metric picker ("total" or "byType").
        /// </summary>
        public SelectField<string> MetricField { get; } = new SelectField<string>();

        /// <summary>
        /// Shape picker ("bars" or "lines").
        /// </summary>
        public SelectField<string> ShapeField { get; } = new SelectField<string>();

        /// <summary>
        /// Totals.
        /// </summary>
        public KpiBar Totals { get; } = new KpiBar();

        /// <summary>
        /// Usage over time.
        /// </summary>
        public MultiSeriesChart TimeChart { get; } = new MultiSeriesChart();

        /// <summary>
        /// Usage by model.
        /// </summary>
        public TokenUsageModelChart ModelChart { get; } = new TokenUsageModelChart();

        /// <summary>
        /// Last result, or null.
        /// </summary>
        public TokenUsageSummaryResult? Data { get; private set; } = null;

        /// <summary>
        /// True while loading.
        /// </summary>
        public bool Loading { get; private set; } = false;

        /// <summary>
        /// Current range.
        /// </summary>
        public TokenUsageRange Range
        {
            get { return TokenUsageRange.For(RangeField.Value); }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("h/d/w/m", "Range"),
                    new KeyValuePair<string, string>("t", "Metric"),
                    new KeyValuePair<string, string>("b", "Bars/lines"),
                    new KeyValuePair<string, string>("y", "Copy"),
                    new KeyValuePair<string, string>("e", "Export CSV"),
                };
            }
        }

        #endregion

        #region Private-Members

        private readonly TextBlock _Note = new TextBlock("", t => t.Muted);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public TokenUsageScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Header = new ScreenHeader("Token Usage", "");
            Header.AddButton("Copy chart", CopyTimeChart, "y");
            Header.AddButton("Copy model chart", CopyModelChart);
            Header.AddButton("Export CSV", ExportCsv, "e");
            Header.AddButton("Refresh", Load, "F5");
            AddFixed(Header, w => Header.HeightFor(w));

            RangeField.ModalHost = context.Modals;
            RangeField.PickerTitle = "Range";
            RangeField.Options = TokenUsageRange.All.Select(r => new SelectOption<string>(r.Key, context.Loc.T(r.Label))).ToList();
            RangeField.SetValue(route.Query.TryGetValue("range", out string? range) ? TokenUsageRange.For(range).Key : "day");
            MetricField.ModalHost = context.Modals;
            MetricField.PickerTitle = "Metric";
            MetricField.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("total", context.Loc.T("Total")),
                new SelectOption<string>("byType", context.Loc.T("By token type")),
            };
            MetricField.SetValue("total");
            ShapeField.ModalHost = context.Modals;
            ShapeField.PickerTitle = "Shape";
            ShapeField.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("bars", context.Loc.T("Stacked bars")),
                new SelectOption<string>("lines", context.Loc.T("Lines")),
            };
            ShapeField.SetValue("bars");
            Controls.Add("Range", RangeField, 14);
            Controls.Add("Metric", MetricField, 16);
            Controls.Add("Shape", ShapeField, 14);
            RangeField.ValueChanged += (s, e) => Load();
            MetricField.ValueChanged += (s, e) => Rebuild();
            ShapeField.ValueChanged += (s, e) => Rebuild();
            AddFixed(Controls, w => Controls.HeightFor(w));
            AddFixed(Totals, w => 3);
            _Note.Translate = false;
            AddFixed(_Note, w => _Note.Text.Length > 0 ? 1 : 0);
            AddFixed(ModelChart, w => Math.Min(12, ModelChart.PreferredHeight) + 1);
            TimeChart.Title = "Usage over time";
            TimeChart.ValueFormatter = v => TokenUsageFormat.Tokens(v);
            TimeChart.EmptyText = "No token usage for this time range";
            AddFill(TimeChart);
            Scope.Focus(Controls);
            Rebuild();
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Load;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> commands = new List<ArmadaCommand>();
            foreach (TokenUsageRange r in TokenUsageRange.All)
            {
                TokenUsageRange captured = r;
                commands.Add(new ArmadaCommand(ScreenKey + ".range-" + r.Key, r.Label, CommandMenuEnum.Actions, () => SetRange(captured.Key), r.Key.Substring(0, 1)));
            }

            commands.Add(new ArmadaCommand(ScreenKey + ".metric", "Toggle metric (total / by token type)", CommandMenuEnum.Actions, () => SetMetric(MetricField.Value == "byType" ? "total" : "byType"), "t"));
            commands.Add(new ArmadaCommand(ScreenKey + ".shape", "Toggle stacked bars / lines", CommandMenuEnum.Actions, () => SetShape(ShapeField.Value == "lines" ? "bars" : "lines"), "b"));
            commands.Add(new ArmadaCommand(ScreenKey + ".copy", "Copy chart", CommandMenuEnum.Actions, CopyTimeChart, "y"));
            commands.Add(new ArmadaCommand(ScreenKey + ".copy-model", "Copy model chart", CommandMenuEnum.Actions, CopyModelChart));
            commands.Add(new ArmadaCommand(ScreenKey + ".export-csv", "Export CSV", CommandMenuEnum.Actions, ExportCsv, "e"));
            return commands;
        }

        /// <summary>
        /// Select a range and reload.
        /// </summary>
        /// <param name="key">"hour", "day", "week", or "month".</param>
        public void SetRange(string key)
        {
            SelectOption<string>? option = RangeField.Options.FirstOrDefault(o => o.Value == key);
            if (option != null) RangeField.Choose(option);
        }

        /// <summary>
        /// Select a metric.
        /// </summary>
        /// <param name="metric">"total" or "byType".</param>
        public void SetMetric(string metric)
        {
            SelectOption<string>? option = MetricField.Options.FirstOrDefault(o => o.Value == metric);
            if (option != null) MetricField.Choose(option);
        }

        /// <summary>
        /// Select a shape.
        /// </summary>
        /// <param name="shape">"bars" or "lines".</param>
        public void SetShape(string shape)
        {
            SelectOption<string>? option = ShapeField.Options.FirstOrDefault(o => o.Value == shape);
            if (option != null) ShapeField.Choose(option);
        }

        /// <summary>
        /// The query for the current range ending now.
        /// </summary>
        /// <returns>Query.</returns>
        public TokenUsageQuery BuildQuery()
        {
            TokenUsageRange r = Range;
            DateTime end = Context.Clock.UtcNow;
            TokenUsageQuery q = new TokenUsageQuery();
            q.FromUtc = end.AddHours(-r.Hours);
            q.ToUtc = end;
            q.BucketMinutes = r.StepMinutes;
            return q;
        }

        /// <summary>
        /// Load the summary for the current range.
        /// </summary>
        public void Load()
        {
            TokenUsageQuery query = BuildQuery();
            Loading = true;
            Rebuild();
            ScreenOps.Quiet(Context, async () =>
            {
                try
                {
                    return await Context.Client.GetTokenUsageAsync(query).ConfigureAwait(false);
                }
                catch (Armada.Client.ArmadaApiException)
                {
                    return null;
                }
            }, result =>
            {
                Loading = false;
                Data = result;
                Rebuild();
            });
        }

        /// <summary>
        /// CSV of the buckets: start, end, input, output, cached, total, then one column per model.
        /// </summary>
        /// <returns>CSV text.</returns>
        public string BuildCsv()
        {
            TokenUsageSummaryResult data = Data ?? new TokenUsageSummaryResult();
            List<string> models = data.ByModel.Select(m => m.Model).ToList();
            List<string> header = new List<string> { "bucketStartUtc", "bucketEndUtc", "inputTokens", "outputTokens", "cachedTokens", "totalTokens" };
            header.AddRange(models);
            List<IEnumerable<string?>> rows = new List<IEnumerable<string?>>();
            foreach (TokenUsageBucket b in data.Buckets)
            {
                List<string?> row = new List<string?>
                {
                    Iso(b.BucketStartUtc), Iso(b.BucketEndUtc),
                    b.InputTokens.ToString(CultureInfo.InvariantCulture), b.OutputTokens.ToString(CultureInfo.InvariantCulture),
                    b.CachedTokens.ToString(CultureInfo.InvariantCulture), b.TotalTokens.ToString(CultureInfo.InvariantCulture),
                };
                foreach (string model in models)
                {
                    TokenUsageModelBreakdown? m = b.Models.FirstOrDefault(x => x.Model == model);
                    row.Add((m != null ? m.TotalTokens : 0).ToString(CultureInfo.InvariantCulture));
                }

                rows.Add(row);
            }

            return DataExport.Csv(header, rows);
        }

        /// <summary>
        /// Write the CSV to a path without prompting.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Full path written.</returns>
        public string ExportCsvTo(string path)
        {
            string written = Context.External.SaveText(path, BuildCsv());
            ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Exported to {{path}}.", LocalizationArgs.Of("path", written));
            return written;
        }

        #endregion

        #region Private-Methods

        private void Rebuild()
        {
            TokenUsageSummaryResult? data = Data;
            bool byType = MetricField.Value == "byType";
            TimeChart.Kind = ShapeField.Value == "lines" ? ChartKindEnum.Line : ChartKindEnum.Bar;
            Totals.SetCards(new List<KpiCard>
            {
                new KpiCard("Total", TokenUsageFormat.Tokens(data?.TotalTokens ?? 0)),
                new KpiCard("Input", TokenUsageFormat.Tokens(data?.InputTokens ?? 0), t => t.Accent),
                new KpiCard("Output", TokenUsageFormat.Tokens(data?.OutputTokens ?? 0), t => t.Success),
                new KpiCard("Cached", TokenUsageFormat.Tokens(data?.CachedTokens ?? 0), t => t.Warning),
            });
            _Note.Text = data != null && data.EstimatedCount > 0
                ? Context.Loc.T("{{estimated}} of {{total}} records estimated", LocalizationArgs.Of("estimated", data.EstimatedCount, "total", data.RecordCount))
                : "";
            ModelChart.ByType = byType;
            ModelChart.Models = data?.ByModel ?? new List<TokenUsageModelBreakdown>();
            if (Loading)
            {
                TimeChart.EmptyText = "Loading token usage...";
                ModelChart.Message = "Loading token usage...";
                TimeChart.SetData(new List<string>(), new List<ChartSeries>());
                return;
            }

            TimeChart.EmptyText = "No token usage for this time range";
            ModelChart.Message = "";
            List<TokenUsageBucket> buckets = data?.Buckets ?? new List<TokenUsageBucket>();
            TokenUsageRange r = Range;
            List<string> labels = buckets.Select(b => TokenUsageFormat.BucketLabel(b.BucketStartUtc, r.StepMinutes, r.Hours, Context.Loc.Culture)).ToList();
            List<ChartSeries> series = new List<ChartSeries>();
            if (byType)
            {
                series.Add(new ChartSeries("Input", buckets.Select(b => (double)b.InputTokens)));
                series.Add(new ChartSeries("Output", buckets.Select(b => (double)b.OutputTokens)));
                series.Add(new ChartSeries("Cached", buckets.Select(b => (double)b.CachedTokens)));
            }
            else
            {
                foreach (TokenUsageModelBreakdown model in data?.ByModel ?? new List<TokenUsageModelBreakdown>())
                {
                    string name = model.Model;
                    series.Add(new ChartSeries(name, buckets.Select(b =>
                    {
                        TokenUsageModelBreakdown? m = b.Models.FirstOrDefault(x => x.Model == name);
                        return m != null ? (double)m.TotalTokens : 0;
                    })));
                }
            }

            TimeChart.SetData(labels, series);
        }

        private void CopyTimeChart()
        {
            if (Data == null || Data.Buckets.Count == 0) return;
            Context.Clipboard.Copy(TimeChart.ToTextTable(), "Chart");
        }

        private void CopyModelChart()
        {
            if (Data == null || Data.ByModel.Count == 0) return;
            Context.Clipboard.Copy(ModelChart.ToTextTable(), "Chart");
        }

        private void ExportCsv()
        {
            string name = "armada-token-usage-" + Range.Key + "-" + Context.Clock.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture) + ".csv";
            PathPrompt.AskSave(Context, "Export CSV", name, path =>
            {
                try
                {
                    ExportCsvTo(path);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    Context.Notifications.Toast(NotificationSeverityEnum.Error, ex.Message);
                }
            });
        }

        private static string Iso(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
