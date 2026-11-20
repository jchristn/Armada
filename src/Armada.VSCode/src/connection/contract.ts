/*
 * The Admiral contract this build of the extension speaks. The Admiral reports ContractVersion (major.minor) on
 * GET /api/v1/status/health (Armada.Core Constants.ContractVersion); a different major means one side is too old, and
 * the extension refuses to connect with a message that says which. Pure, so it is unit tested directly.
 */

/** The contract major this extension is built for. */
export const SUPPORTED_CONTRACT_MAJOR = 1;

/** Why a contract is not compatible. */
export type ContractProblem = 'serverTooOld' | 'extensionTooOld' | 'unknown';

/** The result of a contract check. */
export interface ContractCheck {
  compatible: boolean;
  problem?: ContractProblem;
  /** Short text for the status bar. */
  summary: string;
  /** Full explanation; empty when compatible. */
  reason: string;
}

/** Checks the server's ContractVersion against SUPPORTED_CONTRACT_MAJOR. */
export function checkContract(serverContractVersion: string | null | undefined, serverVersion?: string | null): ContractCheck {
  const raw = (serverContractVersion ?? '').trim();
  const product = serverVersion ? ` ${serverVersion}` : '';
  if (!raw) {
    return {
      compatible: false,
      problem: 'serverTooOld',
      summary: 'server too old',
      reason: `The Armada Admiral${product} does not report a contract version, so it is older than this extension. Update Armada to use this extension.`,
    };
  }
  const major = /^\d+/.exec(raw) ? Number.parseInt(raw, 10) : Number.NaN;
  if (Number.isNaN(major)) {
    return { compatible: false, problem: 'unknown', summary: 'unknown contract', reason: `The Armada Admiral${product} reported an unreadable contract version ("${raw}").` };
  }
  if (major < SUPPORTED_CONTRACT_MAJOR) {
    return {
      compatible: false,
      problem: 'serverTooOld',
      summary: 'server too old',
      reason: `The Armada Admiral${product} is too old for this extension (contract ${raw}, the extension needs ${SUPPORTED_CONTRACT_MAJOR}.x). Update Armada.`,
    };
  }
  if (major > SUPPORTED_CONTRACT_MAJOR) {
    return {
      compatible: false,
      problem: 'extensionTooOld',
      summary: 'extension too old',
      reason: `This extension is too old for the Armada Admiral${product} (contract ${raw}, the extension supports ${SUPPORTED_CONTRACT_MAJOR}.x). Update the Armada extension.`,
    };
  }
  return { compatible: true, summary: '', reason: '' };
}
