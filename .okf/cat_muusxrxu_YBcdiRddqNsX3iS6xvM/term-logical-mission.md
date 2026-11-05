---
type: "Reference"
title: "Logical mission, parent and child (mission hierarchy, v1.3.0)"
description: "A logical mission is a root mission plus its pipeline stages; ParentMissionId makes it a child that starts after the parent's last stage and builds on its branch (ContinueParent/ForkFromParent)."
timestamp: "2026-11-05T22:43:38.9631554Z"
created: "2026-11-05T22:43:38.6799897Z"
slug: "term-logical-mission"
category: "cat_muusxrxu_YBcdiRddqNsX3iS6xvM"
links: ["term-owned-paths", "term-voyage-slot"]
version: "1"
salience: "0.5"
---
Logical mission (v1.3.0; docs/PIPELINES.md#mission-hierarchy): one unit of work = its root mission plus the later pipeline stages chained by DependsOnMissionId. A child has ParentMissionId set to another logical mission's root (same voyage, or same vessel when standalone; depth <= Concurrency.MaxHierarchyDepth, 4). The child waits with ParentNotFinished until the parent's last stage finishes; siblings run in parallel. BranchStrategy New | ContinueParent | ForkFromParent: by default the first child continues the parent's branch and later children fork from its head. A child lands only after its parent (WaitingForParentLanding); a failed/cancelled parent cancels Pending descendants (ParentFailed), restart restores them. Dispatch sets it with Key/ParentKey; architect plan contract v4 with parent. Rescue links use RescueOfMissionId, not ParentMissionId. View: GET /api/v1/voyages/{id}/tree (MCP voyage_tree), dashboard/TUI/Mobile voyage Mission Tree. Shared tree rules: MissionTreeBuilder (a dependent is a stage only if it is the sole dependent or shares the base title). See [[term-owned-paths]].
