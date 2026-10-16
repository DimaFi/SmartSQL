# Roadmap

The stage numbers below are a scope/history map, not a promise of serial delivery. The current execution order, user priorities, parallel work and acceptance criteria are maintained in the Russian [development plan](WORK_PLAN.md). Actual implemented capabilities are in the [matrix](CAPABILITY_MATRIX.md).

## Product Direction and Immediate Priorities

SmartSQL is moving toward a universal desktop database client in the DBeaver direction: one interface for connections, browsing tables, editing records, search/filtering, data exchange, and migration across supported DBMSs. An approachable interface and safe structured actions remain core requirements. Additional providers must eventually support ordinary existing databases, not just SmartSQL-managed projects.

The full current execution sequence is M01–M18 in [WORK_PLAN](WORK_PLAN.md); its next implementation is **M01.01: separate safe opening of an existing managed project from explicit creation**. Shared connection/object/SQL tabs, remembered Simple/Advanced choice, Gray/Light themes, typed native queries and the first native inspector are already implemented, with remaining manual acceptance recorded separately.

The confirmed user priorities include three entry paths (existing DBMS, experienced user without a DBMS, Excel newcomer) and **Google Drive + Yandex Disk automatic backup and recovery**. Safe persistent product data and verified snapshot/restore precede cloud delivery. Native exchange, complete UI polish, reproducible application/installer delivery, localization, further providers and Action/Permission before AI remain in the canonical plan, including dependencies and legal parallel work. This historical Stage map does not defer cloud work until every external database feature is complete.

Development data, caches, backups and test artifacts stay on D:. Calendar dates are not assigned. See WORK_PLAN for dependencies and the distinction between code verification and manual UI acceptance.

## Stage 0 - Project Foundation

Create the solution structure, documentation, dependency direction, and minimal buildable projects.

## Stage 1 - Universal Data Model

Design the first version of the provider-independent structured data model and maintain the minimal Avalonia desktop shell.

## Stage 2 - Provider Abstraction

Define storage provider contracts and capability reporting.

## Stage 3 - SQLite Provider

Implement the default embedded local provider.

## Stage 4 - Basic Local CRUD

Implement create, read, update, and delete operations through application services.

Status: first usable slice implemented (records, individual cells, adding, renaming, and backup-protected removal of fields and tables; type conversion remains).

## Stage 5 - Desktop UI

Build the first usable local interface with Avalonia 12 and an Excel-like data grid workflow.

Status: first usable slice implemented (typed table constructor, grid editing, paging, search, sort, combined reusable saved filters, one-step undo, validated bulk paste from Excel). Semantic Gray/Light themes with a green accent, Russian shell resources and remembered Simple/Advanced modes provide grouped commands and a structure inspector; presentation is separate from data operations. Complete layout/accessibility/manual acceptance remains open.

## Stage 6 - Import/Export CSV/JSON/XLSX

Implement first import/export adapters and compatibility reporting.

Status: CSV column mapping, preview, import, and export for existing tables, atomic creation of a new typed table from CSV, typed JSON exchange, and bounded data-only XLSX exchange for an existing table implemented. XLSX includes worksheet selection, mapping, explicit compatibility warnings, and preview. CSV/JSON/XLSX file export publishes only after success. XLSX formulas/macros/unsupported values are rejected; complex workbook structure is outside the table format.

## Stage 7 - PostgreSQL Provider

PostgreSQL support uses native browsing and explicitly enabled keyed operations without private SmartSQL metadata. Single-row insertion/deletion, typed querying, shared tabs and read-only SQL are implemented; native exchange and broader write workflows remain planned. Unsupported capabilities remain explicit.

Status: native browsing and an explicit single-cell editor implemented: nonsecret connection profiles, ordinary PostgreSQL catalog/table/column inspection, native type/null/PK metadata, pages, literal text search, typed AND filters and native sorting. Editing defaults off, requires a reliable primary key and supported fields, rechecks metadata/permissions/row versions, and rolls back precision loss. Real dedicated PostgreSQL integration covers native values, permissions, conflicts, protected/unsupported metadata and transient handles. Single-row insertion/deletion and ordinary SQLite browsing with a separate opt-in, backup-protected cell UPDATE slice are implemented. Deletion confirms one row with independent DELETE rights and a one-time handle; every incoming foreign key remains blocked. Shared tabs, bounded read-only SQL and object-owned constraints/indexes/definitions are implemented. Native exchange, extended incoming/dependency metadata, broader FK workflows and general undo remain planned; existing UI acceptance is partial.

## Stage 8 - MySQL/MariaDB Provider

Add MySQL and MariaDB support.

## Stage 9 - SQL Server Provider

Add Microsoft SQL Server support.

## Stage 10 - Migration/Compatibility Engine

Detect provider and format capability differences before data movement.

## Stage 11 - Permission/Action Confirmation System

Implement granular permissions, validated actions and confirmation flows before AI-assisted execution.

## Stage 12 - Natural-Language Command Model

Design a structured command model for natural-language requests through the established action boundary.

## Stage 13 - Local Lightweight AI

Investigate and integrate local lightweight AI capabilities only after desktop workflows and permission/action controls are stable.

## Stage 14 - Backup and Recovery

Implement automatic backups, manual backups, restore, and project transfer support.

Status: local SQLite manual backup, automatic safety backup, and validated restore implemented; packaged project transfer remains.

Confirmed future scope: Google Drive and Yandex Disk first, automatic backups and tested recovery, quick status/access, scheduling, retention and version/conflict handling. M05–M08 define verified standalone snapshots, local durable delivery state, both official cloud adapters and separate PostgreSQL backup scope. Completed copies are delivered; a live SQLite working database is not shared through a synchronization folder. Mail/cloud/network/personal-server destinations follow their own verified adapter scope. Credentials and backup contents remain outside Git.

## Stage 15 - Diploma Use Case: Teaching Practice

Validate the system with structured data for organizing university students' teaching practice.
