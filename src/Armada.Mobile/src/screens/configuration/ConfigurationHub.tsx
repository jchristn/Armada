import { HubScreen, type HubTab } from '../../components/resource/Hub';
import { useLocale } from '../../i18n/LocaleContext';
import { EndpointsTab } from './EndpointsTab';
import { HarborsTab } from './HarborsTab';
import { MemoriesTab } from './MemoriesTab';
import { PersonasTab } from './PersonasTab';
import { PipelinesTab } from './PipelinesTab';
import { PlaybooksTab } from './PlaybooksTab';
import { ProjectProfilesTab } from './ProjectProfilesTab';
import { PromptTemplatesTab } from './PromptTemplatesTab';
import { SkillsTab } from './SkillsTab';
import { WorkflowProfilesTab } from './WorkflowProfilesTab';

/**
 * The Configuration hub (/configuration): the dashboard's ten "how work gets done" tabs in the same order, with the
 * active tab in ?tab= so dashboard links (/personas -> /configuration?tab=personas) land on it.
 */
export function ConfigurationHub() {
  const { t } = useLocale();
  const tabs: HubTab[] = [
    { key: 'workflow-profiles', label: 'Workflow Profiles', render: () => <WorkflowProfilesTab /> },
    { key: 'project-profiles', label: 'Project Profiles', render: () => <ProjectProfilesTab /> },
    { key: 'skills', label: 'Skills', render: () => <SkillsTab /> },
    { key: 'personas', label: 'Personas', render: () => <PersonasTab /> },
    { key: 'pipelines', label: 'Pipelines', render: () => <PipelinesTab /> },
    { key: 'prompts', label: 'Prompts', render: () => <PromptTemplatesTab /> },
    { key: 'playbooks', label: 'Playbooks', render: () => <PlaybooksTab /> },
    { key: 'endpoints', label: 'Endpoints', render: () => <EndpointsTab /> },
    { key: 'harbors', label: 'Harbors', render: () => <HarborsTab /> },
    { key: 'memory', label: 'Memory', render: () => <MemoriesTab /> },
  ];
  return <HubScreen title={t('Configuration')} tabs={tabs} defaultKey="workflow-profiles" label={t('Configuration sections')} testID="configuration" />;
}
