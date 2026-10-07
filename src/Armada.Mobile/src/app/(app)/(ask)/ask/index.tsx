import { AskScreen } from '../../../../screens/AskScreen';

/** /ask: the new-conversation screen, or the last conversation on the first visit of an app session. */
export default function AskIndexRoute() {
  return <AskScreen routeThreadId={null} />;
}
