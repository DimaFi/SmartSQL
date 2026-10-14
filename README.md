# SmartSQL

Universal intelligent structured data management system with an adaptive natural-language interface.

## Status

Working Avalonia workspace with one connection tree and tabbed managed SQLite, ordinary SQLite and PostgreSQL tables. PostgreSQL supports keyed updates and confirmed single-row insertion/deletion. T07/T08 shell and mode code passes automated verification; visual acceptance remains open in [workspace UI acceptance](docs/WORKSPACE_UI_ACCEPTANCE.md). Priorities and criteria: [development plan](docs/WORK_PLAN.md). Implemented capabilities and limitations: [matrix](docs/CAPABILITY_MATRIX.md). The existing GitHub [roadmap](https://github.com/DimaFi/SmartSQL/issues/1) and [25 tasks](docs/GITHUB_TASKS.md) retain their IDs; current publication state must be checked as described in [HANDOFF](docs/HANDOFF.md).

To see a populated application immediately, launch it and click **Открыть пример**. SmartSQL opens a separate editable database, `pedagogical-practice-demo.db`, with sample students, practice placements, organizations, and saved filters. Your default `smartsql.db` is not replaced. The demo button reopens the same file, so edits made there are preserved. A screenshot of the working query is in [docs/smartsql-demo-query.jpg](docs/smartsql-demo-query.jpg).

The **Запрос словами** panel in expanded mode accepts a deliberately limited Russian grammar, for example `Покажи записи, где Документы = Нет и Курс = 3`. It translates recognized conditions into the same structured filters as the visual UI; it does not generate or execute arbitrary SQL. This is a deterministic interaction prototype, **not a language model**. Unsupported requests produce an explanation instead of a guessed operation.

The application creates a local SQLite database on first launch. Users can also create, open, back up, and restore database files; create, rename, and safely delete tables with typed fields; add, rename, or safely delete fields; add/edit/delete records; search, sort, combine and save reusable field filters; and browse records in pages of 100. The last local cell edit can be undone. CSV files can be mapped, previewed, and imported into an existing table or exported for Excel. Typed JSON table exchange is also available. XLSX table exchange and bulk paste from Excel are available. PostgreSQL browsing with literal search, text sorting, full cell inspection and explicitly enabled keyed updates is available. Ordinary SQLite read-only browsing and explicit PostgreSQL single-row insertion are now available. Confirmed single-row PostgreSQL deletion is available when no incoming foreign key exists; deletion under incoming foreign keys, external exchange, relations, field type conversion, MySQL/MariaDB/SQL Server, integrated AI and server mode remain planned.

Database actions are grouped under **База**, and selected-table actions under **Импорт**, **Экспорт**, and **Структура**. The natural-language prototype expands on demand. File reading and writing run in the background while the workspace controls are temporarily unavailable, preventing table switches during an operation.

Saved filters belong to one table and retain stable field identifiers when fields are renamed. Saving under an existing name replaces that filter's conditions. Deleting a saved filter removes only the reusable preset; it does not modify table records or the conditions currently displayed.

Deleting a table or field first displays the number of affected records and saved filters, then requires the exact target name. SmartSQL creates a separate SQLite backup beside the active database before the transaction starts. Deleting a field also removes complete saved-filter presets that depend on it, rather than silently changing their meaning. The final field of a table cannot be removed separately; delete the table instead.

Restore validates the selected SmartSQL database in a temporary, non-pooled copy and migrates that copy if needed. The selected source file is never modified. The current database is backed up automatically before SQLite replaces its contents, and the UI requires the exact current filename as confirmation. Unrelated or newer incompatible SQLite files are rejected before the current database changes.

Field types in this first version are Text, Number, Date, and Yes/No. Numbers accept a comma or period as the decimal separator. Dates accept `DD.MM.YYYY` and `YYYY-MM-DD`. Yes/No accepts `Да`/`Нет`, `true`/`false`, or `1`/`0`. Empty cells are allowed. Existing fields from older databases migrate as Text; a backup is made before migration. Changing or removing a field's type is not yet supported.

CSV import accepts UTF-8 files with comma or semicolon separators. The delimiter is detected automatically and can be corrected manually. Each source column can be mapped to any compatible SmartSQL field or explicitly skipped; one target field cannot receive multiple source columns. Omissions require explicit confirmation. The preview shows a row count, mapping, and the first five rows; no data is written until confirmation. Type validation runs before the preview, and the import is all-or-nothing with limits of 100,000 records and 50 MB per file. CSV export uses UTF-8 with BOM and semicolons. For spreadsheet safety, values that could be interpreted as formulas receive a leading apostrophe, so Excel-oriented exports are not byte-for-byte round trips for those cells. Export does not overwrite an existing file.

**Таблица из CSV** creates a new table directly from a UTF-8 CSV file. The dialog lets you edit every field name and choose Text, Number, Date, or Yes/No before import. It shows a sample value for each column and validates all rows before creating anything. Table metadata and records are then written in one transaction; an error rolls the entire new table back. Existing-table CSV import remains a separate action.

JSON exchange uses the portable `SmartSQL.Table.v1` format described in [docs/JSON_TABLE_FORMAT.md](docs/JSON_TABLE_FORMAT.md). It preserves field names and types, nulls, text, exact decimal numbers, ISO dates, and Boolean values. Import appends to an existing table whose field names and types match, with a preview of the first five rows and all-or-nothing confirmation. Unknown capabilities and properties are rejected rather than ignored. It does not transfer saved filters, relationships, record identifiers, or an entire database.

CSV, JSON, and XLSX exports include the **entire selected table**, regardless of the current page, search, or filters. They write to a temporary file beside the destination and publish the destination only after successful completion. Existing files are never overwritten, and a failed export removes its own temporary file.

The Russian desktop uses one `WorkspaceWindow`: a shared connection tree on the left, independent table tabs in the center and a global Simple/Advanced switch. The first-run choice is stored atomically in local JSON. Switching mode keeps open tab state and never grants write access. Managed SQLite uses its existing editor inside the workspace; each native table tab owns a separate provider session, so equal table names from different connections do not collide and closing a loading tab cancels its work. Tabs and credentials are not persisted, and SQL has not been added. Expanded managed mode adds saved filters and a structure inspector. See [desktop interface](docs/DESKTOP_INTERFACE.md) and the open [visual acceptance checklist](docs/WORKSPACE_UI_ACCEPTANCE.md).

## Solution Structure

**База → SQLite-файл для просмотра…** opens ordinary existing SQLite files without SmartSQL metadata or migration. The provider-neutral browser supports native values, catalog, pages, literal search and text sorting in read-only mode. See [native SQLite](docs/NATIVE_SQLITE_BROWSER.md). PostgreSQL's **+ Новая запись** uses independent creation capabilities, explicit DEFAULT/NULL/value modes, schema confirmations and protective locks.

To resume in another chat, start with [handoff](docs/HANDOFF.md), [tasks](docs/TASKS.md), the plan and Git status/history. Publication status is recorded separately from local verification.

**XLSX** exports the whole selected table and imports one selected worksheet into an existing table through mapping, compatibility warnings and preview. Text stays literal, dates/Boolean have native Excel cells, and numbers beyond Excel's 15 significant digits are stored as exact text with a visible count. Worksheet names adapted to Excel's limits are reported. Formulas, macros, merged cells, external links and unsupported values are rejected. Formatting, comments, validation and workbook structure are not table data and their detected loss is disclosed before import. The adapter is bounded to 100,000 rows and 2 million cells.

**Импорт → Вставить строки из Excel…** reads a copied rectangular tab-separated range. Review whether the first row contains headers, map columns and confirm a preview. It appends records atomically; it does not overwrite the selected grid region. Formula-like text remains literal. Local clipboard/test data is never tracked in Git.

**База → Подключения к СУБД…** manages nonsecret PostgreSQL profiles and opens a native browser. Read-only is the default; an explicit session checkbox permits supported keyed cell updates. The browser lists accessible schemas/tables/views, shows native types, nullability and composite primary keys, and reads pages of 100 records. Passwords are entered per session and never saved; errors, cancellation and large values have explicit limits. The SQLite editor remains the editable local workflow. See [PostgreSQL browser](docs/POSTGRESQL_BROWSER.md) and [work plan](docs/WORK_PLAN.md).

```text
src/
  SmartSQL.Domain/
  SmartSQL.Application/
  SmartSQL.Infrastructure/
  SmartSQL.Desktop/
tests/
  SmartSQL.Domain.Tests/
  SmartSQL.Application.Tests/
  SmartSQL.Infrastructure.Tests/
docs/
```

## Requirements

- .NET 10 SDK

## Build

```powershell
./scripts/Build.ps1
```

Run the desktop app with `./scripts/Run.ps1`. In this checkout the default data file is `D:\Code\SmartSQL\data\local\smartsql.db`; the demo and automatic backups also remain on D:. Development caches, temporary files, and test results are excluded from Git. See [workspace storage](docs/WORKSPACE_STORAGE.md) for migration and model-workspace details. Use the **База** menu to create, open, or back up other database files.

## Test

```powershell
./scripts/Test.ps1
```

The latest full verification passed a warning-free Debug build and **404 tests**: Domain 1, Application 113, Infrastructure 281 and Desktop 9. This includes all **41 real PostgreSQL facts**, with zero skipped tests. The suite covers SQLite persistence, search, safe migration, CSV/JSON/XLSX exchange, clipboard paste, decimal precision, atomic export, workspace identity/session isolation, cancellation and mode preferences. Visual acceptance is tracked separately. Add `-Offline` to the helper scripts when using the prepared local NuGet cache.
