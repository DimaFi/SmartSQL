# Roadmap

This roadmap is preliminary and may change as architectural decisions are validated.

## Product Direction and Immediate Priorities

SmartSQL is moving toward a universal desktop database client in the DBeaver direction: one interface for connections, browsing tables, editing records, search/filtering, data exchange, and migration across supported DBMSs. An approachable interface and safe structured actions remain core requirements. Additional providers must eventually support ordinary existing databases, not just SmartSQL-managed projects.

The current checked sequence is:

1. Keep development, local data, model experiments, and test artifacts on D: through the workspace scripts; keep local files out of Git.
2. Stabilize CSV/JSON/XLSX exchange and bulk paste with preview and compatibility reporting.
3. Add connection management and a first PostgreSQL slice that connects to an existing database, lists schemas/tables, inspects columns/types/keys, and pages records. Begin with read-only browsing; enable editing with supported types and reliable row identities.
4. Extend provider-independent editing, relationships, and safe type changes according to concrete use cases; then add MySQL/MariaDB and SQL Server.
5. Validate migration compatibility and then the permission/action boundary before integrating AI mutations.

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

Status: first usable slice implemented (typed table constructor, grid editing, paging, search, sort, combined reusable saved filters, one-step undo, validated bulk paste from Excel).

## Stage 6 - Import/Export CSV/JSON/XLSX

Implement first import/export adapters and compatibility reporting.

Status: CSV column mapping, preview, import, and export for existing tables, atomic creation of a new typed table from CSV, typed JSON exchange, and bounded data-only XLSX exchange for an existing table implemented. XLSX includes worksheet selection, mapping, explicit compatibility warnings, and preview. CSV/JSON/XLSX file export publishes only after success. XLSX formulas/macros/unsupported values are rejected; complex workbook structure is outside the table format.

## Stage 7 - PostgreSQL Provider

Add PostgreSQL support through the provider architecture. First deliver an existing-database connection and read-only catalog/record browser, without assuming private SmartSQL metadata. Report unsupported types/features; follow with parameterized editing and safe operations where reliable keys are available.

## Stage 8 - MySQL/MariaDB Provider

Add MySQL and MariaDB support.

## Stage 9 - SQL Server Provider

Add Microsoft SQL Server support.

## Stage 10 - Migration/Compatibility Engine

Detect provider and format capability differences before data movement.

## Stage 11 - Natural-Language Command Model

Design a structured command model for natural-language requests.

## Stage 12 - Local Lightweight AI

Investigate and integrate local lightweight AI capabilities where practical.

## Stage 13 - Permission/Action Confirmation System

Implement granular permissions and confirmation flows for AI-assisted actions.

## Stage 14 - Backup and Recovery

Implement automatic backups, manual backups, restore, and project transfer support.

Status: local SQLite manual backup, automatic safety backup, and validated restore implemented; packaged project transfer remains.

## Stage 15 - Diploma Use Case: Teaching Practice

Validate the system with structured data for organizing university students' teaching practice.
