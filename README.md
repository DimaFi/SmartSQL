# SmartSQL

Universal intelligent structured data management system with an adaptive natural-language interface.

## Status

Working local database editor on Avalonia and SQLite.

To see a populated application immediately, launch it and click **Открыть демо**. SmartSQL opens a separate editable database, `pedagogical-practice-demo.db`, with sample students, practice placements, organizations, and saved filters. Your default `smartsql.db` is not replaced. The demo button reopens the same file, so edits made there are preserved. A screenshot of the working query is in [docs/smartsql-demo-query.jpg](docs/smartsql-demo-query.jpg).

The **Запрос словами · прототип** bar accepts a deliberately limited Russian grammar, for example `Покажи записи, где Документы = Нет и Курс = 3`. It translates recognized conditions into the same structured filters as the visual UI; it does not generate or execute arbitrary SQL. This is a deterministic interaction prototype, **not a language model**. Unsupported requests produce an explanation instead of a guessed operation.

The application creates a local SQLite database on first launch. Users can also create, open, back up, and restore database files; create, rename, and safely delete tables with typed fields; add, rename, or safely delete fields; add/edit/delete records; search, sort, combine and save reusable field filters; and browse records in pages of 100. The last cell edit can be undone. CSV files can be mapped, previewed, and imported into an existing table or exported for Excel. Typed JSON table exchange is also available. Relations, field type conversion, XLSX exchange, a local language model, additional DBMS providers, and server mode are not implemented yet.

Database actions are grouped under **База**, and selected-table actions under **Импорт**, **Экспорт**, and **Структура**. The natural-language prototype expands on demand. File reading and writing run in the background while the workspace controls are temporarily unavailable, preventing table switches during an operation.

Saved filters belong to one table and retain stable field identifiers when fields are renamed. Saving under an existing name replaces that filter's conditions. Deleting a saved filter removes only the reusable preset; it does not modify table records or the conditions currently displayed.

Deleting a table or field first displays the number of affected records and saved filters, then requires the exact target name. SmartSQL creates a separate SQLite backup beside the active database before the transaction starts. Deleting a field also removes complete saved-filter presets that depend on it, rather than silently changing their meaning. The final field of a table cannot be removed separately; delete the table instead.

Restore validates the selected SmartSQL database in a temporary, non-pooled copy and migrates that copy if needed. The selected source file is never modified. The current database is backed up automatically before SQLite replaces its contents, and the UI requires the exact current filename as confirmation. Unrelated or newer incompatible SQLite files are rejected before the current database changes.

Field types in this first version are Text, Number, Date, and Yes/No. Numbers accept a comma or period as the decimal separator. Dates accept `DD.MM.YYYY` and `YYYY-MM-DD`. Yes/No accepts `Да`/`Нет`, `true`/`false`, or `1`/`0`. Empty cells are allowed. Existing fields from older databases migrate as Text; a backup is made before migration. Changing or removing a field's type is not yet supported.

CSV import accepts UTF-8 files with comma or semicolon separators. The delimiter is detected automatically and can be corrected manually. Each source column can be mapped to any compatible SmartSQL field or explicitly skipped; one target field cannot receive multiple source columns. Omissions require explicit confirmation. The preview shows a row count, mapping, and the first five rows; no data is written until confirmation. Type validation runs before the preview, and the import is all-or-nothing with limits of 100,000 records and 50 MB per file. CSV export uses UTF-8 with BOM and semicolons. For spreadsheet safety, values that could be interpreted as formulas receive a leading apostrophe, so Excel-oriented exports are not byte-for-byte round trips for those cells. Export does not overwrite an existing file.

**Таблица из CSV** creates a new table directly from a UTF-8 CSV file. The dialog lets you edit every field name and choose Text, Number, Date, or Yes/No before import. It shows a sample value for each column and validates all rows before creating anything. Table metadata and records are then written in one transaction; an error rolls the entire new table back. Existing-table CSV import remains a separate action.

JSON exchange uses the portable `SmartSQL.Table.v1` format described in [docs/JSON_TABLE_FORMAT.md](docs/JSON_TABLE_FORMAT.md). It preserves field names and types, nulls, text, exact decimal numbers, ISO dates, and Boolean values. Import appends to an existing table whose field names and types match, with a preview of the first five rows and all-or-nothing confirmation. Unknown capabilities and properties are rejected rather than ignored. It does not transfer saved filters, relationships, record identifiers, or an entire database.

Both CSV and JSON export include the **entire selected table**, regardless of the current page, search, or filters. They write to a temporary file beside the destination and publish the destination only after successful completion. Existing files are never overwritten, and a failed export removes its own temporary file.

## Solution Structure

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
dotnet build SmartSQL.sln
```

Run the desktop app with `dotnet run --project src/SmartSQL.Desktop/SmartSQL.Desktop.csproj`. The default data file is created at `%LOCALAPPDATA%/SmartSQL/smartsql.db` on Windows or the equivalent local application-data directory on other systems. Use the **База** menu to create, open, or back up other database files.

## Test

```powershell
dotnet test SmartSQL.sln
```

The tests cover the model, SQLite persistence, search, safe migration, CSV/JSON exchange, decimal precision, and atomic file export.
