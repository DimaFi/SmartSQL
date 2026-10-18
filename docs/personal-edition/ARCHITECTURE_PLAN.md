# Архитектурное расширение SmartSQL Personal

Статус: **proposed architecture, not implemented**. Приоритет — переиспользование существующего C#/.NET 10/Avalonia 12, стабильного хранилища, тестов и принятых контрактов. Не менять D001–D058 без отдельного решения; не вводить ради «паритета» собственную СУБД, сервер, Java/Eclipse, интерпретатор SQL или универсальный формат, который теряет native semantics.

## 1. Уже работающие seams — использовать, не дублировать

| Точка | Уже есть | Как расширять |
|---|---|---|
| `SmartSQL.Domain` | DataModel/DatabaseCatalog; независимость от провайдеров | Только сущности, если домен действительно требует; native types не принуждать к 4 managed типам |
| `SmartSQL.Application/IDataWorkspace.cs` | Managed data/schema/CRUD | Сохранять отдельный сценарий локальных проектов, расширять под M04/M05 после compatibility decisions |
| `IDatabaseBrowser.cs` | Native metadata, rows, paging, typed browse | Опциональные provider capabilities и корректные scope/completeness |
| `IDatabaseEditor.cs` | Explicit keyed cell update | Не обещать bulk/schema из CanUpdate |
| `IDatabaseRowCreator.cs`, `IDatabaseRowDeleter.cs` | Независимые INSERT/DELETE возможности | Отдельные guards/rights/conflict semantics |
| `IReadOnlySqlExecutor.cs` | Strict bounded read-only SQL | Оставить безопасным; отдельно вводить manual execution API, не расширять небезопасно этот контракт |
| `DatabaseStructure.cs` | Partial PK/FK/index/constraints | Metadata enrichment + Coverage; ERD только по известным связям |
| `DatabaseQueryCapabilities.cs` | Per-column predicates/sort | Server-side AST/visual filtering с отказом неподдержанного |
| `SqliteWorkspace.cs` | Existing managed storage and compatibility | Backup/copy-first and schema constraints не ломать |
| `Postgres*.cs`, `NativeSqlite*.cs` | Provider-specific SQL / read/guards | New drivers as separate adapters; no fake generic SQL |
| `WorkspaceWindow.cs` | Shared tree, tabs, independent sessions | Welcome, view states, compact inspector, command services; не создавать другой основной UI |
| `Styles/Tokens.axaml` | Semantic theme tokens | UX components consume dynamic resources; no hard-coded colors |
| `WorkspacePaths`, StorageRuntimeRoot | Runtime root & process lease | Storage changes gated by M01.04–M01.06 and D056 |
| `JsonConnectionProfileStore`, QueryHistoryStore | Nonsecret profiles, bounded SQL history | Versioned migrations, secret protection and privacy separately |

## 2. Не путать границы ответственности

```text
Desktop (Avalonia Views + ViewModels + commands)
      │  result/error/capabilities only
Application (use cases / validation / policy / contracts)
      │  provider-neutral intentions, never SQL syntax construction
Infrastructure (SQLite / PG / MySQL / SQL Server adapters)
      │  parameterized vendor-specific commands / native drivers
Existing DBMS server or local engine (storage, locks, transactions, roles)

Optional later: AI → structured intent → action plan → permissions → preview → confirmation → audit
Optional later: Team server ↔ shared workspace metadata, not a new DBMS
```

**Compile references** должны соблюдать существующее направление: Application → Domain, Infrastructure → Application/Domain, Desktop composition root может зависеть от Infrastructure, Domain не знает об Infrastructure/Desktop. Вводить ViewModel не означает переносить SQL в Desktop.

## 3. Capability-first вместо одного интерфейса «умеет всё»

В разных СУБД и даже между таблицами одной БД возможности расходятся. Не надо заменять `IDatabaseBrowser` монолитным `IDatabaseManager`. Ввести только нужные опциональные контракты поэтапно:

- IConnectionDiagnostics — test/health/server version/current role, без повышения прав.
- IObjectCatalog — schema/catalog hierarchy + paging/filtering + metadata coverage.
- IManualSqlSession — explicit manual user-owned SQL и transaction lifecycle, **отдельно** от `IReadOnlySqlExecutor`.
- IExecutionPlanProvider — server-specific safe EXPLAIN; ANALYZE/EXECUTE может запускать фактический запрос, требует отдельного предупреждения.
- IQueryBuilderDialect — рендерит *валидированный* AST и identifiers внутри provider SQL generator; values только parameters.
- IDatabaseDiagramSource — собранные верифицированные FK nodes/edges, partial hints.
- IDataTransferSource / IDataTransferTarget — declared types/source consistency/key/merge capabilities, preview before commit.
- IValuePreviewProvider — bounded native BLOB/JSON/XML/spatial readers; выполнять HTML/JS нельзя.
- IScheduledTaskRunner — только когда есть verified snapshots, vault и idempotent lifetime.

Capability answer = **подтверждённая возможность операции для конкретного объекта и сессии**, а не глобальный provider enum. Может измениться при смене роли, схемы, server version, concurrent DDL. Application отображает capability и проверяет перед действием; Infrastructure обязательно проверяет повторно перед commit. User/UI mode ≠ authorization.

Ошибки provider diagnostics возвращать с безопасным кодом, уровнем и рекомендацией; технический стек в logs без credentials, raw SQL/parameters не логировать по умолчанию.

## 4. Корректность нативных типов

Разделять source-native `int8`, `numeric(80,20)`, `date`, `timestamp with time zone`, `UUID`, `BLOB`, `JSON`, `ARRAY`, `domain`, server collation и raw SQLite affinity. Нельзя хранить любой native тип в `decimal` или преобразовывать данные в local managed `Text/Number/Date/Boolean` без осознанного conversion report.

Представление значений: `NULL` — explicit null; `""` — пустой текст; `" "` — пробел; `false` — Boolean; `0` — число. Native arrays/binary/GIS могут показывать placeholder с типом и отдельным bounded reader. Исходный тип и точная сериализация могут потребовать отдельной модели `NativeValue`/lazy reader — только после тестов и согласования контрактов.

Для cross-provider transfer: type mapping table, source row count/consistency, lossy flags, bytes/precision/timezone, escape formulas on Excel export, per-row errors and fail-on-loss default. Export != backup.

## 5. Data editor и identity

Сессия таблицы хранит `connection identity + catalog/schema/object + page/query/sort/version`. Однаковые имена из разных источников не сливать. Редактирование — через **transient provider handle** с snapshot/PK guard, не через строковый номер, не через ORDER BY row position. При успехе handle consume, refresh; при неизвестном COMMIT не retry. Native SQLite текущий prewrite-backup сохраняется, пока новая политика не прошла собственные gates. Для массовых изменений необходим отдельный prepare/dry-run/confirmation/transaction guard; выбор строк не означает согласие на редактирование.

UI-only column order хранить отдельно от физической схемы и native key identity; drag не вызывает ALTER. Label/width/visibility/freeze — local view preferences.

## 6. SQL execution: два разных режима

**Existing** `IReadOnlySqlExecutor`: ограничение одного read-only statements, row/bytes/time budget, parser/authorizer, отдельная read-only сессия. Это безопасный current capability.

**Proposed manual advanced SQL**: явный intent и scope, привязка к конкретному connection/session/transaction, auto vs manual commit status, prepared parameters, cancellable progress, server diagnostics, no hidden retry; script runner — следующий отдельный срез. Результаты SELECT и affected rows, warnings, unknown outcomes показывать раздельно. Raw SQL от пользователя разрешается в рамках серверных прав после UI политики; raw SQL от AI никогда не выполняется без независимой структурной проверки Action/Permission. Не выдавать визуальный фильтр за разрешение исполнять произвольный `WHERE` фрагмент.

Транзакция — ресурс provider connection, а не просто checkbox в UI. Close/Disconnect/Exit с pending tx: запросить commit/rollback/cancel, завершать корректно; unknown COMMIT -> reconcile/reconnect, никогда автоматический повтор неизвестно исполненной операции.

## 7. SQL IDE: постепенный стек

- Stage 1: улучшить нынешние text editor/IME/undo/find/.sql lifecycle, без пакета parser.
- Stage 2: dialect-aware lexer/safe autocomplete по metadata; lazy cache and cancellation, no executing suggestions.
- Stage 3: formatter/lint/snippets, compiled/isolated parser выбран под нужные диалекты; оценить пакет лицензии и версионную совместимость.
- Stage 4: ручное выполнение изменяющего SQL с transaction panel.
- Stage 5: visual builder как **собственный AST** (SELECT/JOIN/WHERE/GROUP/ORDER/limit) и отдельный provider renderer, с честной непреобразуемой зоной для произвольного SQL.
- Stage 6: EXPLAIN, history/replay controls, performance diagnostics.
- Stage 7: AI assist после M17/M18; read-only generated query также требует provider-enforced policy, mutations — confirmation.

Для визуального builder: **не** редактировать SQL регулярными выражениями и не использовать текстовые concatenation escaping пользовательских значений. Identifier quoting принадлежит dialect; `SqlParameter` values не текстово подставляются.

## 8. ER diagram & analytics

ERD: источником являются **confirmed FK/PK constraints** из `DatabaseStructure`; incoming deps отдельно запрашиваются; при Partial/Unavailable нужен видимый indicator. Layout — UI service/graphic layer, не Domain engine. Drag/zoom/save positions — user view preferences, НЕ ALTER. Export PNG/SVG/PDF только с идентификаторами/текстом, без секретов; предотвращать unbounded graph/network lookup.

Charts: построение над bounded result/table snapshot с явным источником/временем и типами, точные числа сохранять отдельно от `double` для вычислений и подписи о возможной потере точности, если renderer использует float. Aggregate может выполняться server-side только как capability; local calculation ограничена scope. Не рисовать «вся таблица», когда загружена только текущая страница.

## 9. Безопасность / cloud / secrets

Стабильное installed/portable storage по D056; M01.04 tests не заменяют пользовательскую проверку. M05 snapshot local first, hash/restore verification, durable outbox; M06 Google/Yandex OAuth/TLS + M07 schedule, не копировать живой SQLite WAL как обычный файл. PG dumps provider-specific with official tools (M08). Any secret handling via opt-in OS vault protected key material; если ключ недоступен, отказ без fallback на plaintext JSON. SSH tunnels/new SSO только после threat model и реальной тестовой инфраструктуры.

Telemetry opt-in only; sensitive SQL, host/user info и идентификаторы базы не отправлять в аналитику по умолчанию. AI model/provider never sees full schema/data without explicit data scope and consent.

## 10. Функциональная граница будущего Team

Personal core должен оставаться автономным. До Team достаточно: source IDs, versioned profile contracts, stable local file semantics, action outcome model, opt-in audit seams. Не добавлять backend HTTP, multitenant auth или общую SQLite-синхронизацию ради «запаса». Team-specific решения и риски — [TEAM_FUTURE.md](TEAM_FUTURE.md).

## 11. Изменения только через вертикальные slices

Срез = UX contract + Application intent + provider behavior + unit/integration fixtures + error cases + accessible UI + manual acceptance + packaging evidence. Feature flags для опасных операций default off; не включать в release до gate. Текущие M01–M18 + 123 подзадачи остаются единственной очередью. Новые P2 фичи добавлять в WORK_PLAN в согласованном следующем цикле после фактических зависимостей и решения пользователя.
