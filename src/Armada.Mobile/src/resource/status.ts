import { statusTone } from '../components/ask/statusTone';
import type { StatusTone } from '../components/ui/StatusBadge';
import type { Translate } from '../i18n/LocaleContext';

const EXTRA: Record<string, StatusTone> = {
  rolledback: 'warning',
  notrun: 'cancelled',
  partial: 'warning',
  pass: 'success',
  warn: 'warning',
  fail: 'failed',
  inactive: 'cancelled',
  resolved: 'success',
  closed: 'cancelled',
  mitigated: 'running',
  monitoring: 'running',
  draft: 'pending',
  candidate: 'info',
  shipped: 'success',
  rolledbackrelease: 'warning',
  critical: 'failed',
  high: 'failed',
  medium: 'warning',
  low: 'info',
};

/** Badge tone for any status the delivery, configuration, and activity screens show (the label is always shown). */
export function toneFor(status: string | null | undefined): StatusTone {
  const key = (status ?? '').toLowerCase().replace(/[^a-z]/g, '');
  return EXTRA[key] ?? statusTone(status);
}

/** A status badge: the translated status and its tone. */
export function statusBadge(t: Translate, status: string | null | undefined): { label: string; tone: StatusTone } {
  return { label: status ? t(status) : '-', tone: toneFor(status) };
}
