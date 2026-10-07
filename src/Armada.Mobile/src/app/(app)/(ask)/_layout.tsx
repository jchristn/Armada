import { AskProvider } from '../../../ask/AskContext';
import { TabStack } from '../../../navigation/TabStack';

export const unstable_settings = { initialRouteName: 'ask/index' };

/** The Ask tab: one AskProvider (captains, quick actions, the live thread list) for every Ask screen in the stack. */
export default function Layout() {
  return (
    <AskProvider>
      <TabStack />
    </AskProvider>
  );
}
