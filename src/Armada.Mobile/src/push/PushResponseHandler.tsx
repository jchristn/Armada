import { useRouter, type Href } from 'expo-router';
import { useEffect, useRef } from 'react';
import { useAuth } from '../auth/AuthContext';
import { authenticateBiometric } from '../auth/biometrics';
import { useLocale } from '../i18n/LocaleContext';
import { useApprovals } from '../notifications/ApprovalsContext';
import { useNotifications, type Severity } from '../notifications/NotificationContext';
import { performPushAction, type PushActionOutcome } from './actions';
import { defaultApprovalActions, type ApprovalActions } from './approvalActions';
import { usePush } from './PushContext';

export interface PushResponseHandlerProps {
  actions?: ApprovalActions;
  /** Device / biometric verification for profiles with biometric unlock; injectable for tests. */
  verify?: (prompt: string, cancel: string) => Promise<boolean>;
}

function outcomeToast(outcome: PushActionOutcome, t: (s: string) => string): { severity: Severity; message: string } | null {
  switch (outcome) {
    case 'approved': return { severity: 'success', message: t('Approved.') };
    case 'denied': return { severity: 'success', message: t('Denied.') };
    case 'alreadyDecided': return { severity: 'info', message: t('This request was already decided.') };
    case 'notAllowed': return { severity: 'warning', message: t('This request is gone or you cannot decide it.') };
    case 'verificationFailed': return { severity: 'warning', message: t('Not verified. Nothing was approved or denied.') };
    case 'failed': return { severity: 'error', message: t('The decision could not be sent. Open the request to try again.') };
    default: return null;
  }
}

/**
 * Opens (and, for Approve / Deny, acts on) the notification the user tapped, once the app is signed in to the
 * profile it came from. Rendered only while the app shell is ready. Actions run only for pushes addressed to a
 * device this app registered, and only after the profile's biometric check when it has one; the iOS action itself
 * already required an unlocked device.
 */
export function PushResponseHandler({ actions = defaultApprovalActions, verify = authenticateBiometric }: PushResponseHandlerProps) {
  const { pending, takePending } = usePush();
  const { activeProfile } = useAuth();
  const { pushToast } = useNotifications();
  const { refresh } = useApprovals();
  const { t } = useLocale();
  const router = useRouter();
  const busyRef = useRef(false);

  useEffect(() => {
    if (!pending || busyRef.current) return;
    if (pending.profileId && pending.profileId !== activeProfile?.id) return; // the provider is switching profiles
    const current = takePending();
    if (!current) return;
    busyRef.current = true;
    void (async () => {
      try {
        if (current.action !== 'open' && current.trusted) {
          const outcome = await performPushAction(current.payload, current.action, actions, {
            verify: activeProfile?.biometricUnlock
              ? () => verify(current.action === 'approve' ? t('Confirm to approve') : t('Confirm to deny'), t('Cancel'))
              : null,
          });
          const toast = outcomeToast(outcome, t);
          if (toast) pushToast(toast.severity, toast.message, current.payload.path);
          if (outcome === 'approved' || outcome === 'denied') void refresh();
        }
        if (current.payload.path) router.push(current.payload.path as Href);
      } finally {
        busyRef.current = false;
      }
    })();
  }, [pending, takePending, activeProfile, actions, verify, pushToast, refresh, router, t]);

  return null;
}
