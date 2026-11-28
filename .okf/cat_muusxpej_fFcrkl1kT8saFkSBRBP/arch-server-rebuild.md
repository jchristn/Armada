---
type: "Project"
title: "Self-rebuild and restart: A/B slots and the replacement-process baton"
description: "ServerRebuildService builds the self vessel at a ref from a detached worktree into <DataDir>/bin/slots/<date>_<sha>, backs up the DB, flips bin/current, and hands over via a detached child that waits on ARMADA_RESTART_WAIT_PID; optional Harbor-supervised health-gated rollback; experimental."
timestamp: "2026-10-05T05:43:36.2640260Z"
created: "2026-10-05T05:43:35.7340280Z"
slug: "arch-server-rebuild"
category: "cat_muusxpej_fFcrkl1kT8saFkSBRBP"
links: ["arch-harbor-link-protocol", "api-experimental-surfaces"]
version: "1"
salience: "0.5"
---
Files: src/Armada.Server/ServerRebuildService.cs, ReplacementProcessLauncher.cs, ServerRebuildRequest.cs; src/Armada.Core/Services/RebuildSlotManager.cs (IRebuildSlotManager; owns <binRoot>/slots and the plain-text <binRoot>/current pointer), RebuildException; Program.WaitForPredecessorExitAsync; docs/SERVER_REBUILD.md.

Restart baton (POST /api/v1/server/restart, `armada server restart`): start a detached child from Environment.ProcessPath with ARMADA_RESTART_WAIT_PID = current PID, then stop; the child waits (bounded) for the predecessor to exit and the port to free before binding.
Rebuild (POST /api/v1/server/rebuild {ref}; status GET /server/rebuild/status persisted in <dataDir>/rebuild-status.json; rollback POST /server/rollback): resolve the source from settings SelfVesselId (vessel WorkingDirectory, works with file:// local clones) -> throwaway git worktree at the branch/tag/commit -> dotnet publish into a new slot named <yyyy-MM-dd>_<short-sha> while the current server keeps serving (dashboard rebuild best-effort into the shared <dataDir>/dashboard) -> only a successful publish proceeds -> database backup -> flip current -> baton into the new slot. Prunes older slots and their pre-rebuild backups to RebuildSlotRetentionCount (3). With RebuildSupervisorHarborId set and connected, the cutover is delegated via the Harbor deferredLaunch message for health-gated rollback (launch new slot, poll health, revert pointer and launch the fallback on failure). Server control routes always require an admin credential. Excluded from the 1.0 compatibility promise (experimental).
