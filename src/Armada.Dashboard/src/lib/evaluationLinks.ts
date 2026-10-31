/** Dashboard path of one evaluation's results page (/evaluations/:id). */
export function evaluationResultsPath(runId: string): string {
  return `/evaluations/${encodeURIComponent(runId)}`;
}

/** Dashboard path of the Evaluations list, optionally filtered to one captain (/evaluations?captainId=...). */
export function evaluationsListPath(captainId?: string | null): string {
  return captainId ? `/evaluations?captainId=${encodeURIComponent(captainId)}` : '/evaluations';
}
