---
type: "Reference"
title: "Captain slot (MaxConcurrency, v1.3.0)"
description: "A captain runs up to MaxConcurrency missions at once, one per slot: CLI runtimes default 1, ApiEndpoint/Mux 4; capped by Concurrency.MaxCaptainConcurrency (256)."
timestamp: "2026-11-05T22:43:30.1220790Z"
created: "2026-11-05T22:43:27.7534191Z"
slug: "term-captain-slot"
category: "cat_muusxrxu_YBcdiRddqNsX3iS6xvM"
links: ["term-voyage-slot", "term-logical-mission"]
version: "1"
salience: "0.5"
---
Captain slot (v1.3.0; src/Armada.Core/Models/Captain.cs, docs/SCHEDULING.md#capacity): each captain has MaxConcurrency slots, one per running (Assigned/InProgress) mission. Defaults: 1 for CLI runtimes (ClaudeCode, Codex, Gemini, Cursor, OpenCode, pi), 4 for ApiEndpoint and Mux; capped by Concurrency.MaxCaptainConcurrency (256). The slot is claimed atomically with the mission's move to Assigned and released when the agent finishes; verification, review and landing hold no captain slot. Captain.ActiveMissionIds lists the running missions (CurrentMissionId/CurrentDockId were removed in migration 121); each mission owns its dock and process. A full captain set makes a mission wait with CaptainConcurrencyLimit. Routing prefers the captain with the fewest active missions. Planning/refinement/analysis sessions are exclusive (CurrentSessionId) and need zero active missions. Endpoint, runtime and global caps count missions, not captains. See [[term-voyage-slot]].
