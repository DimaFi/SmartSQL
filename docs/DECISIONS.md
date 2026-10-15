# Architecture Decision Log

This file records significant architectural decisions. New decisions should be added without silently rewriting accepted decisions.

## D001 - C# / .NET 10 LTS

The project uses C# and targets .NET 10 LTS.

Status: Accepted

## D002 - SQLite as Default Local DBMS

SQLite is planned as the default embedded local DBMS.

Status: Accepted

## D003 - SQLite Through Provider

SQLite is not part of the Domain architecture and must be connected through a provider.

Status: Accepted

## D004 - Dapper as Preliminary SQL Access Choice

Dapper is the preliminary choice for SQL access in future infrastructure providers.

Status: Accepted

## D005 - Local-First Design

The system is designed as local-first.

Status: Accepted

## D006 - Data Portability

Data portability is a key requirement.

Status: Accepted

## D007 - No Direct Arbitrary SQL Execution by AI

AI must not directly execute arbitrary SQL.

Status: Accepted

## D008 - User Confirmation for AI Mutations

By default, mutating AI actions require user confirmation.

Status: Accepted

## D009 - Teaching Practice as Validation Scenario

The main diploma validation domain is organizing university students' teaching practice, but the architecture remains universal.

Status: Accepted

## D010 - Product Name Is SmartSQL

The product, project files, and code namespaces use the SmartSQL name.

Status: Accepted

## D011 - Avalonia 12 as Cross-Platform Desktop UI

The desktop client uses Avalonia 12 on .NET 10 so one UI codebase can target Windows, Linux, and macOS. Only the MIT-licensed Avalonia framework is required; paid tooling and controls are optional and are not part of the baseline architecture.

Status: Accepted

## D012 - SQLite Physical Tables for User Data

Each user table has its own physical SQLite table and stable generated identifiers for fields. User-provided labels are stored as parameterized metadata, not interpolated into SQL. The first slice supports text fields only; field types and schema changes require separate design.

Status: Accepted

## D013 - Safe Local Database Evolution

SQLite `user_version` tracks the SmartSQL schema. An existing pre-versioned SmartSQL database is backed up using SQLite's online backup API before migration. Newer schema versions and unrelated SQLite files are rejected rather than modified.

Status: Accepted

## D014 - Paged Desktop Editing

The first editable desktop table uses the MIT-licensed Avalonia DataGrid and loads at most 100 records per page. User-facing operations go through the provider-independent workspace contract. An edit is persisted when a cell commit updates its bound value; one-step cell undo is available.

Status: Accepted

## D015 - Transactional CSV Exchange

The first import format is UTF-8 CSV with comma or semicolon delimiters and exact field-name matching. The entire file is parsed and validated before a single SQLite transaction inserts its rows. The Excel-oriented export uses a leading apostrophe on formula-like cell values to reduce spreadsheet formula-injection risk; this safety transform is deliberately documented as non-round-tripping.

Status: Accepted

## D016 - Typed Fields Without Rewriting Existing Data Tables

The first typed fields are Text, Number, Date, and Boolean. Stable type metadata is added in schema version 2, with older fields defaulting to Text after an automatic backup. Values are validated and canonicalized at the provider boundary while the physical SQLite columns remain TEXT. This preserves existing records and supports adding nullable fields without a table rewrite. Numeric sorting uses a decimal-aware collation rather than lexical order.

Status: Accepted

## D017 - Structured Field Filters

The application contract represents filters as field ID, operator, and value rather than SQL text. The SQLite provider validates field IDs and allowed operations, binds values as parameters, and combines filters with AND and global search. The desktop offers a visual multi-condition builder; no arbitrary SQL is accepted from users or future AI features.

Status: Accepted

## D018 - Explicit CSV Import Confirmation

CSV is parsed, mapped by exact header names, and type-validated before a read-only preview is shown. Import writes only after user confirmation, in a transaction. The preview object checks that the table schema has not changed and prevents accidental repeated commit.

Status: Accepted

## D019 - Explicit CSV Column Mapping

CSV parsing and column mapping are separate operations. Automatic mapping uses case-insensitive exact names, while the user may map columns manually, change the detected comma/semicolon delimiter, or explicitly skip source columns. A target field may be used only once. Skipped source data and target fields that will remain empty are displayed and require confirmation before validation and preview. Import size is bounded to 100,000 rows and 50 MB for the current in-memory workflow.

Status: Accepted

## D020 - Non-Destructive Schema Renaming

Table and field names are editable metadata associated with stable generated IDs. Renaming is performed transactionally and does not rename or rebuild physical SQLite tables and columns, so values, types, filters, and future relationships can keep referring to stable identities. Field removal and type conversion are deliberately separate because they may lose data.

Status: Accepted

## D021 - Saved Filters Use Stable Field IDs

Named filter presets are stored per table in schema version 3. Each condition references a stable field ID and structured operator rather than a field label or SQL fragment, so presets survive renaming and remain provider-independent. Saving with the same case-insensitive name replaces the existing preset. Deleting a preset does not change records or the currently active conditions.

Status: Accepted

## D022 - Destructive Schema Changes Require Safety Backup and Exact Confirmation

Table and field deletion expose a structured impact summary before execution. The desktop requires the exact target name, and the provider creates a uniquely named SQLite backup before starting the deletion transaction. Removing a field deletes entire saved-filter presets that reference it rather than silently weakening their conditions. The final field cannot be removed separately because zero-field tables are outside the current data model.

Status: Accepted

## D023 - Restore Validates a Temporary Copy Before Replacement

Restore never opens the selected source for mutation. SQLite copies it to a non-pooled temporary database, validates its SmartSQL metadata, and migrates the temporary copy when necessary. Only then is the current database backed up and replaced through SQLite's backup API. The UI requires the exact current filename. Invalid, unrelated, or newer incompatible sources are rejected before current data changes.

Status: Accepted

## D024 - Separate Editable Demonstration Database

The teaching-practice scenario is seeded only by the Desktop project into a separate local SQLite file. The generic Domain and Application contracts retain no student-specific entities. Opening the demo never replaces the default user database, and reopening it preserves user edits.

Status: Accepted

## D025 - Restricted Natural-Language Interaction Prototype

Before introducing a local language model, the Desktop exposes a small deterministic Russian query grammar. The parser resolves current table field labels to stable field IDs and returns structured read-only filter conditions. Unsupported language is rejected with feedback. No user text or future model output is executed as SQL; a later assistant must use the same validated action and permission boundary.

Status: Accepted

## D026 - Atomic CSV-to-New-Table Workflow

Importing a CSV into a new table is separate from mapping CSV columns into an existing table. The user reviews names, types, delimiter, and sample values before committing. Application validates every value first; the provider creates table metadata, physical table, and records in one transaction, so a failed import leaves no partial table. This provider-independent operation is useful beyond CSV and retains the existing CSV size and row limits.

Status: Accepted

## D027 - Typed, Strict Single-Table JSON Exchange

`SmartSQL.Table.v1` exchanges field names, types, and ordered record values independently of the DBMS. Import maps names case-insensitively into an existing table with the same complete set of field types. All rows are validated before preview; commit rechecks the schema and appends transactionally. Decimal values must be represented exactly, and unsupported properties, types, or values are rejected rather than silently discarded. Whole-database metadata is outside this format and documented explicitly.

Status: Accepted

## D028 - Publish Exports Only After Successful Writing

The desktop writes CSV and JSON exports to a unique temporary file beside the destination. Infrastructure flushes and closes the file, then moves it into place without overwriting an existing destination. Failures remove the temporary file. Transfer work runs in the background with workspace controls temporarily disabled so the target cannot change mid-operation.

Status: Accepted

## D029 - Workspace-Local Data and Development Artifacts

For this development workspace, application data lives under `D:\Code\SmartSQL\data\local`, and caches, temporary files, test data/results and migration manifests live under ignored `.workspace` directories on D:. Process-scoped helper scripts initialize .NET, NuGet, app and test paths without changing global user settings. Desktop direct launches discover the checkout or use a portable directory beside the executable; explicit invalid paths fail rather than silently reverting to AppData. Migration is copy-first with hashes and read-only database integrity validation; C: originals are retained as recovery copies. The isolated assistant experiment is maintained separately at `D:\Code\SmartSQL-model`.

Status: Accepted

## D030 - Bounded Data-Only XLSX Adapter

Infrastructure reads/writes the ordinary ZIP/XML spreadsheet format without a DBMS dependency or new package. Worksheet values pass through existing provider-independent mapping, validation, preview and transactional commit. Formulas, macros, external references, merged cells, ambiguous date/time formats and unsupported cell capabilities are rejected. Detected losses of workbook presentation/metadata are disclosed before import. Decimal values beyond Excel's 15 significant digits are exported as exact text and counted for disclosure. Sanitized/truncated worksheet names are reported. Export uses the atomic file publication boundary. XLSX exchanges one table's values, not a whole project, relationships or saved filters.

Status: Accepted

## D031 - Validated Clipboard Batch Append

Tab-separated spreadsheet clipboard input supports quoted tabs/newlines and optional headers. Users review mapping, explicitly acknowledge skipped columns/empty target fields, and confirm a preview. All values are validated before a single atomic `AddRecords`; the preview snapshots schema and prevents repeated/concurrent commits. Text that the current model would silently turn into null is rejected explicitly. Limits bound rows, columns, cells and characters. The first workflow appends records rather than overwriting the selected grid region.

Status: Accepted

## D032 - Universal Database Client Product Direction

Multiple DBMS support is a central product goal. SmartSQL will provide a common desktop connection/table/data workflow in the DBeaver direction while preserving safe operations and accessible UI. A provider that only stores SmartSQL-managed projects is insufficient to deliver ordinary existing-database exploration. The first PostgreSQL milestone therefore validates connection, catalog/type/key inspection and paged read-only browsing before editing external tables. Native limitations must be reported rather than hidden or forced into a lowest-common-denominator model. Implementation contracts will be added against that concrete milestone; the core remains independent of DBMS/SQL/UI.

Status: Accepted

## D033 - Native Database Browsing Is Separate From Managed Workspaces

`IDatabaseBrowser` describes native schema/table references, native column types/nullability/primary-key order, display-only rows and bounded async pages. It does not invent managed record GUIDs or convert external types to the four local editor types. `IDataWorkspace` remains the editable SmartSQL-managed project contract. The first native provider uses Npgsql in Infrastructure to inspect ordinary existing PostgreSQL databases. SQL, transactions and credentials remain outside Domain/Application; the Desktop composition root supplies a provider factory to the browser window.

This slice uses the driver's direct ADO.NET API, following the current SQLite provider style. Dapper remains a preliminary option in D004; no ORM/query package is required for this concrete catalog/read-only workflow.

Status: Accepted

## D034 - Nonsecret Connection Profiles and Read-Only PostgreSQL Sessions

Local profiles contain only structured connection parameters and TLS policy, with strict bounded JSON validation and atomic publication. Passwords are supplied for the current session through a driver callback, never saved in profiles or rendered by request formatting. Driver errors are mapped to safe messages without credential-bearing inner exceptions. PostgreSQL uses startup/session read-only settings and repeatable-read read-only transactions per operation, timeouts and cancellation. Paging uses catalog-confirmed quoted identifiers, parameterized offset/limit and a sentinel row; each page is a separate snapshot, with explicit identity/paging limitations. Native values stream as PostgreSQL text with exact numeric output and explicit refusal beyond cell/page limits. External editing and whole-database exchange are separate later work.

Status: Accepted

## D035 - Shared Desktop Presentation and Two Levels of Detail

Desktop uses a small shared set of semantic colours, control styles and Russian string resources. Layout and presentation state stay in Desktop, separate from data operations and providers. The default simple workspace exposes table editing, search/filter/sort and exchange; the expanded mode adds saved filters, a structure inspector and the existing deterministic query prototype. Switching modes retains the selected table and active conditions. Command panels have bounded scrolling to preserve space for data. New controls join existing command groups and reuse styles; unsupported features are not represented by placeholder buttons. English will be introduced through complete string resources rather than mixed language labels; legacy dialog/status strings still require extraction when that work begins.

Status: Accepted

## D036 - Literal Native Text Search and Explicit Text Sorting

`DatabaseBrowseQuery` adds a bounded literal search and optional catalog-confirmed sort column without exposing SQL in Application. PostgreSQL applies the parameterized search across native text representations before paging. Case conversion follows the server locale; C locale can retain case for non-Latin letters. Selected-column sorting uses text with C collation, NULL first, and existing key ordering to resolve ties. The UI names this text ordering and reports its difference from numeric/date ordering. Native typed sorting and typed external filters remain separate work; existing default primary-key paging is preserved.

Status: Accepted

## D037 - Explicit, Keyed Single-Cell Editing of Native Tables

`IDatabaseEditor` is an optional extension of native browsing. Application describes provider-confirmed editable columns and validates lossless input; SQL, permissions, identity snapshots and native transactions remain in Infrastructure. PostgreSQL connections default to read-only. Editing must be enabled explicitly for the current connection and is never persisted in a profile.

The first editing slice changes one supported cell in an ordinary table with a confirmed primary key. Keys, unique-index fields, generated/identity columns and unsupported native types remain read-only. Tables with inheritance, partitioning, RLS, user triggers, UPDATE rules or unsafe CHECK/type-output functions remain browse-only. The provider checks table privileges and reports restrictions rather than inventing identities or silently losing capabilities.

Rows receive random, provider-private handles for the current page, expiring after 15 minutes. Handles are excluded from JSON; original native values, row version and schema identity remain in a bounded in-memory snapshot. A write locks the table against concurrent structural changes, rechecks metadata and permissions, locks the row by its complete key, and compares its version and original values before a parameterized update. A changed/deleted row is a conflict, never an automatic overwrite or retry.

NULL, empty strings and whitespace remain distinct. Native numeric assignment is compared with the submitted number without converting through .NET decimal; rounding, overflow or truncation cause rollback. Cancellation before commit rolls back. Once commit has started, an unconfirmed result is reported as unknown and must be inspected before another attempt. External inserts/deletes, general undo and backup/export of a remote database remain separate work.

Status: Accepted

## D038 - First-Run Mode Choice and a Shared Tabbed Workspace

The user prioritizes comfortable PostgreSQL/SQLite workflows, values both approachable and advanced modes, and requests a first-run choice with a permanently available switching control. The selected mode will be remembered; switching preserves active connection/object/tab state and does not enable writes.

The target presentation uses one main window with a connection/object tree, table/SQL tabs, an optional inspector and active-context status. Tabs retain separate query/page/loading state and explicit connection identities; session and cancellation ownership must be defined before replacing separate browser windows. This follows the connection tree documented by [DBeaver](https://dbeaver.com/docs/dbeaver/Database-Navigator/) and the object-tree/tabbed layout in [pgAdmin](https://www.pgadmin.org/docs/pgadmin4/latest/user_interface.html). Managed/native data boundaries remain; no plugin framework is introduced.

Russian workflows are completed first, then translated through full resources. T07/T08 now implement persistence, onboarding and unified table tabs; manual UI acceptance remains open. Priorities and acceptance criteria are in [WORK_PLAN.md](WORK_PLAN.md).

Status: Accepted; first implementation verified by build and automated tests, UI acceptance pending.

## D039 - Native Single-Row Creation Has Independent Capabilities

`IDatabaseRowCreator` is separate from cell updating. Its explicit session opt-in, SELECT/INSERT rights, protective-lock rights, defaults, identity/generated rules and supported inputs are confirmed by the provider. A bounded, expiring private schema confirmation is rechecked before a parameterized one-row insertion; a successful or unconfirmed commit consumes it. Server default/omission, explicit NULL and exact values are separate input states. Supported keys and unique fields may be supplied on insertion even when they are protected from updates.

Unsafe defaults can be bypassed with supported explicit input. Owned identity/serial sequences require confirmed rights and disclose nontransactional gaps after error/cancellation. Returned native values are checked for assignment precision/truncation before commit. Unknown commits are never repeated automatically. Bulk insertion and external deletion remain separate work.

Status: Accepted

## D040 - Native Writes Lock Against Concurrent Index DDL

PostgreSQL cell updates and single-row inserts acquire `SHARE ROW EXCLUSIVE` before checking metadata and writing. The earlier `ROW EXCLUSIVE` lock permits concurrent index creation and cannot protect the safety check against a newly introduced expression index. The stronger guard prevents that race and briefly serializes writers while normal readers can continue. The transaction retains bounded statement/lock waits and cancellation.

The lock requires MAINTAIN (PostgreSQL 17+) or UPDATE/DELETE/TRUNCATE; INSERT alone is insufficient. Creation capabilities report this restriction explicitly, without granting permissions or requiring UPDATE specifically. An actual integration test observes the granted protective lock, blocked concurrent index creation, cancellation and rollback. Lock conflicts and privileges follow [PostgreSQL locking](https://www.postgresql.org/docs/17/explicit-locking.html) and [LOCK permissions](https://www.postgresql.org/docs/17/sql-lock.html).

Status: Accepted

## D041 - Ordinary SQLite Browsing Does Not Create Managed Metadata

`NativeSqliteDatabaseBrowser` implements the existing read contract for ordinary files independently of `SqliteWorkspace`. It opens only existing files for reading, uses query-only/trusted-schema restrictions, and adds no SmartSQL metadata or migrations. Storage-class-aware values preserve integer precision, round-trip REAL display, NULL/empty/whitespace and BLOB bytes; text decoding checks the source UTF-8/UTF-16 encoding and fails explicitly on invalid data. Unsupported virtual objects, width/value/page limits and unstable identities are reported.

Live WAL remains visible; immutable reads that could ignore it are not used. SQLite may use/create source-side WAL/SHM auxiliary files, a disclosed engine limitation. No checkpoint is requested. Real tests check main/WAL hashes while a writer remains open. This slice is browse-only and does not make native files editable or backed up automatically.

Status: Accepted

## D042 - Single-Row Deletion Uses an Independent One-Time Confirmation

`IDatabaseRowDeleter` separates deletion from cell updating and row creation. The provider requires explicit session opt-in, SELECT/DELETE rights, a safe ordinary table and a reliable complete primary key. A separate random `DeleteHandle` identifies a bounded private copy of the original native values and `xmin`; it is excluded from serialized/displayed rows and expires with the current page after 15 minutes. After admission each handle is consumed once, including rollback, conflict or cancellation.

The PostgreSQL transaction acquires the existing `SHARE ROW EXCLUSIVE` metadata guard before rechecking structure, permissions and dependencies. It reads original values without `SELECT FOR UPDATE`, which would unnecessarily require UPDATE privileges; the table guard excludes concurrent target writers. `DELETE FROM ONLY ... RETURNING` then verifies exactly one row, its original values and version again before commit. Cancellation before commit rolls back. An unconfirmed commit clears handles and reports an unknown outcome without retrying the deletion.

This first slice rejects every incoming foreign key, including self references and RESTRICT/NO ACTION, as well as cascades/SET NULL/SET DEFAULT. Proving safe RI queries would require guarded child metadata and additional rights; that work stays in T06/T12 rather than introducing a dependency graph now. Outgoing foreign keys do not themselves prevent deleting a child row. User triggers, DELETE rules, RLS, inheritance, partitions and unsafe expressions/type output also prevent deletion. The UI confirms the database, schema, table, ordered key and row preview; successful deletion and subsequent refresh failures are reported separately.

Status: Accepted

## D043 - The Model Workspace Owns All of Its Project Files and Caches

The user assigns future model development to `DimaFi/SmartSQL-ML` and requires every model-specific source, script, document, profile, dataset, weight, cache, temporary file and test/benchmark result to remain inside `D:\Code\SmartSQL-model`. The existing library/CLI/tests/cases names are retained. Model-local scripts and a relative NuGet package folder replace dependence on the desktop repository's configuration/cache; existing required packages were copied without removing the shared desktop cache.

The desktop `-Model` compatibility switch delegates to the model initializer. The model can build/test independently of the desktop checkout. Environment changes affect only the calling process and child tools; installed tools and global settings remain unchanged. Stub tests do not imply a real model, training or Desktop integration. Future integration still requires stable data workflows and an accepted Action/Permission boundary. GitHub publication is currently deferred by the user.

Status: Accepted

## D044 - One Workspace Window with Independent Table Sessions

`WorkspaceWindow` owns the connection/schema/object tree, table tabs, active-context status and presentation preference. The managed SQLite editor is embedded as a control; native browsing uses fixed-table controls. The old standalone browser window is removed. A connection entry has a fresh session identity even when profile names/endpoints coincide. Reopening the same object in that entry selects its existing tab; different entries never share tab state.

Each native table tab acquires and owns a separate provider-browser instance. This keeps query/page/selection state, private edit/delete handles, insertion confirmations and cancellation independent. Catalog refresh uses a temporary browser and cannot invalidate table-tab handles. Disconnect stops new acquisitions, clears credential-capturing factories, cancels and drains catalog/open work, and awaits all associated tab closures. Closing a tab also cancels and drains its operation before disposing the provider; overlapping close/disconnect/exit requests await the same close task. Local import/export prevents closing its active tab until completion.

Simple/Advanced are presentation preferences only. Switching updates existing controls without reloading data or changing provider capabilities/session write opt-in. First-run selection can be deferred. Only the chosen mode is stored in bounded, versioned JSON under the workspace data directory, with atomic replacement and explicit errors preserving damaged/unknown settings. Passwords, live sessions and open tabs are not persisted. SQL tabs remain T11; this slice introduces no model integration or provider changes.

Status: Implemented; Debug build and 404 automated tests (including 41 real PostgreSQL checks) pass. Basic screen checks now pass; remaining UI acceptance is recorded in WORKSPACE_UI_ACCEPTANCE.md. Stateful managed labels use code-owned values to survive first visual attachment; workspace shortcuts use tunneling input so focused child controls cannot swallow them.

## D045 - Typed Native Predicates Are Provider Capabilities

Native queries carry up to 20 immutable-snapshotted AND conditions with literal input and explicit NULL operators. Domain/Application contains no SQL. Providers confirm available operators and native sorting from fresh metadata and recheck before execution, applying search and predicates before pagination. Unsupported operations fail explicitly. NULL, empty text and whitespace remain distinct; values are parameters and names come from quoted metadata.

PostgreSQL admits exact pg_catalog builtin identities for text/varchar, integers, numeric, date and Boolean. Operators/casts/functions are qualified and relation metadata is guarded by ACCESS SHARE in a read-only transaction. Numeric input stays a decimal string rather than passing through .NET decimal. Domains and unsupported types permit NULL tests only and do not offer selected sorting.

SQLite's declarations are not storage guarantees. Every affected non-null column value is checked in the same read snapshot, including rows outside the result/page. Integer input is Int64; numeric input must round-trip without changing its decimal spelling's mathematical value, DATE must be real canonical TEXT and Boolean INTEGER 0/1. Unknown declarations permit NULL tests; their native storage ordering remains available with a diagnostic. No casts silently repair or discard incompatible data. Sources remain read-only and are not migrated.

Status: Implemented; Debug and 461 tests, including 45 real PostgreSQL checks, pass. Manual UI acceptance is tracked separately.

## D046 - Versioned Local Desktop Publications and Shared Branding

The user requests a runnable application alongside development and a gradually formed installer. Windows x64 publication uses a self-contained directory and ZIP under the sibling `D:\Code\SmartSQL-builds`, with explicit version/build identifiers, source commit/dirty state and payload hashes. Existing publications are never overwritten or deleted. SDK caches, build temporaries and runtime test data remain on D:. Online restore is an explicit command-local option; installed tools and global settings are unchanged. Framework-dependent output requires explicit selection and is labelled accordingly.

The portable app can launch directly outside the source tree or through an isolated-data launcher. Databases, profiles, passwords and caches are never included in the payload. The initial Inno Setup recipe is per-user, keeps a stable AppId, uses D: and does not remove user-created data during upgrade/uninstall. Compiler installation, installer acceptance, signing and automatic updates remain separate work; the absence of ISCC does not imply a working installer.

The supplied green S emblem is redrawn as two crisp vector ribbons. A shared multiresolution ICO is embedded in the executable and applied to all windows/dialogs; small frames use a flat fill and light backing for contrast. The shell header uses the same vector geometry. PNG/ICO export is reproducible from the SVG source, without new runtime dependencies.

Status: Implemented and self-contained EXE/ZIP launch verified on Windows; icon visible in dialogs and taskbar. Installer compilation/installation/update/uninstallation and cross-platform delivery remain unverified.

## D047 - Explicit SQLite Cell Updates Require a Prewrite Backup

T10 extends D041 with a separate explicitly enabled session implementing IDatabaseEditor. OpenAsync remains read-only; OpenEditableAsync receives an absolute D: backup directory. Presentation mode and saved profiles do not grant write access. The first slice changes one supported cell, with no INSERT/DELETE, migrations or general undo.

Only ordinary main tables with a complete non-null INTEGER/TEXT primary key qualify. Keys, unique fields and unsupported target types remain protected. Views, virtual/shadow tables, triggers, foreign keys in either direction, generated fields, CHECK and unsafe index/collation expressions are rejected conservatively. Values preserve NULL/empty/whitespace and native storage/precision; unsupported SQLite affinity conversions fail explicitly.

The current page grants random, bounded, provider-private single-use handles expiring after 15 minutes. A write takes BEGIN IMMEDIATE on an existing file, confirms metadata and the full raw original row by its complete key, and consumes the submitted handle. Storage classes, integer values, REAL bits and original TEXT/BLOB bytes are compared. SQLite has no PostgreSQL-like row version: changes which return to exactly the same raw values are not distinguishable; this limitation is disclosed.

While holding the writer lock, a separate read-only connection creates an online backup of the prewrite state, including live WAL. The backup is checked and atomically published under a unique name on D: before UPDATE OR ABORT. Copy failure prevents writing; failed updates retain any completed protective copy. The result must contain exactly one row and preserve every other raw value; loss, rounding or constraint errors roll back. Cancellation before commit rolls back. Commit uncertainty invalidates handles, reports an unknown result and never triggers an automatic retry.

Status: Implemented; Debug 0 warnings and 519 tests (including 45 real PostgreSQL facts) pass without skips. UI partially accepted: explicit opt-in, exactly one changed cell, verified prewrite copy and CHECK-table refusal. Full UI acceptance remains open. Existing core dependency direction is unchanged.

## D048 - Manual SQL Uses Independent Provider-Enforced Read-Only Sessions

User-authored SQL is an explicit advanced-user desktop workflow. Application carries bounded SQL text and provider-neutral result/cancellation contracts, without parsing or generating SQL. Infrastructure owns native parsing, connection settings and permission enforcement. Query results have ordinals, native type information and exact display values, never inferred row identities or editing capabilities. SQL sessions do not invalidate table operation handles and never inherit an editing connection's permission.

PostgreSQL uses one zero-parameter NpgsqlBatch command/native Parse, a non-executing schema description, read-only transaction and cursor/FETCH with exact server text. SQLite uses an independent ReadOnly connection, defensive/query-only configuration, native whole-tail single-statement parsing, NO_VTAB and a conservative authorizer against snapshotted ordinary main objects and vetted builtins. Schema-less columnless SQLite READ is admitted only for an exact confirmed main object name; unknown CTE names remain unsupported. UTF-16 SQLite is explicitly unsupported in this initial SQL executor without changing existing table browsing/editing.

Both implementations serialize admitted work, use bounded results/time, cancel running and queued work, drain before disposal and never commit user SQL. Read-only is not an OS/function-side-effect sandbox. Local bounded successful-query history and explicit UTF-8 SQL files support the current desktop task; they do not persist passwords or query results and are not committed. The initial editor uses the existing TextBox dependency; syntax highlighting remains an explicit next T11 slice, not a claimed capability. See READ_ONLY_SQL.md and the open UI acceptance checklist.
