---
type: "Reference"
title: "Voyage slot and per-voyage mission limit (v1.3.0)"
description: "Vessel MaxParallelVoyages (default 1) limits admitted voyages; MaxParallelMissionsPerVoyage (default 4) limits running missions per voyage; 0 = unlimited; slots are sticky through review/landing."
timestamp: "2026-11-05T22:43:32.9649577Z"
created: "2026-11-05T22:43:32.6816286Z"
slug: "term-voyage-slot"
category: "cat_muusxrxu_YBcdiRddqNsX3iS6xvM"
links: ["term-captain-slot", "term-logical-mission"]
version: "1"
salience: "0.5"
---
Voyage slot (v1.3.0; docs/SCHEDULING.md#capacity): a vessel admits at most MaxParallelVoyages voyages (default 1; a standalone mission counts as its own voyage) and runs at most MaxParallelMissionsPerVoyage missions of one voyage at a time (default 4); 0 means unlimited. An admitted voyage keeps its slot (sticky) through verification, review, landing and between stages until it settles; LandingFailed and Blocked missions do not keep it. Blockers: VesselVoyageLimit (gate VesselVoyageSlot) and VoyageMissionLimit (gate VoyageMissionSlot). Replaces the removed Vessel.AllowConcurrentMissions (migration 117 backfilled false to 1/1, true to 0/0). Live view: GET /api/v1/vessels/{id}/concurrency, MCP vessel_concurrency. See [[term-captain-slot]].
