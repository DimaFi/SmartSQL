# Project Goals

## Problem

Many people need to work with structured data but do not know SQL, database commands, database administration, table design, or the differences between SQLite, PostgreSQL, MySQL, SQL Server, and other systems.

Typical users still need to create lists, maintain records, search, filter, update, import, export, and move data between systems. Existing tools often force users either into spreadsheets with weak structure or into database systems that require technical knowledge.

## Goal

UniData aims to become a universal intelligent system for managing structured data through a human-friendly interface.

The user should be able to create, import, view, search, modify, and export structured data without knowing SQL, DBMS commands, or implementation details of a concrete database engine.

## Main User Scenarios

- Create a new structured data project.
- Define or generate a data structure for a real-world task.
- Import data from common formats.
- View and search records.
- Add, edit, and delete records through safe application operations.
- Export data to common formats or another system.
- Move a project between computers.
- Use natural-language requests in the future AI layer.

## Universal Scope

The system must not be limited to one subject area. The primary diploma validation domain is structured data about children's camp participants, but the architecture must also support schools, small organizations, customers, employees, warehouses, projects, catalogs, equipment accounting, and other structured datasets.

## Local-First Approach

UniData is designed as a local-first application. A user should be able to keep data locally, work without mandatory cloud infrastructure, create backups, and move projects between machines.

Local-first does not mean SQLite-first architecture. SQLite is planned as the default embedded local provider, but the core model and application layer must stay independent from it.

## Natural-Language Interface

In future stages, UniData will include an AI assistant that understands requests such as:

```text
Find all students from group three.
Show customers who have not purchased anything in the last three months.
Add a new employee.
Create a database for a small language school.
Move these people to another group.
```

The AI must not directly execute arbitrary SQL. It should produce an action plan, validate it, check permissions, explain the proposed operation, and wait for user confirmation by default.

## Portability

Users must not be locked into UniData. The target data flow is:

```text
external system
-> import
-> UniData
-> work
-> export
-> external system
```

Import and export should be symmetrical where possible. If a target format or DBMS cannot preserve all capabilities, the system must detect this and clearly report it instead of silently losing data.

## No Vendor Lock-In

The architecture must support multiple storage providers and data formats over time. Planned targets include SQLite, PostgreSQL, MySQL, MariaDB, Microsoft SQL Server, CSV, XLSX, JSON, and later other formats or systems.

UniData must remain a tool for the user's data, not a trap around it.

## Diploma Validation Scenario

The main diploma scenario is managing structured data about children's camp participants: children, groups, attributes, search, updates, import, and export.

This scenario validates the system but must not leak into the core as a hard-coded domain model.
