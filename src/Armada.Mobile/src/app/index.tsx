import { Redirect, type Href } from 'expo-router';
import { useState } from 'react';
import { takePendingLink } from '../navigation/pendingLink';

/**
 * Where the app lands once signed in. A deep link that arrived before the session was ready (a cold start from a
 * link or notification, or a link opened while signed out) wins; otherwise the app opens into Ask Armada
 * (MOBILE_APP_PLAN.md design principle 1). The dashboard Home is /home.
 */
export default function Index() {
  const [target] = useState(() => takePendingLink() ?? '/ask');
  return <Redirect href={target as Href} />;
}
