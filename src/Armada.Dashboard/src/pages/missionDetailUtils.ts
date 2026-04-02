export const MISSION_DETAIL_GRID_COLUMNS = '1fr 1fr 1fr 1fr';

export function formatDuration(runtimeMs: number | null | undefined): string {
  if (runtimeMs == null) return '--';

  if (runtimeMs < 60000) {
    return (runtimeMs / 1000).toFixed(1).replace(/\.0$/, '') + 's';
  }

  const totalSeconds = Math.round(runtimeMs / 1000);
  const seconds = totalSeconds % 60;
  const totalMinutes = Math.floor(totalSeconds / 60);

  if (totalMinutes < 60) {
    return totalMinutes + 'm ' + seconds + 's';
  }

  const minutes = totalMinutes % 60;
  const hours = Math.floor(totalMinutes / 60);

  if (hours < 24) {
    return hours + 'h ' + minutes + 'm';
  }

  const days = Math.floor(hours / 24);
  return days + 'd ' + (hours % 24) + 'h';
}
