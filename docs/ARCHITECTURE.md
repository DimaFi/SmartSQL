# Architecture

## Current Scope

This document records the current architecture and planned extensions.

The first universal model, SQLite provider, typed fields, desktop CRUD/filters, CSV/JSON/XLSX table exchange, clipboard bulk paste, and local backup/restore are implemented. Relationships, safe type conversion, additional DBMS providers, and integrated AI remain later stages.

The product direction includes a common database-client interface for ordinary existing databases across supported DBMSs. The current SQLite workspace owns SmartSQL metadata and generated physical tables; it must not be mistaken for an existing-database catalog browser. The next PostgreSQL slice validates connection/catalog browsing before editing external schemas. Native provider differences remain explicit through capability and compatibility reporting.

## Layered Architecture

The intended architecture is:

```text
Presentation
↓
Application
↓
Domain / Universal Data Model
↓
Provider abstractions
↓
Infrastructure providers
↓
Physical database
```

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

The future direction is:

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
