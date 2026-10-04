import type { PlanningSessionMessage } from '../../types/models';

interface PlanningDispatchCardProps {
  t: (value: string, vars?: Record<string, string | number>) => string;
  selectedMessage: PlanningSessionMessage | null;
  dispatchTitle: string;
  dispatchDescription: string;
  canSummarize: boolean;
  canOpenInDispatch: boolean;
  canDispatch: boolean;
  summarizing: boolean;
  dispatching: boolean;
  onDispatchTitleChange: (value: string) => void;
  onDispatchDescriptionChange: (value: string) => void;
  onSummarize: () => void;
  onOpenInDispatch: () => void;
  onDispatch: () => void;
}

export default function PlanningDispatchCard(props: PlanningDispatchCardProps) {
  const {
    t,
    selectedMessage,
    dispatchTitle,
    dispatchDescription,
    canSummarize,
    canOpenInDispatch,
    canDispatch,
    summarizing,
    dispatching,
    onDispatchTitleChange,
    onDispatchDescriptionChange,
    onSummarize,
    onOpenInDispatch,
    onDispatch,
  } = props;

  return (
    <div className="card" style={{ padding: '1rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'start', marginBottom: '0.75rem' }}>
        <div>
          <h3>{t('Dispatch From Session')}</h3>
          <p className="text-muted">
            {t('Select an assistant response, optionally summarize it into a cleaner draft, then dispatch or open the draft in the main dispatch page.')}
          </p>
        </div>
        {selectedMessage && (
          <span className="text-dim mono" style={{ fontSize: '0.8rem' }}>
            {selectedMessage.id}
          </span>
        )}
      </div>

      <div className="dispatch-form">
        <div className="form-group">
          <label>{t('Voyage Title')}</label>
          <input aria-label={t('Voyage Title')}
            value={dispatchTitle}
            onChange={(event) => onDispatchTitleChange(event.target.value)}
            placeholder={t('Optional override for the resulting voyage title')}
          />
        </div>

        <div className="form-group">
          <label>{t('Mission Description')}</label>
          <textarea aria-label={t('Mission Description')}
            value={dispatchDescription}
            onChange={(event) => onDispatchDescriptionChange(event.target.value)}
            rows={10}
            placeholder={t('Select a planning response to seed the dispatch description.')}
          />
        </div>

        <div className="form-actions" style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
          <button type="button" className="btn" disabled={!canSummarize} onClick={onSummarize}>
            {summarizing ? t('Summarizing...') : t('Summarize Draft')}
          </button>
          <button type="button" className="btn" disabled={!canOpenInDispatch} onClick={onOpenInDispatch}>
            {t('Open In Dispatch')}
          </button>
          <button type="button" className="btn-primary" disabled={!canDispatch} onClick={onDispatch}>
            {dispatching ? t('Dispatching...') : t('Dispatch')}
          </button>
        </div>
      </div>
    </div>
  );
}
