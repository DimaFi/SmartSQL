# SmartSQL Personal: функциональная матрица

**Срез справочника:** DBeaver Lite 26.2 (07.09.2026), публичные официальные документы в [REFERENCES](REFERENCES.md). **Срез SmartSQL:** main / документы 17.10.2026, без исполнения тестов в этой документационной ветке. Матрица служит **предложением расширения охвата**; единственный план исполнения — [../WORK_PLAN.md](../WORK_PLAN.md). Требует повторного аудита перед реализацией.

Легенда SmartSQL: **Есть** — подтверждено кодом в оговорённом scope (не обязательно ручным UI); **Частично** — ограниченная реализация; **Нет** — отсутствует; **Проект** — обсуждено/запланировано без реализации. Приоритет P0 критический UX/security, P1 ежедневные функции, P2 функциональный Personal, P3 поздние персональные расширения. Столбец «Дальше» — предлагаемая проверяемая работа, не обещание даты.

## A. Начало, оболочка, жизненный цикл

| ID | Возможность | Сейчас | Дальше / приёмка | Зависимость |
|---|---|---|---|---|
| UI-01 | Одно окно, дерево источников, вкладки | Есть, UX частично | Ясное дерево «Проекты / Подключения», активный контекст, раздельные сессии | M02/M10, UF09-01 |
| UI-02 | Excel-first / advanced раскрытие | Есть режимы | Нет потери фильтров/данных/прав при смене; не перегружать Simple | M02/M10 |
| UI-03 | Welcome: создать/открыть/импортировать | Нет | 3 главных действия + recent, путь, ошибки | M03 |
| UI-04 | Недавние файлы/подключения | Частично профили | Тип источника, last used, отсутствующий источник ≠ новая БД | M03 |
| UI-05 | Тема, единые векторные иконки | Есть основа | Согласованные tokens, контраст, состояния, тёплый нейтральный фон и зелёный акцент | M10 |
| UI-06 | Tabs split / закрепление и восстановление | Частично tabs | Не открывать неверные БД автоматически; серийное восстановление без паролей | M10 + storage |
| UI-07 | Глобальный поиск объектов и команд | Нет | Cmd/Ctrl+K с контекстом, результат доступен клавиатуре | После M03/M10 |
| UI-08 | Drag reorder columns, order at create | Нет | View reorder без DDL; schema reorder отдельно | UF09-03, M10/M16 |
| UI-09 | Состояния и прогресс | Частично | Empty/loading/invalid/permission/offline/cancel, операционный центр | M10 |
| UI-10 | RU полный и EN | Частично RU | English только после полной проверенной локализации | M13 |
| UI-11 | Доступность | UI-приёмка открыта | 100/150/200%, keyboard/focus, screen reader, large text | M02/M10 |
| UI-12 | Установщик/updates/portable | Частично preview ZIP/EXE | Win/Linux/macOS, подписанные релизы, migration/rollback | M11, M01 |
| UI-13 | Два отдельных Undo и History | Только локальный single-cell undo | Ясно различать история действий, undo, rollback, DB audit | UF09-06, M17 |

## B. Подключения и поддержка систем

| ID | Возможность | Сейчас | Дальше / приёмка | Зависимость |
|---|---|---|---|---|
| DB-01 | Managed SQLite проекты | Есть | Регрессия schema/CRUD/copies с честной семантикой типов | M02/M04 |
| DB-02 | Ordinary SQLite без SmartSQL metadata | Есть, частичный UPDATE | Дополнительные безопасные операции отдельными capabilities | M09/M16 |
| DB-03 | PostgreSQL сервер | Есть; ограниченные writes | UI gate, profile/TLS, версия/роль, live metadata | M02/M09 |
| DB-04 | MySQL/MariaDB | Нет | Собственный driver adapter, реальная integration matrix | M14 |
| DB-05 | Microsoft SQL Server | Нет | SqlClient, TLS, identities, rowversion, permissions | M15 |
| DB-06 | Другие SQL/NoSQL/BigData драйверы | Нет | Отдельный provider feasibility + типы + поддержка read/write | После M15, не «любая БД» |
| DB-07 | Добавление/тест/переименование соединений | Частично | Понятный wizard, безопасный test, диагностика без secrets | M03/M10 |
| DB-08 | Пароли/секреты через OS vault | Пароль на сессию | Opt-in OS keychain, secure migration, no plaintext fallback | Security ADR |
| DB-09 | TLS CA/cert/SSH tunnel | TLS policy ограниченно | Точные проверки, SSH via vetted lib, no insecure defaults | M03.05/provider |
| DB-10 | SSO/Kerberos/OAuth/device auth | Нет | Раздельные provider/auth adapters, корпоративные integration tests | P3 / конкретная СУБД |
| DB-11 | Server roles/permissions, read-only status | Частично provider checks | Человекочитаемый effective capability; не симулировать серверные права | M16/M17 |
| DB-12 | Отключение/reconnect/timeouts | Частично | Отдельный жизненный цикл каждой сессии, no ghost writes | M02 |
| DB-13 | Множество профилей | Есть для PG | Группы/избранное/поиск без помещения паролей в JSON | M03/M10 |

## C. Просмотр и редактирование данных

| ID | Возможность | Сейчас | Дальше / приёмка | Зависимость |
|---|---|---|---|---|
| DATA-01 | Пагинация и нативные значения | Есть | Стриминг/виртуализация, точный count/unknown, лимиты | M02 |
| DATA-02 | Табличный и карточный view | Табличный есть | Read-only record view для узких/широких таблиц | P1 |
| DATA-03 | SELECT, text search, typed filters/sort | Есть с ограничениями | Filter builder OR/grouping только после безопасного AST | M02/M16 |
| DATA-04 | Filter by cell/context, presets | Managed presets; native частично | Для native — сохранение по object/profile ID, scope и expiry | P1 |
| DATA-05 | Колонки hide/freeze/reorder/width | Частично | Удобная панель «Столбцы» без изменения физической схемы | M10 |
| DATA-06 | Inline cell edit | Managed + native guarded | Типо-адаптивные редакторы, edit/diff/commit/cancel | M02 |
| DATA-07 | Native INSERT/DELETE | PG частично; SQLite нет | Доказанный provider-specific scope, pre-write safety | M09/M16 |
| DATA-08 | Batch edit/paste/update | Managed paste | Native staging + preview, atomicity/conflicts, limits | M09/M16 |
| DATA-09 | NULL vs empty vs whitespace | Native учитывает | Явная визуализация, политика управляемого проекта | M04 |
| DATA-10 | Булевы поля | Валидация есть; UI проблема | «Да/Нет/Не задано» при допустимом NULL, round-trip | UF09-05 |
| DATA-11 | JSON/XML/BLOB/image/hex value viewers | Только native text/BLOB hex частично | Bounded sandboxed previews, без HTML/script execution | P2 |
| DATA-12 | Clipboard multi-range/copy cell formats | Managed insert | Чёткое направление/схема, escaping, CSV formula injection | M09 |
| DATA-13 | Data colors/conditional styles | Нет | Локальные правила отображения без SQL mutations | P2 |
| DATA-14 | Grouping/aggregation/summary | Нет | Distinct NULL semantics, точность, server-vs-local badge | M12 |
| DATA-15 | Query history/activity log | Ограниченная локальная SQL история | Поиск, явная чувствительность SQL/параметров, retention | M17 |
| DATA-16 | Undo/revert operations | Managed one-cell | Proven reversible actions only; server writes no fictitious undo | M05/M17 |
| DATA-17 | Query/result export scope | Managed local table | Selection/page/full result distinction, consistent snapshot | M09 |

## D. SQL IDE и визуальные запросы

| ID | Возможность | Сейчас | Дальше / приёмка | Зависимость |
|---|---|---|---|---|
| SQL-01 | Независимые SQL tabs/.sql UTF-8 | Есть | Unsaved indication, file conflict, split editor, search | M02/M10 |
| SQL-02 | Подсветка/IME/редактор | Простой подсветчик | Lexing per dialect, find/replace/indent/brackets/large files | M02/P1 |
| SQL-03 | Native SELECT read-only | Есть bounded | Сохранить как безопасный предварительный режим | M02 |
| SQL-04 | Manual DML/DDL execution | Нет | Отдельный explicit advanced session с DBMS permissions, transaction, safety UI | После M17 policy и acceptance |
| SQL-05 | Multi-statement script runner | Нет | Разделение реальным dialect parser, transaction and partial failure | P2 |
| SQL-06 | Transaction panel, commit/rollback | Нет | Real connection-bound transaction, failed/unknown commit semantics | P2 |
| SQL-07 | Autocomplete keywords/objects | Нет | Метаданные по lazy cache, context/dialect, cancellation | P1 |
| SQL-08 | Formatter, snippets, lint | Нет | Independent implementation or licensed library, no query mutation | P1 |
| SQL-09 | Statement params/bindings | Нет | Prepared params, never interpolate data into identifiers/SQL | P2 |
| SQL-10 | EXPLAIN graphical/text | Нет | Provider-safe default EXPLAIN, ANALYZE separate warning | P2 |
| SQL-11 | Query history search/statistics | Ограниченная 30 successful entries | Full privacy/retention UI, per connection and status | M17/P2 |
| SQL-12 | Visual Query Builder | Нет | AST SELECT/JOIN/WHERE/GROUP/ORDER; preview generated SQL | P2 |
| SQL-13 | SQL↔Builder round-trip | Нет | Honest supported grammar; unsupported query remains text-only | P2 |
| SQL-14 | SQL diagnostics and quick-fix | Частично errors | Lexical/syntax diagnostics separated from server errors | P2 |
| SQL-15 | SQL execution progress/cancel | Частично read-only | timeouts, cancellation/drain, connection state transitions | M02 |
| SQL-16 | AI-assisted SQL | NaturalLanguageQuery rule-based managed; ML separate | Proposed actions only via Action/Permission; local first | M17/M18 |

## E. Схемы, диаграммы, миграции

| ID | Возможность | Сейчас | Дальше / приёмка | Зависимость |
|---|---|---|---|---|
| META-01 | Catalog schemas/tables/views | PG/SQLite есть | Provider expansion and object search, accurate unknown coverage | M14/M15 |
| META-02 | Columns/PK/FK/index/constraints | Частичный inspector | Complete vs Partial explicitly; incoming deps, triggers, domains | M16 |
| META-03 | ER diagrams (read-only) | Нет | Graph from confirmed FK, manual positioning, zoom, export | P2 |
| META-04 | Schema changes managed | Частично есть | Drop/rename safe backups and precise rollback | M16 |
| META-05 | Native DDL wizard | Нет | Diff/preview SQL, privilege checks, critical confirmations | M16 |
| META-06 | Full schema compare/migration | Нет | Отложено: не часть базового Lite; расширенный Personal+ | После P2/M16 |
| META-07 | Data compare/sync | Нет | Отложено: Enterprise-level advanced optional | После Personal |
| META-08 | Dependency map/impact preview | Нет | Verified inbound FK, view dependencies, unknown markers | M16 |
| META-09 | Version aware native DDL | Нет | Compatibility + capability by version, not generic rename | M16 |
| META-10 | Schema snapshots | Backup partial | Precise scope: schema-only, data-only, full | M05/M16 |

## F. Перенос, графики, инструменты

| ID | Возможность | Сейчас | Дальше / приёмка | Зависимость |
|---|---|---|---|---|
| XFER-01 | Managed CSV/JSON/XLSX/clipboard | Есть | Полный Excel-first wizard, scope/loss diagnostics | M04 |
| XFER-02 | Native table → CSV/JSON/XLSX | Нет | Streaming read with exact native values, warnings, progress | M09 |
| XFER-03 | CSV/XLSX → native table | Нет | Mapping/stage/preview/confirm/rollback, DB-specific | M09 |
| XFER-04 | Cross-provider copy | Нет | Type matrix/precision/timezone/BLOB/PK/FK, dry-run | M16 |
| XFER-05 | Task saved presets/schedule | Нет | Local secure scheduler only after reliable backup & idempotency | P3 |
| XFER-06 | Analytics charts | Нет | Bars/lines/pie/scatter from bounded exact source with provenance | M12 |
| XFER-07 | XLS (legacy) | Нет | Separate safe converter/import adapter after threat assessment | P2 |
| XFER-08 | Query export/import and encoding | UTF-8 SQL files | Safe SQL encoding choice + conflict handling | P2 |
| XFER-09 | GIS/spatial views | Нет | Provider-type-specific read-only viewer; not execute untrusted markup | P3 |
| XFER-10 | Data generator / mock data | Нет | Optional future advanced; synthetic fixtures first | P3 |

## G. Безопасность, пользовательская надёжность

| ID | Возможность | Сейчас | Дальше / приёмка | Зависимость |
|---|---|---|---|---|
| SEC-01 | Fail-closed local storage root | M01.04 code done | User/clean-PC gates, migration M01.05, races M01.06 | M01 |
| SEC-02 | SQLite consistent snapshots | Есть локальные primitives | Shared catalog/restore/source identity/checksum | M05 |
| SEC-03 | PG backup | Нет | pg_dump/pg_restore through official tools, least privilege | M08 |
| SEC-04 | Cloud backup providers | Нет | OAuth scoped / verified restore / retries | M06/M07 |
| SEC-05 | Secrets | Session-only passwords | OS vault opt-in; profiles remain nonsecret | P2/ADR |
| SEC-06 | Action/Permission/Audit | Нет | Distinct human/AI execution policy, structured audit | M17 |
| SEC-07 | DB error and uncertain commit | Guarded writes partly | No retry of unknown commit; reconciliation state | M02/M17 |
| SEC-08 | Platform parity and accessibility | Частично | Windows/Linux/macOS, offline no-D: clean box | M11 |
| SEC-09 | Safe updates/installer | Partial packaging | Signature, upgrade, rollback, backup before migration | M11 |
| SEC-10 | File/URL/SQL sensitive-content privacy | Partial | No plaintext secrets in logs; scrubbed diagnostics, opt-in AI | M17/M18 |

## H. Граница с Team

**Не приписывать Lite:** централизованные пользователи и роли Team, общие подключения/скрипты в реальном времени, серверное администрирование, коллективная история, web workspace. **Не приписывать Lite также** Enterprise-only schema compare, data compare, Git project sync как обязательные функции. Если хотим их лично — это независимые функции SmartSQL, не аргумент «паритет Lite». См. [TEAM_FUTURE](TEAM_FUTURE.md).

## I. Definition of done для строки

Реализация не считается полной без: user job → provider/version scope → разрешение/права → точное сохранение типов → cancel/error/conflict → recovery → код/автотесты → manual UI на 100/150/200% → package smoke → обновлённая CAPABILITY_MATRIX с доказательствами. Ни один общий интерфейс не может скрывать фактическое несовпадение поддержки на PostgreSQL и SQLite.
