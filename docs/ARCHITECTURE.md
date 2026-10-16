# Architecture

## Current Scope

This document records the current architecture and planned extensions.

The universal model, editable managed SQLite provider, typed fields, desktop CRUD/filters, CSV/JSON/XLSX table exchange, clipboard paste, and local backup/restore are implemented. Native PostgreSQL browsing, explicitly enabled keyed cell updates and independent single-row insertion/deletion are implemented. Ordinary SQLite browsing has a separate opt-in, backup-protected single-cell UPDATE slice; native INSERT/DELETE remain future work. Managed and native workflows share WorkspaceWindow/tree/tabs but retain separate contracts. Typed native queries, bounded read-only SQL and an object-owned native inspector are implemented. Broader relationships, type conversion, other providers and integrated AI remain later stages. Existing manual acceptance is partial; see the [capability matrix](CAPABILITY_MATRIX.md) and [development plan](WORK_PLAN.md).

The product includes a common database-client interface for ordinary existing databases across supported DBMSs. `IDataWorkspace` owns managed metadata and physical tables; `IDatabaseBrowser` supplies native metadata and pages without managed identifiers or type conversion. `IDatabaseEditor` adds optional provider-confirmed cell updates; `IDatabaseRowCreator` and `IDatabaseRowDeleter` add independent single-row insertion/deletion. None implies another write capability or schema editing. Native provider differences remain explicit.

## Product Responsibilities and Runtime Commands

The user reaffirmed on 16 October 2026 that SmartSQL simplifies and automates work with established DBMSs; it does not implement a new DBMS engine/server. Engine storage, indexes, query planning, native transactions, locking and native authorization belong to the selected DBMS. SmartSQL owns guided workflows, supported-operation selection, application validation, provider-specific command generation, conflict/result presentation, import/export and recovery orchestration. Application permission checks complement, never replace, DBMS authorization.

Current native-table flow is `button/dialog -> validated operation request -> provider adapter -> driver -> existing DBMS -> result/status`. Desktop currently orchestrates operations through Application contracts; this diagram does not imply that every handler already passes through a separate Application service. PostgreSQL uses Npgsql; local SQLite uses Microsoft.Data.Sqlite with the packaged SQLite engine. SQL generation and native connections remain in Infrastructure.

- Local SQLite needs a writable project path; no separate DBMS server, database GUI, cloud account, AI model or network connection is needed for ordinary local work.
- PostgreSQL needs a running local/remote server, endpoint/network access and the appropriate database role/TLS settings. The client PC needs no local PostgreSQL server when it connects to a remote server; another GUI is optional. No SmartSQL backend proxies these current direct connections.
- Import/export uses format readers/writers plus the relevant data provider; appearance/tab navigation runs inside the desktop UI. Not every button sends SQL or invokes AI.
- Future cloud delivery uses official service APIs to transfer verified backup artifacts. It is separate from committing edits to a working database. Future AI proposes structured actions through Action/Permission rather than replacing the DBMS.

Existing adapter and safety code integrates established engines; it is not a new transaction/storage implementation. A SmartSQL server or additional technology needs a concrete separately justified task. See D052 and the user-path/dependency sections of [WORK_PLAN](WORK_PLAN.md).

## Layered Architecture

Compile-time project references point toward the independent core:

```text
Application -> Domain
Infrastructure -> Application / Domain
Desktop -> Application / Domain
Desktop composition root -> Infrastructure
```

Domain/Application do not reference Infrastructure or Desktop. Desktop's existing composition root selects concrete providers; data workflows use the core contracts. Runtime calls from UI to a provider do not reverse these project dependencies.

## Projects

### SmartSQL.Domain

The most independent layer.

Current and future responsibilities:

- universal data model;
- entity and field definitions;
- data types;
- relationships;
- constraints;
- domain abstractions.

Must not depend on SQLite, Dapper, UI frameworks, concrete DBMS providers, or AI APIs.

### SmartSQL.Application

Application use cases and orchestration.

Current and future responsibilities:

- commands and queries;
- data operations;
- validation;
- permissions;
- action planning;
- import/export orchestration;
- application services.

May depend on `SmartSQL.Domain`.

Must not directly depend on SQLite, PostgreSQL, MySQL, SQL Server, Dapper repositories, or UI frameworks.

### SmartSQL.Infrastructure

Concrete technical implementations and adapters.

Current and future responsibilities:

- SQLite provider (first slice implemented);
- PostgreSQL provider;
- MySQL/MariaDB provider;
- SQL Server provider;
- Dapper integration;
- file import/export;
- backup storage;
- other adapters.

May depend on Domain and Application.

### SmartSQL.Desktop

Desktop presentation entry point.

The desktop application uses Avalonia 12 on .NET 10. It must remain cross-platform across Windows, Linux, and macOS and may depend on Domain and Application, but not the reverse.

## Provider Principle

The application does not depend directly on SQLite. SQLite is the default local embedded provider, but remains an infrastructure detail.

The following describes runtime orchestration, not compile-time project dependencies:

```text
UI
↓
Application
↓
Universal Data Model
↓
Database Provider API
↓
SQLite / PostgreSQL / MySQL / MariaDB / SQL Server / ...
```

## Future AI Pipeline

The planned AI pipeline is:

```text
Natural language
↓
Intent
↓
Action Plan
↓
Validation
↓
Permissions
↓
Execution
↓
Audit
```

The AI assistant must not directly execute arbitrary SQL. By default, mutating operations proposed by AI require user confirmation before execution.

## Future Safety Concepts

The architecture should allow later implementation of:

- additional transaction workflows;
- action logs;
- undo where possible;
- automatic backups;
- manual backups;
- restore;
- migration compatibility checks;
- data portability diagnostics.

Manual/automatic local safety backups, validated restore, import/schema transactions, one-step cell undo, and format diagnostics are implemented. General action audit, broader undo, and cross-provider migration diagnostics remain planned.

## Native Database Editing

`IDatabaseBrowser` describes ordinary existing database objects and native display values independently of managed `IDataWorkspace` projects. `IDatabaseEditor` adds an optional explicit editing capability for one cell. Provider-confirmed column rules and lossless input validation live in Application; native SQL, permissions, keys, transient row handles, concurrency checks and transactions belong to Infrastructure.

The first PostgreSQL editor defaults to read-only and enables writes only for the current opted-in connection. It supports selected built-in scalar fields on ordinary tables with reliable primary keys, protects keys/generated/unique fields and reports unsupported table features. It rechecks schema and row snapshots before writing, rejects precision loss, and never automatically repeats a conflicting or unconfirmed commit. No native SQL or unrestricted AI execution is introduced into Domain/Application.
