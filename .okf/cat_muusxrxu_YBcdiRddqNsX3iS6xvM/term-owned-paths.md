---
type: "Reference"
title: "Owned paths (mission file ownership, v1.3.0)"
description: "Mission.OwnedPaths lists files or dir/ prefixes (globs * and **) a mission may change; v4 plans reject parallel missions that own the same path; violations are warning-only."
timestamp: "2026-11-05T22:43:46.7175960Z"
created: "2026-11-05T22:43:43.2313487Z"
slug: "term-owned-paths"
category: "cat_muusxrxu_YBcdiRddqNsX3iS6xvM"
links: ["term-logical-mission"]
version: "1"
salience: "0.5"
---
Owned paths (v1.3.0; Mission.OwnedPaths, OwnedPathMatcher, MissionOwnership): the files or dir/ prefixes (globs * and **) a mission may change; readsPaths are context only. Architect plan contract v4 rejects two missions that can run at the same time (neither an ancestor of the other) owning the same path; v3 only warns. Prompts receive {OwnedPaths} and {ConcurrentSiblingPaths} (and the merge-conflict section names them). After a stage, files changed outside owned paths are recorded as an OwnershipViolation stage-outcome warning and the ownershipWarnings scorecard metric; v1.3.0 does not fail the mission. Set from plans, REST/MCP dispatch (ownedPaths) and the dashboard Voyage Create form. See [[term-logical-mission]].
