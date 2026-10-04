namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Testing;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Theme switching (palette pushed into widgets and the TUIKit theme, Auto from COLORFGBG, high contrast ASCII) and
    /// i18n (catalog phrases and terms, ICU plurals, locale aliases and formatting, a CJK locale rendered in the shell).
    /// </summary>
    public sealed class TuiThemeI18nSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.ThemeI18n";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "theme_switch_restyles", "Switching theme restyles rendered cells and the TUIKit theme", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    Color darkHeader = HeaderBackground(host);
                    host.Tui.Context.Commands.Execute("view.theme.light");
                    host.Pump();
                    Color lightHeader = HeaderBackground(host);
                    AssertNotEqual(darkHeader, lightHeader, "header background changed");
                    AssertEqual(ThemePalettes.Light().Header.Background, lightHeader, "light header color");
                    AssertEqual("Armada Light", host.App.Theme.Name, "TUIKit theme updated");
                    AssertEqual(ThemeModeEnum.Light, host.Tui.Context.Prefs.Current.Theme, "persisted");
                    host.Tui.Context.Commands.Execute("view.theme.high-contrast");
                    AssertTrue(host.Tui.Context.Theme.Current.AsciiBorders, "ascii borders");
                    AssertTrue(host.App.Theme.UseAsciiBorders, "TUIKit ascii borders");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "auto_theme", "Auto picks light for a light terminal background", () =>
            {
                ThemeService svc = new ThemeService();
                svc.EnvironmentReader = name => name == "COLORFGBG" ? "0;15" : null;
                svc.Apply(ThemeModeEnum.Auto);
                AssertEqual(ThemeModeEnum.Light, svc.EffectiveMode, "light");
                svc.EnvironmentReader = name => name == "COLORFGBG" ? "15;0" : null;
                svc.Apply(ThemeModeEnum.Auto);
                AssertEqual(ThemeModeEnum.Dark, svc.EffectiveMode, "dark");
                svc.EnvironmentReader = name => null;
                AssertEqual(ThemeModeEnum.Dark, svc.Resolve(ThemeModeEnum.Auto), "unknown is dark");
            }));

            cases.Add(TuiCase.Sync(Suite, "theme_pushes_into_tuikit_widgets", "The applicator pushes styles into TUIKit widgets", () =>
            {
                ArmadaTheme light = ThemePalettes.Light();
                TUIKit.Widgets.TextField field = new TUIKit.Widgets.TextField();
                TUIKit.Widgets.DiffView diff = new TUIKit.Widgets.DiffView("a", "b");
                ThemeApplicator.Apply(field, light);
                ThemeApplicator.Apply(diff, light);
                AssertEqual(light.Input, field.NormalStyle, "text field");
                AssertEqual(light.Success, diff.AddedStyle, "diff added");
            }));

            cases.Add(TuiCase.Sync(Suite, "translate_and_plurals", "Phrases, terms, ICU plurals, and interpolation follow the dashboard runtime", () =>
            {
                LocalizationService loc = new LocalizationService();
                loc.SetCatalog(Catalog());
                loc.SetLocale("ja-JP");
                AssertEqual("ja", loc.Locale, "alias normalized");
                AssertEqual("\u30df\u30c3\u30b7\u30e7\u30f3", loc.T("Missions"), "term");
                AssertEqual("\u30c0\u30c3\u30b7\u30e5\u30dc\u30fc\u30c9", loc.T("Dashboard"), "term 2");
                AssertEqual("Unknown text", loc.T("Unknown text"), "fallback to English");
                string msg = "{count, plural, =0 {Import repositories} one {Import # repository} other {Import # repositories}}";
                loc.SetLocale("en");
                AssertEqual("Import 1,234 repositories", loc.T(msg, LocalizationArgs.Of("count", 1234)), "en plural");
                AssertEqual("Import 1 repository", loc.T(msg, LocalizationArgs.Of("count", 1)), "one");
                AssertEqual("Import repositories", loc.T(msg, LocalizationArgs.Of("count", 0)), "exact");
                loc.SetLocale("de");
                AssertEqual("2.500 Repositorys importieren", loc.T(msg, LocalizationArgs.Of("count", 2500)), "de catalog plural and number format");
                AssertEqual("Run Build: 3 vessels queued", new LocalizationService().T("Run {{name}}: {count, plural, one {# vessel} other {# vessels}} queued", LocalizationArgs.Of("name", "Build", "count", 3)), "placeholders kept");
                AssertEqual("zh-Hans", loc.Normalize("zh-CN"), "zh alias");
                AssertEqual("yue-Hant", loc.Normalize("zh-HK"), "cantonese alias");
                AssertEqual("en", new LocalizationService().Normalize("xx-YY"), "unknown falls back");
                AssertEqual("other", PluralRules.Select("ja", 1), "ja has only other");
                AssertEqual("one", PluralRules.Select("fr", 0), "fr zero is one");
            }));

            cases.Add(TuiCase.Sync(Suite, "cjk_shell", "A CJK locale renders the shell with translated, aligned labels", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/dashboard/i18n/armada.json", Armada.Client.ArmadaJson.Serialize(Catalog()));
                using (TuiTestHost host = new TuiTestHost(120, 40, stub, "http://127.0.0.1:9", o => { o.Token = "tok"; o.StartRoute = "/missions"; }))
                {
                    host.Tui.Context.Prefs.Current.Locale = "ja";
                    host.Start();
                    host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null);
                    host.Tui.Context.Loc.SetLocale("ja");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "\u30df\u30c3\u30b7\u30e7\u30f3", "Missions translated");
                    TuiCase.Contains(frame, "\u30c0\u30c3\u30b7\u30e5\u30dc\u30fc\u30c9", "Dashboard translated");
                    foreach (string line in frame.Split('\n').Where(l => l.Length > 0))
                    {
                        AssertTrue(TextCells.Width(line) <= 120, "row fits the terminal width: " + line);
                    }

                    int bar = frame.Split('\n').Where(l => l.Contains("\u30c0\u30c3\u30b7\u30e5\u30dc\u30fc\u30c9")).Select(l => TextCells.Width(l.Substring(0, l.IndexOf('|')))).First();
                    int plain = frame.Split('\n').Where(l => l.Contains("|") && !l.Contains("\u30c0\u30c3")).Select(l => TextCells.Width(l.Substring(0, l.IndexOf('|')))).First();
                    AssertEqual(plain, bar, "sidebar border aligned on CJK rows");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "text_cells", "Cell-width helpers truncate, pad, and wrap CJK correctly", () =>
            {
                string cjk = "\u4fee\u590d\u8868\u683c\u5bbd\u5ea6";
                AssertEqual(12, TextCells.Width(cjk), "width");
                AssertEqual(10, TextCells.Width(TextCells.PadRight(cjk, 10)), "pad keeps width");
                AssertEqual(7, TextCells.Width(TextCells.Truncate("abcdefghij", 7)), "truncate");
                AssertTrue(TextCells.Truncate("abcdefghij", 7).EndsWith("..."), "ascii ellipsis");
                List<string> wrapped = TextCells.Wrap(cjk, 5);
                AssertTrue(wrapped.All(l => TextCells.Width(l) <= 5), "wrap honors width");
                AssertEqual(2, TextCells.Wrap("alpha beta gamma", 10).Count, "word wrap");
                AssertEqual("alpha beta", TextCells.Wrap("alpha beta gamma", 10)[0], "first line");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI theme and i18n", cases: cases);
        }

        private static Color HeaderBackground(TuiTestHost host)
        {
            CellBuffer buffer = new CellBuffer(host.Width, host.Height);
            host.Tui.Shell.Render(new BufferSurface(buffer));
            return buffer.Get(0, 0).Style.Background;
        }

        private static I18nCatalog Catalog()
        {
            I18nCatalog catalog = new I18nCatalog();
            catalog.DefaultLocale = "en";
            catalog.SupportedLocales = LocalizationService.DefaultLocales.ToList();
            I18nLocalePack ja = new I18nLocalePack();
            ja.Terms = new Dictionary<string, string> { ["Missions"] = "\u30df\u30c3\u30b7\u30e7\u30f3", ["Dashboard"] = "\u30c0\u30c3\u30b7\u30e5\u30dc\u30fc\u30c9", ["Help"] = "\u30d8\u30eb\u30d7" };
            ja.Phrases = new Dictionary<string, string> { ["Needs You"] = "\u5bfe\u5fdc\u304c\u5fc5\u8981", ["Coming in a later milestone"] = "\u4eca\u5f8c\u306e\u30de\u30a4\u30eb\u30b9\u30c8\u30fc\u30f3\u3067\u63d0\u4f9b" };
            I18nLocalePack de = new I18nLocalePack();
            de.Phrases = new Dictionary<string, string> { ["{count, plural, =0 {Import repositories} one {Import # repository} other {Import # repositories}}"] = "{count, plural, =0 {Repositorys importieren} one {# Repository importieren} other {# Repositorys importieren}}" };
            catalog.Locales["ja"] = ja;
            catalog.Locales["de"] = de;
            return catalog;
        }
    }
}
