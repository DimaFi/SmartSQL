# M05–M07: проверяемая копия, доставка и восстановление — техническое предложение

> **Интеграционная пометка 17.10.2026:** reference/proposal, не новая принятая архитектура и не реализованные функции. Использование, перепроверка источников и ограничения — [реестр интеграции](BRANCH_INTEGRATION_2026-10-17.md). WORK_PLAN / DECISIONS остаются источниками текущего порядка и решений.

> **Research/design only.** Это не утверждённый DEC-FORMAT или DEC-KEYS. Единственная очередь — [WORK_PLAN](../WORK_PLAN.md). Ни один API/test/restore не запускался.

## 1. Четыре независимых факта

- **F1 DB_COMMITTED:** транзакция подтверждена СУБД. Не равно резервной копии.
- **F2 LOCAL_VERIFIED:** SQLite Online Backup создал отдельный закрытый immutable файл, он независимо открылся, прошёл проверку целостности, схемы/версии, raw значений; вычислен SHA-256, завершён durable catalog.
- **F3 REMOTE_VERIFIED:** конкретный аккаунт/назначение/object содержит точные байты нужной копии, remote SHA-256 совпал или скачивание подтвердило SHA-256. Не доказывает возможность восстановления/наличие ключа.
- **F4 RESTORE_VALIDATED:** скачивание, bounded manifest, hash, будущий decrypt, integrity/schema/open на новом target завершены. Подмена активной БД — отдельное подтверждение с защитной копией.

## 2. Proposed catalog contract, не wire standard

Идентичность записи: snapshotId (UUID), sourceKind (managed-sqlite или native-sqlite; PostgreSQL отдельно), opaque sourceIdentity, created UTC, scope=complete-database, payload filename, byteLength, SHA-256, formatVersion.

Queue дополнительно требует destinationProvider, immutable accountId, remoteObjectId и durable current state. Имя или modifiedTime НЕ идентифицирует копию. SourceIdentity не содержит полного local path/URL/имени клиента. В переносимый payload не включать OAuth/DB credentials, SQL history, user profile или cache.

До реализации нужно **отдельно принять** DEC-FORMAT: parse/size caps, serialization/versioning, algorithm identifiers, atomic persistence/journal replay, ownership of orphan artifacts, format compatibility, what happens on CRC/hash failure. Выдуманная длина/хеш в образце не являются валидной копией.

## 3. Durable state machine

CREATING_LOCAL → VERIFYING_LOCAL → LOCAL_VERIFIED → QUEUED → UPLOADING → REMOTE_UNKNOWN or REMOTE_VERIFIED → RESTORE_DOWNLOADING → RESTORE_VALIDATING → RESTORED_NEW_TARGET.

Любая ошибка в первых двух шагах означает NO LOCAL_VERIFIED. Сбой доставки не отменяет локальную целостную копию. После missing upload response сначала точный provider/account/session/object reconciliation; нельзя слепо делать overwrite/новый объект с тем же названием. Статус REMOTE_VERIFIED не означает RESTORED. Google success не говорит ничего про Яндекс.

| Точка аварии | Что допустимо после restart |
| --- | --- |
| Online Backup temp или вычисление SHA не завершены | Предыдущая valid копия остаётся, новый artifact not verified |
| Payload опубликован, каталог ещё нет | Найти собственный orphan, проверить байты, но не скрывать отсутствие каталога |
| Local verified, очередь ещё не persist | Состояние local verified, возможна постановка того же snapshot ID |
| Upload принят, ответ потерян | Remote unknown, попытаться найти тот же object и SHA, не blind retry |
| Auth revoked/quota/connection dropped | Local snapshot цел; явное AUTH_REQUIRED/QUOTA/OFFLINE и ограниченные попытки |
| Remote SHA mismatch или подмена манифеста | Remote untrusted; last known good не удалять |
| Download interrupted / full disk / wrong key | Restore failed, активная DB нетронута |
| Переустановка без local catalog | После повторного login искать app-owned versions по разрешённому scope и проверять bytes |
| Два устройства с одинаковым display name | Два отдельных snapshot UUID, нет merge/last-writer-wins |

Секретоподобные upload-session URLs не писать в telemetry/PR/Git/переносимый manifest. Не удалять последний verified snapshot при retention. Unfinished backups из очереди не удалять как обычный cache.

## 4. Provider specifics и границы доказанного

| | Google | Яндекс |
| --- | --- | --- |
| OAuth | Registered Desktop client, system browser + code PKCE, app-created visible folder, drive.file scope | OAuth code/PKCE документированы; native refresh без embedded client secret требует отдельного production-safe test |
| Upload | Resumable status inquiry; 308 означает incomplete, 404 может означать expired session | resources/upload предоставляет upload URL, далее PUT; overwrite=false; 202/pending нельзя считать завершением |
| Verify | SHA-256 metadata если доступна, иначе точное скачивание и local SHA-256 | Resource size/hash если реально доступны для данного scope; иначе exact download SHA |
| App data | Hidden appDataFolder не единственная backup location: пользователь может потерять при удалении app data | app folder scope cloud_api:disk.app_folder — кандидат, проверить операции app:/ under restricted token |
| Reinstall | Правильный аккаунт, recovery list app-created objects без прежнего каталога | Проверить тот же сценарий на test account и право после повторной авторизации |

Официальные ссылки на 17.10.2026: [Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Drive upload/reconciliation](https://developers.google.com/workspace/drive/api/guides/manage-uploads), [Drive files resource/hash](https://developers.google.com/workspace/drive/api/reference/rest/v3/files), [Yandex Disk REST](https://yandex.com/dev/disk-api/doc/en/concepts/quickstart), [Yandex upload](https://yandex.com/dev/disk-api/doc/en/reference/upload). Углублённый readiness — [OAuth review](OAUTH_PROVIDER_READINESS_2026-10-17.md).

## 5. M05 → M06 → M07 без перепутанных зависимостей

**M05:** SQLite Online Backup из живого WAL, bounded temp, independent integrity + SHA, immutable file + durable manifest/catalog, restore into new target и disk/restart/fault tests. Native SQLite не получает managed migrations; PostgreSQL single DB dump — отдельный M08 scope. XLSX/CSV export не backup.

**M06:** отдельные Google/Yandex адаптеры, токены в OS secret store, proper account identity, scope, upload/resume/readback verification, revoke/quota/reinstall tests; опираться на M05. Не регистрировать OAuth продукт через личные токены Codex; требуется добровольное test account владельца.

**M07:** автоматические точки только **пока SmartSQL открыт** и ограниченное количество snapshot/upload задач, offline durable outbox и catch-up после старта. Background service при закрытом приложении — будущий отдельный opt-in. Не копировать live SQLite в sync folder.

**DEC-KEYS:** локальный secret store для OAuth не заменяет переносимый восстановительный ключ. Потребуется vetted authenticated encryption/library+license review, тест чистого ПК без старого DPAPI/profile, authenticated metadata. Потеря единственного ключа означает невозможность restore. Исследование не блокирует M05 local snapshot.

## 6. Проверяемые будущие сценарии B01–B20

| ID | Что проверяется |
| --- | --- |
| B01 | Live WAL и конкурентный writer, autonomous consistent DB |
| B02 | Cancellation в SQLite Backup API, no partial visible backup |
| B03 | Typed BLOB/UTF-16/Int64/NULL и exact raw preservation |
| B04 | Future/foreign managed content fails before false verified |
| B05 | Kill before manifest/publish, no false local success |
| B06 | Orphan payload/corrupt catalog after restart |
| B07 | Two snapshots same friendly filename, unique ID/no overwrite |
| B08 | Destination disk full/read-only, existing copy untouched |
| B09 | Lost Google final response and exact reconciliation |
| B10 | Google 308 offset/restart and expired session |
| B11 | Yandex pending/202 and expiration, no premature delivered |
| B12 | Wrong account/A->B, no mixed catalogs |
| B13 | Revoked/expired auth; state and local copy retained |
| B14 | Quota/429/5xx bounded retries, no unbounded growth |
| B15 | Remote SHA unavailable/mismatch; full download verification |
| B16 | Clean PC restore after reinstall without old catalog |
| B17 | App-open scheduling and throttling, truthful next run |
| B18 | App closed = no claim of scheduled backup |
| B19 | Offline backlog survives crash and catches up |
| B20 | Independent Google/Yandex statuses, retention protects last good |

Все сценарии **не выполнены**. Исторические 676 тестов M01.02 не являются evidence для M05–M07.

Интеграционное уточнение: Yandex upload reference в этом документе не открылся при повторной web-проверке. Сценарий ресурсов/upload и PUT/overwrite остаётся design assumption для отдельного официального source/live protocol verification; готовность транспорта не заявляется. Детали — OAUTH_PROVIDER_READINESS и BRANCH_INTEGRATION.
