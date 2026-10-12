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
16. Keep project-created data, backups, caches, build/test temporaries, and test results on D:. Use the workspace scripts `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Run.ps1`. Main workspace: `D:\Code\SmartSQL`; isolated local-assistant workspace: `D:\Code\SmartSQL-model`. Local databases, model weights, and test artifacts must remain ignored by Git. Do not change installed tools or global user settings.
17. Commit completed, verified stages in Git; exclude local data and runtime files.
18. Use parallel subagents for independent important tasks with disjoint file ownership when available. Coordinate integration, builds/tests, and Git in the parent agent.
19. Develop toward a universal database client in the DBeaver direction: common desktop workflows across DBMS providers, including exploration of ordinary existing databases. Retain approachable UI and safe operations.

## Current Stage

Stage 5/6 - Local Desktop UI and CSV/JSON/XLSX exchange.

Avalonia 12 on .NET 10 is the selected cross-platform desktop framework. The local SQLite editor supports typed fields (text, number, date, Boolean), adding/renaming/safely deleting fields and tables, paged CRUD, search/sort, combined reusable saved filters, file selection, manual/automatic backups and validated restore, CSV/JSON/XLSX preview/import/export, and validated bulk paste from Excel. Next prioritize connection management and a first PostgreSQL browsing/provider slice after stabilizing exchange. Experimental local-assistant source remains isolated in `D:\Code\SmartSQL-model`; do not integrate AI or add server mode before desktop workflows are stable.

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
