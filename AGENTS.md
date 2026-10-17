# AGENTS.md

## Project Goal

SmartSQL is a universal intelligent system for managing structured data.

University teaching-practice management is the primary diploma validation scenario, not an architectural limitation. The core must remain suitable for schools, small organizations, customers, employees, warehouses, projects, catalogs, equipment inventory, and other structured data.

## Main Rules

1. Do not bind the core to a specific DBMS.
2. Do not place SQL in Domain/Application without architectural necessity.
3. Do not give AI direct unrestricted access to SQL.
4. Future AI actions must pass through the Action/Permission layer.
5. SQLite is the default local provider, not the system core.
6. Import and export should be as symmetrical as possible.
7. Do not create vendor lock-in.
8. If formats or DBMS capabilities are incompatible, data must not be silently lost.
9. Any loss of capabilities during migration must be detected and reported to the user.
10. Do not overcomplicate the architecture ahead of concrete needs.
11. Do not add technologies "for the future" without a current task.
12. Make small, verifiable changes.
13. Run build/tests after each meaningful stage.
14. Do not silently change accepted architectural decisions.
15. Record significant architectural decisions in `docs/DECISIONS.md`.
16. Keep project-created data, backups, caches, build/test temporaries, and test results on D:. Use the workspace scripts `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Run.ps1`. Main workspace: `D:\Code\SmartSQL`; isolated ML workspace: `D:\Code\SmartSQL-model`, future remote `DimaFi/SmartSQL-ML`. All model source/docs/scripts, data, weights, caches, temporaries and results belong inside that model workspace and use its own scripts/configuration. Local databases, model weights, and test artifacts must remain ignored by Git. Do not change installed tools or global user settings.
17. Commit completed, verified stages in Git; exclude local data and runtime files.
18. Use parallel subagents for independent important tasks with disjoint file ownership when available. Coordinate integration, builds/tests, and Git in the parent agent.
19. Develop toward a universal database client in the DBeaver direction: common desktop workflows across DBMS providers, including exploration of ordinary existing databases. Retain approachable UI and safe operations.
20. Normal delegated-work preferences: `gpt-5.6-sol` high or `gpt-6.1-sol` medium; no Astra/extra-high. Temporary user-requested economy override is active ONLY from 2026-10-17T08:36:50Z through (exclusive) 2026-10-18T04:36:50Z (Europe/Istanbul: 17 Oct 11:36:50 to 18 Oct 07:36:50): use `gpt-6-luna` for simple checks and `gpt-5.6-sol` medium for bounded coding/analysis, short explicit briefs without full history, independent exact file ownership. After expiresAt the override automatically has no effect and normal preferences apply unless superseded by the user. Parent integrates/builds/tests/Git. Thread automation smartsql is a one-time expiration reminder, not authorization to resume paused product work.
21. Before continuing in another chat, read `docs/HANDOFF.md`, `docs/TASKS.md`, and `docs/WORK_PLAN.md`, then inspect Git status/history. Update verified progress and remaining UI acceptance after each stage. Publish authorized commits/plans to the configured GitHub remote when authentication is available; never claim publication until the remote commit is verified.

22. As verified product/provider workflows evolve, maintain public architecture/capability snapshots, synthetic guidance examples and separate holdout cases in D:\Code\SmartSQL-model\knowledge using model-local export/validation scripts. Pin clean product commit/hashes, distinguish implemented/partial/planned, exclude real user data/secrets/history, and keep runtime Action/Permission authoritative. This is data preparation only; do not start training/download weights or integrate AI without its planned stage.

## Current Stage

Stage 7 - Local SQLite editor, native SQLite browsing, portable exchange and guarded PostgreSQL row operations.

Avalonia 12 on .NET 10 is the selected cross-platform desktop framework. The application now uses one `WorkspaceWindow` with a shared connection tree and central tabs. The managed SQLite editor is embedded without its old navigation, while native PostgreSQL/SQLite tables use independent provider sessions per tab; identical table names from different connections remain distinct, and closing a tab cancels its in-flight load. Simple/Advanced mode is chosen on first run, saved atomically in local JSON and switched globally without changing database permissions. Tabs and credentials are not persisted. The local SQLite editor supports typed fields, schema editing, paged CRUD, filters, backups/restore and CSV/JSON/XLSX/clipboard exchange. PostgreSQL supports native metadata, paging and guarded single-cell update plus single-row insertion/deletion; all incoming foreign keys remain blocked for deletion. On 15 October the user authorized screen control for scoped SmartSQL testing with synthetic data on D:. Partial UI acceptance verified first-run Simple choice, remembered mode, local search preservation, distinct same-name SQLite tabs with independent page state, keyboard/mouse tab workflows, isolated disconnect, and the corrected managed-table header/summary. Typed AND filters and native sorting (T09) now run before pagination with provider-confirmed types, exact input and explicit SQLite storage validation. Current Debug has 0 warnings; all 638 tests pass (1/129/469/39), including 60 PostgreSQL facts with zero skips. A self-contained Windows x64 EXE/ZIP is published locally under sibling D:\Code\SmartSQL-builds; refined user-supplied S branding is embedded in executable/window icons and visible on the taskbar. Installer source is prepared, but ISCC is absent and no installer was built. T04 remains open for PostgreSQL writes/conflicts and minimum-size, high-DPI and Narrator checks; see `docs/WORKSPACE_UI_ACCEPTANCE.md`. Native SQLite now supports an explicit opt-in single-cell UPDATE slice with confirmed PK/raw-row/schema checks, BEGIN IMMEDIATE and mandatory verified prewrite backups on D:; complex constraints remain read-only and no INSERT/DELETE or general undo is provided. T10 partial UI acceptance confirmed one changed cell, a valid prewrite copy and CHECK-table refusal. T11 now adds independent read-only SQL tabs, provider-native single-statement enforcement, exact bounded results, cancellation/draining, local bounded history and UTF-8 SQL files. SQLite SQL is initially UTF-8 only and conservatively rejects unconfirmed columnless CTE reads. Partial UI acceptance covers native SQLite results, mode preservation, history, timeout and recovery. On 16 October gray/light themes with a pinned top-right button and atomic remembered choice were added, along with editable native SQL highlighting without packages. Theme changes preserve SQL/results; manual checks confirmed full oversized-input rejection, Cancel before the deadline and recovery. Native window decorations are explicit; user confirmed caption buttons returned. T11 cleanup now drains/releases even after cancellation callback failures; native SQL file pickers explicitly prevent tab/disconnect/shutdown until dismissed. Independent SQLite/PostgreSQL session isolation is tested. New screen acceptance is unavailable because the computer-use tool fails initialization; do not claim it passed. T12 now implements a bounded read-only inspector of object-owned constraints/indexes/native definitions with explicit coverage, unknown flags and strict SQLite metadata decoding; successful reads preserve current edit handles. Incoming FKs/domain constraints/dependency graphs are outside scope; write/delete guards are unchanged. T12 manual UI acceptance remains open alongside T11/T04/T23. A user-entry/cloud audit now records three entry paths and the confirmed Google Drive/Yandex Disk automatic-backup-and-recovery priority. Cloud delivery is not implemented. The audit proposes safe open/stable product data, Excel onboarding and verified snapshot/restore before automatic cloud delivery; current D: development policy and remaining UI acceptance are unchanged. docs/WORK_PLAN.md is now the sole consolidated execution plan: M01-M18/123 subtasks mapped to stable T01-T25, with dependencies/acceptance/agent ownership. M01.01 explicit CreateNew/OpenExisting now passes code/tests (24 added cases), rejects missing/empty/foreign targets without adoption, and uses ReadWrite without recreate fallback; manual acceptance and full independent final review remain open. M01.02 now validates managed metadata/physical schema/raw typed data and requires a fingerprint-matched standalone SQLite backup (including committed WAL) before upgrade; reports versions/path/SHA. Debug 676 tests pass (1/144/492/39), 60 real PG facts, zero skips/warnings/errors; independent bounded review found no blocker. Manual UI remains open after screen-tool initialization failure. Clean preview.8 source 1ac1480c is locally published; isolated ML knowledge is updated at local 310e14d with no training/inference. M01.03/D056 PRODUCT_STORAGE.md now records the stable installed/explicit portable/override root contract; no runtime change yet. Next implementation is M01.04 unified resolver and startup errors; product paths need a separate recorded decision, manual acceptance remains open, and T13 can proceed later in parallel after its own prerequisites. See docs/USER_ENTRY_AND_CLOUD_AUDIT.md; see docs/DELIVERY.md for reproducible local application publication. Manual read-only SQL is integrated; AI is not integrated; experimental assistant work remains isolated in `D:\Code\SmartSQL-model`.

## Dependency Direction

Allowed:

```text
Domain
↑
Application
↑
Infrastructure

Domain/Application
↑
Desktop
```

Forbidden:

```text
Domain -> Infrastructure
Domain -> Desktop
Application -> Desktop
cyclic dependencies
```
