# Roadmap

This roadmap is preliminary and may change as architectural decisions are validated.

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

Status: first usable slice implemented (typed table constructor, grid editing, paging, search, sort, combined reusable saved filters, one-step undo).

## Stage 6 - Import/Export CSV/JSON/XLSX

Implement first import/export adapters and compatibility reporting.

Status: CSV column mapping, preview, import, and export for existing tables, atomic creation of a new typed table from CSV, and typed JSON table exchange with preview implemented. CSV/JSON file export publishes only after success. XLSX remains.

## Stage 7 - PostgreSQL Provider

Add PostgreSQL support through the provider architecture.

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
