import { describe, expect, it } from 'vitest';
import missionStatusSource from '../../../Armada.Core/Enums/MissionStatusEnum.cs?raw';
import deploymentStatusSource from '../../../Armada.Core/Enums/DeploymentStatusEnum.cs?raw';
import verificationStatusSource from '../../../Armada.Core/Enums/DeploymentVerificationStatusEnum.cs?raw';
import { deploymentSeverity, entityStatusSeverity } from './notificationSeverity';

/** Member names of a C# enum source file. */
function enumMembers(source: string): string[] {
  const body = source.slice(source.indexOf('{', source.indexOf(' enum ')) + 1);
  return [...body.matchAll(/^\s*([A-Z][A-Za-z0-9]*)\s*(?:=\s*\d+\s*)?,?\s*$/gm)].map((m) => m[1]);
}

describe('notification severity', () => {
  it('treats a succeeded deployment whose verification failed as an error (was success when matched by substring)', () => {
    expect(deploymentSeverity('Succeeded', 'Failed')).toBe('error');
    expect(entityStatusSeverity('Deployment', 'Succeeded', 'Failed')).toBe('error');
  });

  it('combines deployment status and verification status, the worse one wins', () => {
    expect(deploymentSeverity('Succeeded', 'Passed')).toBe('success');
    expect(deploymentSeverity('Succeeded', null)).toBe('success');
    expect(deploymentSeverity('Succeeded', 'Partial')).toBe('warning');
    expect(deploymentSeverity('VerificationFailed', 'Failed')).toBe('error');
    expect(deploymentSeverity('RolledBack', 'Passed')).toBe('warning');
    expect(deploymentSeverity('Running', 'NotRun')).toBe('info');
  });

  it('maps exact enum values per entity kind', () => {
    expect(entityStatusSeverity('Mission', 'Complete')).toBe('success');
    expect(entityStatusSeverity('Mission', 'LandingFailed')).toBe('error');
    expect(entityStatusSeverity('Mission', 'Cancelled')).toBe('warning');
    expect(entityStatusSeverity('Mission', 'InProgress')).toBe('info');
    expect(entityStatusSeverity('Voyage', 'Failed')).toBe('error');
    expect(entityStatusSeverity('Captain', 'Stalled')).toBe('warning');
    expect(entityStatusSeverity('Captain', 'Idle')).toBe('info');
    expect(entityStatusSeverity('Objective', 'Completed')).toBe('success');
    expect(entityStatusSeverity('Incident', 'RolledBack')).toBe('warning');
  });

  it('does not classify by substring', () => {
    // A value that merely contains a known word is not that value.
    expect(entityStatusSeverity('Mission', 'NotFailed')).toBe('info');
    expect(entityStatusSeverity('Mission', 'Succeeded / Failed')).toBe('info');
    expect(entityStatusSeverity('Mission', 'complete')).toBe('info');
    expect(entityStatusSeverity('Mission', null)).toBe('info');
  });

  it('every server mission and deployment enum value has a defined severity (no throw, typed result)', () => {
    for (const value of enumMembers(missionStatusSource)) {
      expect(['info', 'success', 'warning', 'error']).toContain(entityStatusSeverity('Mission', value));
    }
    for (const status of enumMembers(deploymentStatusSource)) {
      for (const verification of enumMembers(verificationStatusSource)) {
        expect(['info', 'success', 'warning', 'error']).toContain(deploymentSeverity(status, verification));
      }
    }
    expect(enumMembers(missionStatusSource)).toContain('LandingFailed');
    expect(enumMembers(verificationStatusSource)).toContain('Partial');
  });
});
