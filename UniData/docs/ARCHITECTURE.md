# Architecture

## Current Scope

This document records the high-level architecture for Stage 0 - Project Foundation.

Concrete Universal Data Model classes, provider APIs, database implementations, import/export engines, backup engine, UI framework, and AI integrations are intentionally not designed here. They will be handled in later stages.

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

### UniData.Domain

The most independent layer.

Future responsibilities:

- universal data model;
- entity and field definitions;
- data types;
- relationships;
- constraints;
- domain abstractions.

Must not depend on SQLite, Dapper, UI frameworks, concrete DBMS providers, or AI APIs.

### UniData.Application

Application use cases and orchestration.

Future responsibilities:

- commands and queries;
- data operations;
- validation;
- permissions;
- action planning;
- import/export orchestration;
- application services.

May depend on `UniData.Domain`.

Must not directly depend on SQLite, PostgreSQL, MySQL, SQL Server, Dapper repositories, or UI frameworks.

### UniData.Infrastructure

Concrete technical implementations and adapters.

Future responsibilities:

- SQLite provider;
- PostgreSQL provider;
- MySQL/MariaDB provider;
- SQL Server provider;
- Dapper integration;
- file import/export;
- backup storage;
- other adapters.

May depend on Domain and Application.

### UniData.Desktop

Desktop presentation entry point.

The UI framework is intentionally not selected in Stage 0. The project is currently a neutral placeholder so that the solution structure can be built without committing to WPF, Avalonia, WinUI, MAUI, or another framework.

## Provider Principle

The application must not depend directly on SQLite. SQLite is planned as the default local embedded provider, but it remains an infrastructure detail.

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

- transactions;
- action logs;
- undo where possible;
- automatic backups;
- manual backups;
- restore;
- migration compatibility checks;
- data portability diagnostics.

These concepts are not implemented in Stage 0.
