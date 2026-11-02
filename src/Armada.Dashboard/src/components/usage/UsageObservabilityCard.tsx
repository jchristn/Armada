import { useLocale } from '../../context/LocaleContext';

/** Links to the bundled operations tools; deployments can override browser-facing endpoints at build time. */
export default function UsageObservabilityCard() {
  const { t } = useLocale();
  const endpoint = (port: number) => {
    const url = new URL(window.location.origin);
    url.port = String(port);
    return url.origin;
  };
  const grafana = import.meta.env.VITE_GRAFANA_URL || endpoint(3001);
  const prometheus = import.meta.env.VITE_PROMETHEUS_URL || endpoint(9090);
  const tempo = import.meta.env.VITE_TEMPO_URL || endpoint(3200);
  return (
    <section className="card usage-observability" aria-label={t('External services')}>
      <h3>{t('External services')}</h3>
      <p className="text-dim">{t('Operational metrics, traces and logs for the bundled observability stack. Sign in with your configured Grafana credentials.')}</p>
      <div className="usage-observability-links">
        <a href={`${grafana.replace(/\/$/, '')}/d/armada-usage`} target="_blank" rel="noopener noreferrer">{t('Usage operations')} (Grafana)</a>
        <a href={prometheus} target="_blank" rel="noopener noreferrer">Prometheus</a>
        <a href={tempo} target="_blank" rel="noopener noreferrer">Tempo</a>
      </div>
    </section>
  );
}
