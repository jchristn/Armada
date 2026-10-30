/**
 * Titles for the delete progress modal (components/shared/DeleteProgressDialog), one pair per kind of record the
 * dashboard deletes: the singular title for a single delete and an ICU plural title for a bulk delete. Every
 * literal here goes through the translator, so the catalog scanner (lib/deleteProgressCatalog.test.ts) checks that
 * each one is translated. The TUI uses the same English phrases (Armada.Tui/Screens/Kit/DeleteProgress.cs).
 */

type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

/** A kind of record the dashboard deletes or purges. */
export type DeleteProgressKind =
  | 'fleet' | 'vessel' | 'captain' | 'mission' | 'missionPurge' | 'voyagePurge' | 'dock' | 'signal' | 'event'
  | 'mergeEntry' | 'persona' | 'pipeline' | 'playbook' | 'workflowProfile' | 'projectProfile' | 'skill' | 'harbor'
  | 'modelEndpoint' | 'memory' | 'fleetAction' | 'cliPermissionRule' | 'conversation' | 'planningSession'
  | 'refinementSession' | 'backlogItem' | 'deployment' | 'environment' | 'incident' | 'release' | 'runbook'
  | 'checkRun' | 'requestHistoryEntry' | 'workspaceEntry' | 'tenant' | 'user' | 'credential' | 'blackBoxEntry';

/** The default progress message: plain enough to be true for every kind. */
export function deleteProgressMessage(t: Translate): string {
  return t('This may take some time while related records are cleaned up.');
}

/**
 * Title for a delete in progress: "Deleting fleet" for a single record, "Deleting 12 fleets" when a count is given
 * (bulk deletes). Purges say "Purging".
 */
export function deleteProgressTitle(t: Translate, kind: DeleteProgressKind, count?: number): string {
  if (count === undefined) return singular(t, kind);
  return plural(t, kind, count);
}

/** Title for deleting every request history entry that matches the current filter (the count is not known). */
export function deleteMatchingRequestHistoryTitle(t: Translate): string {
  return t('Deleting matching request history entries');
}

function singular(t: Translate, kind: DeleteProgressKind): string {
  switch (kind) {
    case 'fleet': return t('Deleting fleet');
    case 'vessel': return t('Deleting vessel');
    case 'captain': return t('Deleting captain');
    case 'mission': return t('Deleting mission');
    case 'missionPurge': return t('Purging mission');
    case 'voyagePurge': return t('Purging voyage');
    case 'dock': return t('Deleting dock');
    case 'signal': return t('Deleting signal');
    case 'event': return t('Deleting event');
    case 'mergeEntry': return t('Deleting merge queue entry');
    case 'persona': return t('Deleting persona');
    case 'pipeline': return t('Deleting pipeline');
    case 'playbook': return t('Deleting playbook');
    case 'workflowProfile': return t('Deleting workflow profile');
    case 'projectProfile': return t('Deleting project profile');
    case 'skill': return t('Deleting skill');
    case 'harbor': return t('Deleting harbor');
    case 'modelEndpoint': return t('Deleting model endpoint');
    case 'memory': return t('Deleting memory');
    case 'fleetAction': return t('Deleting fleet action');
    case 'cliPermissionRule': return t('Deleting CLI permission rule');
    case 'conversation': return t('Deleting conversation');
    case 'planningSession': return t('Deleting planning session');
    case 'refinementSession': return t('Deleting refinement session');
    case 'backlogItem': return t('Deleting backlog item');
    case 'deployment': return t('Deleting deployment');
    case 'environment': return t('Deleting environment');
    case 'incident': return t('Deleting incident');
    case 'release': return t('Deleting release');
    case 'runbook': return t('Deleting runbook');
    case 'checkRun': return t('Deleting check run');
    case 'requestHistoryEntry': return t('Deleting request history entry');
    case 'workspaceEntry': return t('Deleting workspace entry');
    case 'tenant': return t('Deleting tenant');
    case 'user': return t('Deleting user');
    case 'credential': return t('Deleting credential');
    case 'blackBoxEntry': return t('Deleting black box entry');
  }
}

function plural(t: Translate, kind: DeleteProgressKind, count: number): string {
  const p = { count };
  switch (kind) {
    case 'fleet': return t('{count, plural, one {Deleting # fleet} other {Deleting # fleets}}', p);
    case 'vessel': return t('{count, plural, one {Deleting # vessel} other {Deleting # vessels}}', p);
    case 'captain': return t('{count, plural, one {Deleting # captain} other {Deleting # captains}}', p);
    case 'mission': return t('{count, plural, one {Deleting # mission} other {Deleting # missions}}', p);
    case 'missionPurge': return t('{count, plural, one {Purging # mission} other {Purging # missions}}', p);
    case 'voyagePurge': return t('{count, plural, one {Purging # voyage} other {Purging # voyages}}', p);
    case 'dock': return t('{count, plural, one {Deleting # dock} other {Deleting # docks}}', p);
    case 'signal': return t('{count, plural, one {Deleting # signal} other {Deleting # signals}}', p);
    case 'event': return t('{count, plural, one {Deleting # event} other {Deleting # events}}', p);
    case 'mergeEntry': return t('{count, plural, one {Deleting # merge queue entry} other {Deleting # merge queue entries}}', p);
    case 'persona': return t('{count, plural, one {Deleting # persona} other {Deleting # personas}}', p);
    case 'pipeline': return t('{count, plural, one {Deleting # pipeline} other {Deleting # pipelines}}', p);
    case 'playbook': return t('{count, plural, one {Deleting # playbook} other {Deleting # playbooks}}', p);
    case 'workflowProfile': return t('{count, plural, one {Deleting # workflow profile} other {Deleting # workflow profiles}}', p);
    case 'projectProfile': return t('{count, plural, one {Deleting # project profile} other {Deleting # project profiles}}', p);
    case 'skill': return t('{count, plural, one {Deleting # skill} other {Deleting # skills}}', p);
    case 'harbor': return t('{count, plural, one {Deleting # harbor} other {Deleting # harbors}}', p);
    case 'modelEndpoint': return t('{count, plural, one {Deleting # model endpoint} other {Deleting # model endpoints}}', p);
    case 'memory': return t('{count, plural, one {Deleting # memory} other {Deleting # memories}}', p);
    case 'fleetAction': return t('{count, plural, one {Deleting # fleet action} other {Deleting # fleet actions}}', p);
    case 'cliPermissionRule': return t('{count, plural, one {Deleting # CLI permission rule} other {Deleting # CLI permission rules}}', p);
    case 'conversation': return t('{count, plural, one {Deleting # conversation} other {Deleting # conversations}}', p);
    case 'planningSession': return t('{count, plural, one {Deleting # planning session} other {Deleting # planning sessions}}', p);
    case 'refinementSession': return t('{count, plural, one {Deleting # refinement session} other {Deleting # refinement sessions}}', p);
    case 'backlogItem': return t('{count, plural, one {Deleting # backlog item} other {Deleting # backlog items}}', p);
    case 'deployment': return t('{count, plural, one {Deleting # deployment} other {Deleting # deployments}}', p);
    case 'environment': return t('{count, plural, one {Deleting # environment} other {Deleting # environments}}', p);
    case 'incident': return t('{count, plural, one {Deleting # incident} other {Deleting # incidents}}', p);
    case 'release': return t('{count, plural, one {Deleting # release} other {Deleting # releases}}', p);
    case 'runbook': return t('{count, plural, one {Deleting # runbook} other {Deleting # runbooks}}', p);
    case 'checkRun': return t('{count, plural, one {Deleting # check run} other {Deleting # check runs}}', p);
    case 'requestHistoryEntry': return t('{count, plural, one {Deleting # request history entry} other {Deleting # request history entries}}', p);
    case 'workspaceEntry': return t('{count, plural, one {Deleting # workspace entry} other {Deleting # workspace entries}}', p);
    case 'tenant': return t('{count, plural, one {Deleting # tenant} other {Deleting # tenants}}', p);
    case 'user': return t('{count, plural, one {Deleting # user} other {Deleting # users}}', p);
    case 'credential': return t('{count, plural, one {Deleting # credential} other {Deleting # credentials}}', p);
    case 'blackBoxEntry': return t('{count, plural, one {Deleting # black box entry} other {Deleting # black box entries}}', p);
  }
}
