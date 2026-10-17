# M09 / T13→T14: native export/import, exactness and atomicity

> **Контракт/тест-дизайн, НЕ выполненный экспорт.** [WORK_PLAN](../WORK_PLAN.md) — единственный план; [CAPABILITY_MATRIX](../CAPABILITY_MATRIX.md) — текущие реальные возможности. Основа на main `7e6aa2c` прочитана: `IReadOnlySqlExecutor`, `IDatabaseBrowser`, `DatabaseQueryView`, `XlsxTableTransfer`. Не расширять права providers скрыто.

## 1. Приоритет: T13 экспорт раньше T14 импорта

| Сценарий | Источник, consistency | Честная кнопка/результат |
| --- | --- | --- |
| Текущая страница обычной native таблицы | Уже загруженные строки одной страницы/снимка | «Экспортировать текущую страницу», row/page/scope count |
| Текущий показанный SQL-результат | Строки, **которые уже вернул ограниченный SQL** | «Экспортировать показанный результат», явная пометка неполноты/лимита |
| Вся таблица | Отдельный provider-approved стабильный streaming read snapshot, не loop разных current pages | «Экспортировать всю таблицу», snapshot start/complete/report |
| Отфильтрованный native набор | Та же фильтрация/типизированные AND/native sort **до pagination**, один consistent snapshot | «Экспортировать результат фильтра», без утверждения «вся таблица» |
| Managed проект/таблица | Существующий CSV/JSON/XLSX scope согласно действующим адаптерам | Сохранять отдельный contract, не выдавать за native SQL-result/new export |

Подтверждено кодом: `SqlReadRequest` допускает `RowLimit=1..500`, результат имеет `Rows`, `Columns`, `StorageClass`, `WasTruncated` (проверить точное свойство перед кодом). SQL text ограничен 65 536 UTF-8 bytes, лимиты на columns/cells/result/time; `DatabaseQueryView` держит уже полученный `SqlReadResult`. Это **не streaming API** и не основание тайно re-run SQL ради экспорта. Native table browser ограничивает страницу 500. Отдельный streaming provider reader обязателен для consistent full-table export.

## 2. Native values fidelity matrix

| Data | JSON / Portable structured | CSV | XLSX | Gate |
| --- | --- | --- | --- | --- |
| SQL NULL | Typed explicit null metadata | Empty cell alone **ambiguous** | Blank cell alone **ambiguous** | Before operation disclose/sidecar exactness or refusal for lossless claim |
| Empty vs whitespace text | Explicit stored text | Quoted empty text convention must be defined; imports often collapse | String cells, spreadsheet trimming/display pitfalls | Round-trip exact raw for claimed formats, otherwise loss report |
| PostgreSQL NUMERIC (very large precision)/SQLite integer64 | UTF-8 exact decimal lexical/text with native type metadata | Text value, may be interpreted as scientific notation by Excel | Store as text with warning to avoid 15-digit spreadsheet rounding | Never convert via .NET double or decimal silently |
| Real floats / NaN / Infinity and signed zero | Provider-native exact display + ordinal/type | Locale-invariant serialized text if support is confirmed | Value/text choice and exactness disclosure | Round-trip numeric bit or explicitly not supported |
| Date/time/timestamptz/timezone | Preserve native textual representation and timezone semantics where available | Data text, no timezone typed cells | Excel serial date has limited semantic scope | Do not convert timezone to local without consent |
| Binary/BLOB/BYTEA | Base64/hex + length + type metadata | Encoding + prefix must be documented | XLSX limited/non-native; warn/refuse lossless | Never silently drop binary cells |
| Text CSV injection (leading =,+,-,@) | Keep raw in structured form | Spreadsheet-safe escaping when viewed in Excel; byte-identical import not automatic | Store as string cell not formula | Do not execute spreadsheet formulas from DB content |
| Unknown native types/arrays/JSON/domain | Native textual form plus original type ID where safe | Flat text lossy | Limited | Report unsupported; do not fabricate typed conversion |
| Duplicate column names/empty labels/ordinals | Preserve ordinal keys | Column label collision possible | Duplicate headers possible | Ordinals mandatory, no silent merging |
| Empty table / zero-row result | Fields/ordinal metadata remains | Header-only with disclosed schema loss | Header-only sheet | No fabricated row |

**Representability rule:** The generic CSV/XLSX exports are useful human-facing formats, **not** guaranteed byte-identical backups; user must see loss warnings for unrepresentable types. Need separate explicit structured format/contract before claiming portable native exact exchange. This is a proposed direction, not acceptance of a new format by D056.

## 3. Export pipeline contract (M09.01–.04)

1. User chooses `scope + format + destination`. Before actual read report provider, database/schema/table/query result snapshot scope, row estimate if known, native value caveats, and selected subset vs entire table.
2. For already displayed SQL result: export **immutable snapshotted result rows**, including `WasTruncated`, `RowLimit`; do not silently rerun SQL or assume full DB scope.
3. For full table/filter: Infrastructure opens a **new dedicated read-only consistent snapshot/session**, takes provider-native metadata+row cursor; fields identified with confirmed identifiers, values read with explicit UTF-8/text/native type limits. Avoid using independent pages as if one snapshot. Existing native browse sessions/handles remain isolated.
4. Stream bounded-size chunks with cancellation/progress/backpressure. Outputs to unique sibling temp in allowed D: dev path; close+flush+validate; publish atomic no-overwrite. Destination conflicts get explicit choose-new/retry/cancel; no silent overwrite.
5. Finish returns `row count, columns, scope, checksum, warnings, truncation status`; value conversion decisions recorded in report. Cancel/out-of-space/read failure leaves destination unchanged and preserves source DB.
6. Read back exported **synthetic** test samples using own reader or independent verifier; values/schema compared against declared format representability, not a fake `all identical` when source includes BLOB/timezone.
7. Tests for quoting/CSV formula strings, invalid UTF8, large fields/long names, duplicate columns, empty source, concurrent writer, timeout/cancel, permissions and read-only page invariants.

## 4. Import gate (M09.05–.08), independent write capabilities

Native PostgreSQL **single-row** INSERT and native SQLite **single-cell** UPDATE do not imply safe bulk import capability. For each provider:

- Fresh metadata, qualified target, separate allowed operations and effective DB permissions, column mapping+identity/generated/default, immutable preview of *source*→*saved*; no SQL execution from Excel/CSV strings.
- Batch write requires provider-specific guarded transaction, constraints and role/trigger/relation policies, checked conflicts, cancellation before COMMIT and explicit unknown-COMMIT state afterward. No automatic repeat of unknown write, even if UI says refresh failed.
- Replace/upsert requires separately accepted conflict strategy; first slice can be explicit **append only** and may refuse unsupported providers/tables.
- SQLite ordinary native INSERT currently **not implemented**; do not bypass its guard via hidden unrestricted SQL or simply use a managed importer on foreign schema.
- Clipboard paste must reuse the same provider-specific mapping/validation/transaction/confirmation contract, not a weaker special path.
- Mid-row failure/cancel/missing permission/constraint failure must have exact transaction outcome; no silent half-population. Expected atomicity is *within one provider DB transaction* only; no promise of cross-provider/global transaction.

## 5. 18 acceptance cases: N01–N18

| ID | Expected |
| --- | --- |
| N01 | SQL result at 500 max shows bounded scope, exported rows exactly displayed, no hidden rerun |
| N02 | SQL result truncated=true still shows truncation/warning and has no full-table label |
| N03 | Native full table 1000+ rows under concurrent writer uses *one* consistent snapshot |
| N04 | Native filter/sort/search applied before exporting beyond first page |
| N05 | Duplicate/quoted/Unicode columns preserve ordinal and label mapping |
| N06 | Large PostgreSQL NUMERIC and SQLite Int64 not rounded |
| N07 | NULL/empty/whitespace round-trip when format claims exactness, otherwise explicit loss |
| N08 | TEXT beginning =,+,-,@ exports Excel-safe as text, not formula |
| N09 | BLOB/BYTEA exact if supported, otherwise explicit block/warning |
| N10 | DATE/timestamptz/timezone semantics and unsupported conversions disclosed |
| N11 | Empty table keeps columns, zero rows, no fabricated data |
| N12 | Cancellation or IO full before publish leaves destination untouched |
| N13 | Existing destination never overwritten without separate recovery contract |
| N14 | Revoked/read-only permission, schema changed during selection => explicit error |
| N15 | Import preview shows identities/generated/required/default/duplicates |
| N16 | Mid-batch constraint failure rolls back whole declared batch or refuses unsupported target |
| N17 | Unknown COMMIT has distinct status, no automatic retry |
| N18 | Clipboard paste and file import share validation, no side door to writes |

## 6. Implementation slicing and file owners

**N13a/M09.01–.04:** pick **already displayed SQL-result export** first (bounded, no provider write scope), using shared `AtomicFileExport` if compatible, explicit output scope and formatting warnings. Add `tests/SmartSQL.Application.Tests/..` contract tests and focused Desktop tests; separate native full-table export is next slice with new Infrastructure cursor/metadata contract. Once verified close **T13 only**.

**N14a/M09.05–.08:** design mapping/validation and rejection before any mutation. PG guarded batch and SQLite ordinary INSERT are **independent later slices** with own safety review; do not mark T14 done based on preview or managed CSV code.

Main reference code: `src/SmartSQL.Application/IReadOnlySqlExecutor.cs`, `IDatabaseBrowser.cs`, `src/SmartSQL.Desktop/Views/DatabaseQueryView.cs`, `src/SmartSQL.Infrastructure/XlsxTableTransfer.cs`, `AtomicFileExport.cs`, `PostgresReadOnlySqlExecutor.cs`, `tests/SmartSQL.Infrastructure.Tests/XlsxTableTransferTests.cs`. Exact path and APIs recheck HEAD before implementation.

**Статус:** tests and code above have **not** run or been added. Source references are static inspection; all 18 N scenarios are proposals, not passes.
