import type { ModelEndpointHealthRecord } from '../../types/models';

interface HealthHistogramProps {
  history: ModelEndpointHealthRecord[];
  width?: number;
  height?: number;
  /** Stretch to fill the container width instead of a fixed pixel width. */
  fill?: boolean;
}

interface Bucket {
  success: number;
  fail: number;
  time: number;
}

/**
 * A segmented bar chart of recent health-check probes over time. Each segment is one time bucket:
 * green when every probe in the bucket succeeded, red when all failed, amber when mixed. Modeled on the
 * health histogram used across the sibling dashboards (conductor, pneuma, assistanthub, isis).
 */
export default function HealthHistogram({ history, width = 80, height = 18, fill = false }: HealthHistogramProps) {
  if (!history || history.length === 0) {
    return <span className="text-dim" style={{ fontSize: '0.75rem' }}>No data</span>;
  }

  const now = Date.now();
  const sorted = [...history].sort((a, b) => new Date(a.timestampUtc).getTime() - new Date(b.timestampUtc).getTime());
  const oldest = new Date(sorted[0].timestampUtc).getTime();
  const spanHours = (now - oldest) / (1000 * 60 * 60);

  let buckets: Bucket[] = [];
  if (spanHours < 1) {
    buckets = sorted.map((r) => ({
      success: r.success ? 1 : 0,
      fail: r.success ? 0 : 1,
      time: new Date(r.timestampUtc).getTime(),
    }));
  } else {
    const bucketMs = spanHours <= 6 ? 60000 : 300000;
    const map = new Map<number, Bucket>();
    for (const r of sorted) {
      const key = Math.floor(new Date(r.timestampUtc).getTime() / bucketMs);
      let b = map.get(key);
      if (!b) {
        b = { success: 0, fail: 0, time: key * bucketMs };
        map.set(key, b);
      }
      if (r.success) b.success += 1;
      else b.fail += 1;
    }
    buckets = Array.from(map.values()).sort((a, b) => a.time - b.time);
  }

  const maxBars = Math.max(6, Math.floor(width / 4));
  if (buckets.length > maxBars) buckets = buckets.slice(buckets.length - maxBars);

  return (
    <div
      className="health-histogram"
      style={{ height: `${height}px`, width: fill ? '100%' : `${width}px`, maxWidth: fill ? '100%' : `${width}px` }}
      aria-label="Health check history"
    >
      {buckets.map((b, i) => {
        const tone = b.fail === 0 ? 'ok' : b.success === 0 ? 'fail' : 'mixed';
        const title = `${new Date(b.time).toLocaleString()} - ${b.success} ok, ${b.fail} fail`;
        return <span key={i} className={`hh-bar hh-${tone}`} title={title} />;
      })}
    </div>
  );
}
