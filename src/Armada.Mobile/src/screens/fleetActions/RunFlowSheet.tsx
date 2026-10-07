import { BottomSheet } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { RunActionContent } from './RunActionSheet';
import { VesselPickerContent } from './VesselPickerSheet';

/** The run flow: choose vessels (unless given), then configure and confirm the run. */
export interface RunFlow {
  stage: 'pick' | 'run' | null;
  actionId: string | null;
  vesselIds: string[];
}

export const NO_RUN_FLOW: RunFlow = { stage: null, actionId: null, vesselIds: [] };

/**
 * The vessel picker and the run sheet in ONE sheet whose content changes, so going from picking to running never
 * dismisses one modal while presenting another (iOS drops the second presentation).
 */
export function RunFlowSheet({ flow, onChange }: { flow: RunFlow; onChange: (next: RunFlow) => void }) {
  const { t } = useLocale();
  const close = () => onChange(NO_RUN_FLOW);
  return (
    <BottomSheet
      open={flow.stage !== null}
      title={flow.stage === 'pick' ? t('Choose vessels to run on') : t('Run fleet action')}
      onClose={close}
      closeLabel={t('Close')}
      testID={flow.stage === 'pick' ? 'vessel-picker' : 'run-action'}
    >
      {flow.stage === 'pick' ? <VesselPickerContent onClose={close} onPicked={(ids) => onChange({ ...flow, stage: 'run', vesselIds: ids })} /> : null}
      {flow.stage === 'run' ? <RunActionContent open vesselIds={flow.vesselIds} initialActionId={flow.actionId} onClose={close} /> : null}
    </BottomSheet>
  );
}
