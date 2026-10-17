# M08 — PostgreSQL backup/restore: контракт проверки и команды

> **Research/spec only, не код, не новый ADR и не доказанная приёмка.** Подчинено [WORK_PLAN M08](../WORK_PLAN.md). PostgreSQL 17 на loopback 15432 из HANDOFF — только ранее описанная синтетическая test fixture, **не** разрешение запускать её здесь. Фактический код PG backup в main не реализован. Опорный main `7e6aa2c71ec725d9ca76976f0268b823b2394ffc`.

## 1. Product scope and non-goals

| Что заявляем | Что не входит |
| --- | --- |
| Согласованный архив **одной** PostgreSQL database через официальную утилиту pg_dump | Весь кластер, глобальные роли/tablespaces, физический PITR/репликация |
| Переносимый `pg_dump -Fc` (custom archive), если проверена совместимость клиента и сервера | CSV из отдельных страниц, `SELECT *` по таблицам как эквивалент снимка БД |
| Восстановление в **новую изолированную БД** с проверкой успешного завершения | Автоматическое выполнение чужого dump в production/root/superuser |
| Snapshot вместе с M05 manifest/catalog/outbox только после сохранения и проверки | Синхронизация live PostgreSQL data-dir, общий server SmartSQL, backup ролей за один раз |

По PostgreSQL 18: `pg_dump` делает согласованный экспорт в условиях concurrent reads/writes, dump охватывает одну DB, только `pg_dumpall` поддерживает globals. `pg_dump` **не** обслуживает сервер новой major-версии, чем клиентская утилита; обратное восстановление на более старый сервер не гарантируется. Выбор `--schema` не гарантирует внешние зависимости. Официальные ссылки в конце.

## 2. Command execution boundary

1. Проверить разрешённый **путь** до исполняемого pg_dump/pg_restore и major versions, run `--version` с bounded time и stdout/stderr. Self-contained SmartSQL EXE не включает их автоматически; если utility отсутствует — обычная понятная ошибка + официальный PostgreSQL download, без тихой установки.
2. Аргументы собирать **отдельными токенами** через `ProcessStartInfo.ArgumentList`, без `cmd.exe`/`powershell -Command` и без string shell escaping. Host/db/schema пути не конкатенировать в строку shell. Отдельно валидировать output destination на D: dev root.
3. Не класть пароль в аргументы/командную строку, `PGPASSWORD` в общие env, лог или exception. Для subprocess runtime сделать минимальный поддерживаемый credential path (с отдельной Threat Review), например защищённый ephemeral passfile на разрешённом test root, с permissions, немедленной очисткой и without cross-process leak; не выдавать это за готовый секретный store без проверки.
4. `-Fc` → временный уникальный файл без overwrite → проверить exit code **и stderr warnings** → закрыть producer → вычислить local size/SHA → структурно прочитать TOC через `pg_restore --list` под bounded timeout → durable publish local artifact. TOC + hash — **не** доказанное успешное восстановление.
5. Scope: database ID/server version, client tool version, archive format, selected options, expected roles/extensions/schema exclusions, timestamp+hash; manifest не включает пароли/внутренние URI. Не включать connection password в export. `RLS` и ограничения прав: `pg_dump` по умолчанию пытается исключить RLS и может требовать elevated privileges; `--enable-row-security` меняет backup scope, а не служит прозрачным эквивалентом полной БД.
6. Cancellation: остановить только **own spawned process tree**; дождаться выхода/закрытия streams, удалить только собственный temp. Не прибивать пользовательские PostgreSQL процессы. Exit unknown/partial file ⇒ no VERIFIED state.
7. Restore только после explicit intent + заранее проверенной совместимости; сначала `pg_restore --list` (можно inspect), затем тест восстановления в fresh isolated database **под ограниченной ролью**, с `--exit-on-error` и подходящей транзакционной стратегией, а не через `--clean --create` поверх пользовательской DB. Archive от недоверенного сервера может запускать arbitrary source superuser-chosen code — это независимый security gate.

## 3. Provider pipeline statuses, distinct from M05

```text
TOOL_MISSING / INCOMPATIBLE / NEED_PRIVILEGE
     ↓ (after supported fix)
EXPORTING → TEMP_CLOSED → ARCHIVE_HASHED → TOC_READABLE
                                         ↓
LOCAL_ARTIFACT (not yet RESTORE_VALIDATED)
       ↓ (new isolated database + permission)
RESTORING → VERIFYING_SCHEMA_AND_DATA → RESTORE_VALIDATED
       ↓
M05 catalog/outbox (if product scope accepted)
```

Статусы UI: «Архив одной базы PostgreSQL создан», «Архив доступен для проверки», «Восстановление в новую базу проверено». Не писать «весь PostgreSQL защищён», если globals и server-level maintenance не включены. Не заявлять восстановление после одного `pg_restore --list`.

## 4. Restore verification scope

Synthetic fixture из разных схем: quoted Cyrillic identifier, PK/FK (включая cyclic deferrable), unique/partial indexes, sequence values, generated/identity columns, nullable vs empty, NUMERIC precision beyond .NET decimal, TEXT/BYTEA, date/time/timestamptz, UTF-8, partition example only when supported; как минимум один extension/owner/ACL dependency, RLS/permissions denial. Verify count + exact content hashes/types for sampled and complete bounded synthetic rows, relation and FK/index structure, sequence next-value correctness, re-open under ordinary role. Restore failure must leave **source server/database untouched** and never present partly restored target as clean.

## 5. 14 synthetic acceptance checks (PG-B01..14)

| ID | Проверка и pass criterion |
| --- | --- |
| PG-B01 | pg_dump not installed: clear error, no auto-install/no source mutation |
| PG-B02 | Tool older major than server: explicit incompatibility, no invalid dump |
| PG-B03 | Tool path whitespace/Unicode, quoted database names: no shell injection |
| PG-B04 | User lacks SELECT/object privileges: fail with scope disclosure, no superuser demand |
| PG-B05 | RLS/partial dump scenario: not silently called full backup |
| PG-B06 | Concurrent source writes: consistent restore of chosen snapshot, not page-by-page mixture |
| PG-B07 | Cancel/kill during dump: partial temp never catalogued as verified |
| PG-B08 | Full disk / read-only target / output collision: source untouched, previous backup retained |
| PG-B09 | stderr warning or exit nonzero: no «успех» by file size |
| PG-B10 | Bad/truncated archive passes hash of **bad bytes**: TOC/restore still fails; hash alone insufficient |
| PG-B11 | Archive with untrusted code: no automatic production/superuser restore, show risk |
| PG-B12 | Restored new database: PK/FK/indexes/sequence/NUMERIC/encoding/roles claimed scope rechecked |
| PG-B13 | Globals absent: visible disclosure, never claim cluster-wide backup |
| PG-B14 | Cloud M05/M06 queue: same immutable bytes/hash after transfer; remote copy not claimed restored until target test |

## 6. Следующий Codex slice и файлы

До кода: M08.01 utility discovery/versions/scope (Infrastructure adapter + pure process args tests, Application only small provider-agnostic capability). Затем M08.02 `-Fc` producing verified local archive with cancel/stderr/status; M08.03 restore into test fresh DB, M08.04 cloud only **after M05 pipeline**. Не заменять нынешние `PostgresReadOnlySqlExecutor` и row editor экспортом и не менять существующие PG write guards.

Перечень исходников проверять на актуальном HEAD: `src/SmartSQL.Infrastructure/Postgres*.cs`, `src/SmartSQL.Application/IReadOnlySqlExecutor.cs`, `tests/SmartSQL.Infrastructure.Tests/Postgres*Tests.cs`. Предполагаемые **новые**: `PostgresBackupToolDiscovery`, `PostgresSnapshotProducer` и их focused tests, но имена не являются предписанной архитектурой; не добавлять пакет до конкретной необходимости.

## 7. Официальные источники (проверено 17 октября 2026)

- [pg_dump (PostgreSQL current/18)](https://www.postgresql.org/docs/current/app-pgdump.html) — scope/versions/consistency/RLS/schemas/untrusted restore.
- [pg_restore](https://www.postgresql.org/docs/current/app-pgrestore.html) — archive restore, `--exit-on-error`, `--single-transaction`, executing code risk.
- [pg_dumpall](https://www.postgresql.org/docs/current/app-pg-dumpall.html) — globals/roles/tablespaces, отдельный scope.

**Не выполнялось:** ни один PG dump/restore, тест, сборка, проверка RLS/установленных утилит; флаги здесь proposal до проверки конкретной версии утилиты и тестовой среды.
