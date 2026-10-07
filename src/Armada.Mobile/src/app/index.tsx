import { Redirect } from 'expo-router';

/** The app opens into Ask Armada (MOBILE_APP_PLAN.md design principle 1); the dashboard Home is /home. */
export default function Index() {
  return <Redirect href="/ask" />;
}
