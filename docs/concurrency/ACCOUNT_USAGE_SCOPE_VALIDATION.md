# Account usage scope annotation

Activity > Usage Overview (limits or combined view) and Activity > Account Limits share this visible note:

> Account usage reported by captains may include direct use of Claude Code, Codex or other harnesses outside Armada with the same provider account.

The note uses the existing muted chart-help styling and translation system, with phrases for all eight non-English locales. It remains visible for empty data and errors. It is omitted from token-only views: those charts summarize retained Armada token records, rather than provider account quotas.

The Dashboard production build and 23 affected Activity/chart/localization tests pass. Mobile/shared Dashboard lint passes with zero warnings. The change adds explanatory text only; it does not change collection, APIs, aggregation or filtering.

An owned disposable Admiral served the actual built Dashboard. Four views (combined Usage Overview, Account Limits, token-only Usage Overview and Token Usage) were checked at 1440, 768 and 390 pixels in both themes. All 24 checks verify the expected note count, no document horizontal overflow and one shared breadcrumb. Desktop and phone screenshots were visually inspected for readable wrapping, spacing and contrast. See [measurements](account-usage-scope-layout-results.json). Screenshots were reviewed locally and are not committed. The owned fixture was stopped after validation.

Manual check: open Usage Overview and Account Limits and confirm the note is visible. Select Tokens only or open Token Usage and confirm account-quota guidance is absent from those recorded-token views.

Release command: `./build-all.sh v1.3.0`; execution log: `/tmp/armada-v130-account-usage-note-build-all.log`.
