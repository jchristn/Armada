import type {
  DependencyDrift,
  VesselHealthCriterion,
  VesselHealthFinding,
  VesselHealthStatus,
  VulnerabilitySeverity,
} from '../../types/models';

/**
 * Localization for vessel health. The server stores stable codes (statuses, criteria, detail codes
 * plus two integers); this module turns them into English catalog keys that the dashboard i18n
 * runtime (`t`) translates. Numbers are formatted with an explicit locale before interpolation.
 *
 * Every English key is written as a literal inside `msg(...)`, `t(...)` or `plural(...)` so the
 * catalog-coverage test can find it by scanning this file.
 */

/** Translation function shape provided by `useLocale().t`. */
export type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

/** Marks an English catalog key for extraction without translating it yet (translated where rendered). */
export function msg(key: string): string {
  return key;
}

export const HEALTH_STATUSES: VesselHealthStatus[] = ['Pass', 'Warn', 'Fail', 'NotApplicable', 'Unknown'];

/** Criteria that have findings, in display order (Overall is valid only as an override target). */
export const HEALTH_CRITERIA: VesselHealthCriterion[] = [
  'GitDivergence',
  'WorkingTree',
  'Branches',
  'CommitRecency',
  'Dependencies',
  'Vulnerabilities',
  'TestInfrastructure',
  'ContinuousIntegration',
  'ArmadaReadiness',
  'MissionOutcomes',
];

/** Override targets: every criterion plus Overall. */
export const OVERRIDE_CRITERIA: VesselHealthCriterion[] = ['Overall', ...HEALTH_CRITERIA];

export const STATUS_LABELS: Record<VesselHealthStatus, string> = {
  Pass: msg('Pass'),
  Warn: msg('Warn'),
  Fail: msg('Fail'),
  NotApplicable: msg('Not applicable'),
  Unknown: msg('Unknown'),
};

export const STATUS_DESCRIPTIONS: Record<VesselHealthStatus, string> = {
  Pass: msg('Healthy by the configured thresholds.'),
  Warn: msg('Worth a look soon.'),
  Fail: msg('Needs attention.'),
  NotApplicable: msg('Does not apply to this vessel.'),
  Unknown: msg('Could not be evaluated, or never evaluated.'),
};

export const CRITERION_LABELS: Record<VesselHealthCriterion, string> = {
  GitDivergence: msg('Git divergence'),
  WorkingTree: msg('Working tree'),
  Branches: msg('Branches'),
  CommitRecency: msg('Commit recency'),
  Dependencies: msg('Dependencies'),
  Vulnerabilities: msg('Vulnerabilities'),
  TestInfrastructure: msg('Test infrastructure'),
  ContinuousIntegration: msg('Continuous integration'),
  ArmadaReadiness: msg('Armada readiness'),
  MissionOutcomes: msg('Mission outcomes'),
  Overall: msg('Overall'),
};

export const SEVERITY_LABELS: Record<VulnerabilitySeverity, string> = {
  None: msg('None'),
  Low: msg('Low'),
  Moderate: msg('Moderate'),
  High: msg('High'),
  Critical: msg('Critical'),
};

export const DRIFT_LABELS: Record<DependencyDrift, string> = {
  None: msg('None'),
  Patch: msg('Patch'),
  Minor: msg('Minor'),
  Major: msg('Major'),
};

/** Severity rank carried in ValueB of VulnerablePackages / NoVulnerabilities. */
const SEVERITY_BY_RANK: VulnerabilitySeverity[] = ['None', 'Low', 'Moderate', 'High', 'Critical'];

export function statusLabel(t: Translate, status: VesselHealthStatus | null | undefined): string {
  const key = status && STATUS_LABELS[status] ? STATUS_LABELS[status] : STATUS_LABELS.Unknown;
  return t(key);
}

export function criterionLabel(t: Translate, criterion: VesselHealthCriterion | string): string {
  const key = CRITERION_LABELS[criterion as VesselHealthCriterion];
  return key ? t(key) : criterion;
}

export function severityLabel(t: Translate, severity: VulnerabilitySeverity | null | undefined): string {
  const key = severity && SEVERITY_LABELS[severity] ? SEVERITY_LABELS[severity] : SEVERITY_LABELS.None;
  return t(key);
}

export function driftLabel(t: Translate, drift: DependencyDrift | null | undefined): string {
  const key = drift && DRIFT_LABELS[drift] ? DRIFT_LABELS[drift] : DRIFT_LABELS.None;
  return t(key);
}

/** Locale-explicit integer formatting. */
export function formatCount(locale: string, value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '-';
  try {
    return new Intl.NumberFormat(locale).format(value);
  } catch {
    return String(value);
  }
}

/** Locale-explicit relative day phrase ("3 days ago", "yesterday"). */
export function formatDaysAgo(locale: string, days: number): string {
  try {
    return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(-days, 'day');
  } catch {
    return `${days}d`;
  }
}

/**
 * Picks the singular or plural English key for a count. The catalog holds a translation for both keys;
 * locales without a grammatical plural simply translate both the same way.
 */
export function pluralKey(count: number, one: string, other: string): string {
  return count === 1 ? one : other;
}

interface DescribeContext {
  t: Translate;
  locale: string;
}

type DetailFormatter = (a: number, b: number, ctx: DescribeContext, rawA: number | null | undefined) => string;

/** Translates the singular/plural key for `count` and interpolates `count` (formatted) plus extra params. */
function plural(ctx: DescribeContext, count: number, one: string, other: string, params?: Record<string, string>): string {
  return ctx.t(pluralKey(count, one, other), { count: formatCount(ctx.locale, count), ...(params ?? {}) });
}

/**
 * One formatter per backend detail code (see `VesselHealthDetailCodes.cs`). ValueA and ValueB arrive as
 * `a` and `b` (null coerced to 0); `rawA` keeps null so optional values can be detected.
 */
export const DETAIL_CODE_FORMATTERS: Record<string, DetailFormatter> = {
  // General
  EvaluationError: (_a, _b, { t }) => t('The check failed while evaluating.'),
  RepositoryUnavailable: (_a, _b, { t }) => t('The repository was not found on disk.'),
  BareRepository: (_a, _b, { t }) => t('Only a bare clone is available, so there is no checkout to inspect.'),
  NotApplicable: (_a, _b, { t }) => t('Does not apply to this vessel.'),

  // GitDivergence
  Even: (_a, _b, { t }) => t('Even with the default branch.'),
  Ahead: (a, _b, ctx) => plural(ctx, a, 'Ahead by {{count}} commit.', 'Ahead by {{count}} commits.'),
  Behind: (a, b, ctx) => (a > 0
    ? plural(ctx, b, 'Behind by {{count}} commit (ahead {{ahead}}).', 'Behind by {{count}} commits (ahead {{ahead}}).', { ahead: formatCount(ctx.locale, a) })
    : plural(ctx, b, 'Behind by {{count}} commit.', 'Behind by {{count}} commits.')),
  Diverged: (a, b, ctx) => ctx.t('Diverged: {{ahead}} ahead and {{behind}} behind.', { ahead: formatCount(ctx.locale, a), behind: formatCount(ctx.locale, b) }),
  FetchFailed: (_a, _b, { t }) => t('git fetch failed, so divergence could not be measured.'),
  DefaultBranchMissing: (_a, _b, { t }) => t('The default branch could not be found.'),
  NoCommits: (_a, _b, { t }) => t('The repository has no commits.'),

  // WorkingTree
  Clean: (_a, _b, { t }) => t('The working tree is clean.'),
  UntrackedOnly: (_a, b, ctx) => plural(ctx, b, '{{count}} untracked file.', '{{count}} untracked files.'),
  Modified: (a, b, ctx) => plural(ctx, a, '{{count}} modified tracked file ({{untracked}} untracked).', '{{count}} modified tracked files ({{untracked}} untracked).', { untracked: formatCount(ctx.locale, b) }),

  // Branches
  BranchesOk: (a, _b, ctx) => (a === 0
    ? ctx.t('No stale branches.')
    : plural(ctx, a, '{{count}} stale branch.', '{{count}} stale branches.')),
  StaleBranches: (a, b, ctx) => plural(ctx, a, '{{count}} stale branch ({{armada}} leftover armada/*).', '{{count}} stale branches ({{armada}} leftover armada/*).', { armada: formatCount(ctx.locale, b) }),
  ArmadaBranches: (a, b, ctx) => plural(ctx, b, '{{count}} leftover armada/* branch ({{stale}} stale).', '{{count}} leftover armada/* branches ({{stale}} stale).', { stale: formatCount(ctx.locale, a) }),

  // CommitRecency
  LastCommitAge: (a, _b, ctx) => ctx.t('Last commit {{when}}.', { when: formatDaysAgo(ctx.locale, a) }),

  // Dependencies and Vulnerabilities
  NoOutdatedPackages: (_a, _b, { t }) => t('All packages are up to date.'),
  OutdatedPackages: (a, b, ctx) => plural(ctx, a, '{{count}} outdated package ({{major}} major).', '{{count}} outdated packages ({{major}} major).', { major: formatCount(ctx.locale, b) }),
  NoVulnerabilities: (_a, _b, { t }) => t('No known vulnerabilities.'),
  VulnerablePackages: (a, b, ctx) => plural(ctx, a, '{{count}} vulnerable package (highest severity {{severity}}).', '{{count}} vulnerable packages (highest severity {{severity}}).', { severity: severityLabel(ctx.t, SEVERITY_BY_RANK[b] ?? 'None') }),
  NoPackageManifests: (_a, _b, { t }) => t('No NuGet project or npm package with a lockfile was found.'),
  ToolMissing: (_a, _b, { t }) => t('dotnet or npm is not installed on the Admiral host.'),
  RestoreRequired: (_a, _b, { t }) => t('A package restore is required before packages can be checked.'),
  Timeout: (a, _b, ctx, rawA) => (rawA === null || rawA === undefined
    ? ctx.t('The package tool timed out.')
    : plural(ctx, a, 'The package tool timed out after {{count}} second.', 'The package tool timed out after {{count}} seconds.')),
  ParseError: (_a, _b, { t }) => t('The package tool output could not be parsed.'),
  ToolFailed: (a, _b, ctx, rawA) => (rawA === null || rawA === undefined
    ? ctx.t('The package tool failed.')
    : ctx.t('The package tool exited with code {{code}}.', { code: String(a) })),

  // TestInfrastructure
  TestsPassing: (a, _b, ctx) => plural(ctx, a, '{{count}} test indicator found and the last test run passed.', '{{count}} test indicators found and the last test run passed.'),
  NoTestRun: (a, _b, ctx) => plural(ctx, a, '{{count}} test indicator found but no test run is recorded.', '{{count}} test indicators found but no test run is recorded.'),
  LastTestRunFailed: (a, _b, ctx) => plural(ctx, a, '{{count}} test indicator found but the last test run did not pass.', '{{count}} test indicators found but the last test run did not pass.'),
  NoTestsFound: (a, _b, ctx) => plural(ctx, a, 'No tests found in {{count}} recognized project.', 'No tests found in {{count}} recognized projects.'),
  NoRecognizedProject: (_a, _b, { t }) => t('No .NET, Node, Python, Go, or Rust project was recognized.'),

  // ContinuousIntegration
  CiConfigured: (a, _b, ctx) => plural(ctx, a, '{{count}} CI configuration file found.', '{{count}} CI configuration files found.'),
  NoCiConfig: (_a, _b, { t }) => t('No CI configuration was found.'),

  // ArmadaReadiness
  ReadinessOk: (_a, _b, { t }) => t('No readiness issues.'),
  ReadinessWarnings: (_a, b, ctx) => plural(ctx, b, '{{count}} readiness warning.', '{{count}} readiness warnings.'),
  ReadinessErrors: (a, b, ctx) => plural(ctx, a, '{{count}} readiness error ({{warnings}} warnings).', '{{count}} readiness errors ({{warnings}} warnings).', { warnings: formatCount(ctx.locale, b) }),

  // MissionOutcomes
  NoRecentFailures: (_a, b, ctx) => (b === 1
    ? ctx.t('No failed missions in the last day.')
    : ctx.t('No failed missions in the last {{days}} days.', { days: formatCount(ctx.locale, b) })),
  RecentFailures: (a, b, ctx) => (b === 1
    ? plural(ctx, a, '{{count}} failed mission in the last day.', '{{count}} failed missions in the last day.')
    : plural(ctx, a, '{{count}} failed mission in the last {{days}} days.', '{{count}} failed missions in the last {{days}} days.', { days: formatCount(ctx.locale, b) })),
};

/**
 * Localized one-sentence description of a finding, e.g. "Behind by 7 commits (ahead 2)." Unknown codes
 * (a newer server) fall back to the raw code so nothing is silently hidden.
 */
export function describeFinding(
  finding: Pick<VesselHealthFinding, 'detailCode' | 'valueA' | 'valueB'>,
  t: Translate,
  locale: string,
): string {
  const code = finding.detailCode ?? '';
  if (!code) return t('No details recorded.');
  const formatter = DETAIL_CODE_FORMATTERS[code];
  if (!formatter) return code;
  const a = typeof finding.valueA === 'number' ? finding.valueA : 0;
  const b = typeof finding.valueB === 'number' ? finding.valueB : 0;
  return formatter(a, b, { t, locale }, finding.valueA);
}
