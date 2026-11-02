import type { UsageWindow } from '../types/models';

type Translate = (message: string, params?: Record<string, string | number | null | undefined>) => string;
type WindowLabel = Pick<UsageWindow, 'name'> & Partial<Pick<UsageWindow, 'windowMinutes' | 'model'>>;

/** Catalog keys used by the provider-neutral usage-window presentation. */
export const USAGE_WINDOW_LABELS = [
  'Weekly limit', 'Daily limit', 'Primary limit', 'Secondary limit', 'Usage limit',
  '{{count}}-hour limit', '{{count}}-day limit', '{{count}}-minute limit',
];

/** Human-readable window name; duration comes from provider metadata, never an assumed Codex reset. */
export function usageWindowLabel(quotaWindow: WindowLabel, t: Translate): string {
  const name = quotaWindow.name.trim();
  const normalized = name.toLowerCase();
  let label: string;
  if (/^(seven_day|weekly|7d)(?:_(opus|sonnet))?$/.test(normalized)) label = t('Weekly limit');
  else if (/^(five_hour|5h)$/.test(normalized)) label = t('{{count}}-hour limit', { count: 5 });
  else if (/^(daily|one_day|24h|1d)$/.test(normalized)) label = t('Daily limit');
  else if (/^(codex[:_])?(primary|secondary)$/.test(normalized)) {
    const minutes = quotaWindow.windowMinutes;
    if (minutes && Number.isFinite(minutes) && minutes > 0) {
      if (minutes === 10080) label = t('Weekly limit');
      else if (minutes === 1440) label = t('Daily limit');
      else if (minutes % 1440 === 0) label = t('{{count}}-day limit', { count: minutes / 1440 });
      else if (minutes % 60 === 0) label = t('{{count}}-hour limit', { count: minutes / 60 });
      else label = t('{{count}}-minute limit', { count: minutes });
    } else label = t(normalized.endsWith('primary') ? 'Primary limit' : 'Secondary limit');
  } else {
    // Custom/unknown identifiers retain their meaning while separators become spaces. Keep raw IDs in data.
    const words = name.replace(/[_:-]+/g, ' ').replace(/\s+/g, ' ').trim();
    label = words ? words.charAt(0).toUpperCase() + words.slice(1).toLowerCase() : t('Usage limit');
  }
  const model = quotaWindow.model?.trim() || normalized.match(/^seven_day_(opus|sonnet)$/)?.[1];
  if (model) {
    const displayModel = /^(opus|sonnet)$/i.test(model) ? model.charAt(0).toUpperCase() + model.slice(1).toLowerCase() : model;
    label += ` (${displayModel})`;
  }
  return label;
}
