import { BlackBoxRedirect } from '@/navigation/legacyRedirects';

/** /blackbox: now the Missions hub's Black Box tab; the old ?tab= becomes ?bbtab=, filters are kept. */
export default function Screen() {
  return <BlackBoxRedirect />;
}
