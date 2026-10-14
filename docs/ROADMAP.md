# Roadmap

The stage numbers below are a scope/history map, not a promise of serial delivery. The current execution order, user priorities, parallel work and acceptance criteria are maintained in the Russian [development plan](WORK_PLAN.md). Actual implemented capabilities are in the [matrix](CAPABILITY_MATRIX.md).

## Product Direction and Immediate Priorities

SmartSQL is moving toward a universal desktop database client in the DBeaver direction: one interface for connections, browsing tables, editing records, search/filtering, data exchange, and migration across supported DBMSs. An approachable interface and safe structured actions remain core requirements. Additional providers must eventually support ordinary existing databases, not just SmartSQL-managed projects.

The user currently prioritizes comfortable PostgreSQL/SQLite workflows, both interface modes, a first-run choice and a permanent switch. A shared tabbed main window is the chosen future direction. There is no calendar deadline. The checked next sequence is:

1. Accept the current PostgreSQL editing UI; add safe native INSERT/DELETE capabilities. Develop ordinary SQLite browsing independently, without managed metadata migration.
2. Integrate connection/object tabs, typed native filters/sorting, and supported ordinary SQLite editing. Complete Russian workflows and refine both modes.
3. Add bounded manual SQL for reading, native structure inspection and external data exchange with compatibility reporting; deliver a usable PostgreSQL/SQLite release.
4. Complete English localization; add MySQL/MariaDB, then SQL Server through verified provider slices.
5. Expand relationships, schema/type changes and migration. Validate diploma scenarios and compatibility throughout. Implement Action/Permission before integrated AI; add external backup destinations after stable database workflows.

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

Status: first usable slice implemented (typed table constructor, grid editing, paging, search, sort, combined reusable saved filters, one-step undo, validated bulk paste from Excel). A shared light green theme, Russian shell resources and simple/expanded modes provide grouped commands and a structure inspector; presentation is separate from data operations.

## Stage 6 - Import/Export CSV/JSON/XLSX

Implement first import/export adapters and compatibility reporting.

Status: CSV column mapping, preview, import, and export for existing tables, atomic creation of a new typed table from CSV, typed JSON exchange, and bounded data-only XLSX exchange for an existing table implemented. XLSX includes worksheet selection, mapping, explicit compatibility warnings, and preview. CSV/JSON/XLSX file export publishes only after success. XLSX formulas/macros/unsupported values are rejected; complex workbook structure is outside the table format.

## Stage 7 - PostgreSQL Provider

PostgreSQL support uses native browsing and explicitly enabled keyed updates without private SmartSQL metadata. The next slices add safe inserts/deletes, typed querying and native exchange; unsupported capabilities remain explicit.

Status: native browsing and an explicit single-cell editor implemented: nonsecret connection profiles, ordinary PostgreSQL catalog/table/column inspection, native type/null/PK metadata, pages, literal text search and explicit text sorting. Editing defaults off, requires a reliable primary key and supported fields, rechecks metadata/permissions/row versions, and rolls back precision loss. Real dedicated PostgreSQL integration covers native values, permissions, conflicts, protected/unsupported metadata and transient handles. External inserts/deletes and a general undo workflow remain next.

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

Future scope requested by the user: cloud drives and personal servers as backup destinations, with basic destination/access/schedule/retention settings, integrity checks and tested restore. Implement after external database workflows are stable; local synchronized folders/network storage can precede direct cloud/protocol adapters. Credentials and backup contents remain outside Git.

## Stage 15 - Diploma Use Case: Teaching Practice

Validate the system with structured data for organizing university students' teaching practice.
