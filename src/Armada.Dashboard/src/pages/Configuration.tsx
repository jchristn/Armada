import { lazy, Suspense, type ReactNode } from 'react';
import Tabs, { type TabDef } from '../components/shared/Tabs';

const WorkflowProfiles = lazy(() => import('./WorkflowProfiles'));
const ProjectProfiles = lazy(() => import('./ProjectProfiles'));
const Skills = lazy(() => import('./Skills'));
const Personas = lazy(() => import('./Personas'));
const Pipelines = lazy(() => import('./Pipelines'));
const PromptTemplates = lazy(() => import('./PromptTemplates'));
const Playbooks = lazy(() => import('./Playbooks'));
const Endpoints = lazy(() => import('./Endpoints'));
const Harbors = lazy(() => import('./Harbors'));

function panel(node: ReactNode): ReactNode {
  return <Suspense fallback={<p className="text-dim" style={{ padding: '1rem' }}>Loading...</p>}>{node}</Suspense>;
}

/**
 * Configuration hub. Folds the seven "how work gets done" setup surfaces
 * (Workflow Profiles, Project Profiles, Skills, Personas, Pipelines, Prompts,
 * Playbooks) into one tabbed page so rarely-touched configuration stops
 * competing for nav slots with daily-driver work. Each tab lazy-mounts the
 * existing page component; their detail routes are unchanged.
 */
export default function Configuration() {
  const tabs: TabDef[] = [
    { key: 'workflow-profiles', label: 'Workflow Profiles', render: () => panel(<WorkflowProfiles />) },
    { key: 'project-profiles', label: 'Project Profiles', render: () => panel(<ProjectProfiles />) },
    { key: 'skills', label: 'Skills', render: () => panel(<Skills />) },
    { key: 'personas', label: 'Personas', render: () => panel(<Personas />) },
    { key: 'pipelines', label: 'Pipelines', render: () => panel(<Pipelines />) },
    { key: 'prompts', label: 'Prompts', render: () => panel(<PromptTemplates />) },
    { key: 'playbooks', label: 'Playbooks', render: () => panel(<Playbooks />) },
    { key: 'endpoints', label: 'Endpoints', render: () => panel(<Endpoints />) },
    { key: 'harbors', label: 'Harbors', render: () => panel(<Harbors />) },
  ];

  return <Tabs tabs={tabs} defaultTabKey="workflow-profiles" ariaLabel="Configuration sections" />;
}
