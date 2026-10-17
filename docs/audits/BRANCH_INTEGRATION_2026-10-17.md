# Проверка и интеграция двух веток — 17 октября 2026

Проверка выполнена по новому запросу пользователя после временной остановки. Scope — весь `DimaFi/SmartSQL`, локальные изменения/stash, все доступные remote heads и открытые PR. Новая разработка всего M01–M18 этим запросом не запускалась.

## Полнота входа

| Вход | Проверенный snapshot | Состав и решение |
| --- | --- | --- |
| `main` до проверки | `7e6aa2c71ec725d9ca76976f0268b823b2394ffc` | Чистый checkout, других локальных веток/stash/незакоммиченных данных не было |
| [PR #27](https://github.com/DimaFi/SmartSQL/pull/27), `docs/gpt6-review-codex-handoff-20261017` | `077b8de11c53f9e23d797c39b52bde6a3f4a86bf` | 22 коммита, 15 Markdown-файлов; принять как исследования/исторические инструкции, не implementation claims |
| [PR #28](https://github.com/DimaFi/SmartSQL/pull/28), `feature/m0104-root-selection-foundation-20261017` | `cae852f462c1ce7504982dc19d0b34235eab57fc` | 21 коммит, 3 source + 3 test + 1 handoff; принять проверенную **не подключённую к runtime** основу M01.04 после исправления |

`git fetch` wildcard по всем heads и `ls-remote --heads` подтвердили только эти две новые ветки и main на момент входа. Общий merge base — тот же `7e6aa2c`. GitHub open-PR search нашёл только #27/#28; отдельные discussion/reviews/review_threads пусты. Не найдено третьего пакета, который можно случайно пропустить. Это проверка конкретного snapshot, не гарантия отсутствия будущих коммитов.

Объединение подготовлено в `codex/integrate-oct17-research-storage`, без reset/clean/squash и без удаления исходных веток. Сохранить в истории оба head через merge ancestry; все **43 исходных коммита / 22 новых файла** учитываются ниже. `main` перемещается только после проверок. Таблица — реестр использования материалов, не новая очередь задач.

## Все 22 файла: назначение и границы

Пути документации в таблице относительны к `docs/`; source/test — к root репозитория.

| Файл | Как использовать |
| --- | --- |
| `CODEX_EXECUTION_PROMPT_2026-10-17.md` | Сохранённая v1, superseded; не отдельная активная инструкция |
| `CODEX_EXECUTION_PROMPT_V2_2026-10-17.md` | Справочная карта зависимостей/передачи; исторические SHA и «tests not run» не текущий статус, не команда автоматически исполнять все waves |
| `CODEX_HANDOFF_2026-10-17.md` | Обоснование будущего M01.04 и ограничения; актуальная точка входа остаётся HANDOFF/WORK_PLAN |
| `M0104_ROOT_SELECTION_CODE_HANDOFF_2026-10-17.md` | Границы helper-кода и следующей FS/startup интеграции; обновлён ссылкой на фактические проверки ниже |
| `audits/BROWSER_GPT6_FINDINGS_2026-10-17.md` | Сводное static review D056/cloud/UX/keys, сохранить reference; предложения не приняты автоматически |
| `audits/M01_ROOT_RESOLVER_CONTRACT_REVIEW.md` | Fail-closed invariants и fault cases для M01.04/.05/.06; не затягивать весь M01.06 в текущий helper slice |
| `audits/M03_M04_FIRST_USE_ACCEPTANCE_PROPOSAL.md` | Три аудитории/Excel preview/UX cases; новый onboarding пока отсутствует |
| `audits/M05_M07_BACKUP_RECOVERY_PROTOCOL_PROPOSAL.md` | Local/remote/restore states, immutable snapshot/outbox/retry; только proposal до DEC-FORMAT и своей реализации |
| `audits/OAUTH_PROVIDER_READINESS_2026-10-17.md` | Проверяемые provider prerequisites; уточнены rotation/client-secret claims и недоступный upload source, live flow не подтверждён |
| `audits/DEC_KEYS_SECURITY_RECOVERY_REVIEW.md` | Threat model/recovery-key alternatives; DEC-KEYS открыт, криптобиблиотека/формат не выбраны |
| `audits/M08_POSTGRES_BACKUP_RESTORE_READINESS.md` | Single-DB dump/restore scope, utilities/roles/untrusted dump/verification; не готовая backup-функция |
| `audits/M09_NATIVE_EXCHANGE_SAFE_SLICES.md` | Fidelity/scope/result export vs full table/import; `Truncated`/RowLimit перепроверены по коду, ordering proposal не меняет WORK_PLAN |
| `audits/M10_M11_UI_RELEASE_ACCEPTANCE_PACK.md` | UI/DPI/Narrator/update/uninstall matrix; не прошедшая UI/installer acceptance |
| `audits/M12_EXACT_CALCULATIONS_SCOPE.md` | Exact bounded calculation/graph scope; CALC IDs — future acceptance, реализации нет |
| `audits/M14_M16_PROVIDER_ADMISSION_MATRIX.md` | Раздельная admission matrix MySQL/MariaDB/SQL Server; новых драйверов/возможностей нет |
| `audits/M17_M18_ACTION_PERMISSION_MODEL_BOUNDARY.md` | Untrusted structured AI proposals и будущие permissions/evals; runtime Action/Permission/модель не появились |
| `src/SmartSQL.Desktop/Storage/StorageRootSelectionPolicy.cs` | Чистый bounded selector, present-empty env отказ, strict JSON/version/rootId, completed legacy-discovery gate; не FS validator |
| `src/SmartSQL.Desktop/Storage/StorageManifestReader.cs` | Read-only 4 КиБ strict UTF-8/no-BOM reader для трёх имён; исправлен UNC/device pre-open guard |
| `src/SmartSQL.Desktop/Storage/StorageRootReadOnlyPreflight.cs` | Precedence + marker pairing; не создаёт root/DB и не доказывает физическую идентичность |
| `tests/SmartSQL.Desktop.Tests/StorageRootSelectionPolicyTests.cs` | Pure precedence/JSON/version/legacy/path cases, впервые реально выполнены здесь |
| `tests/SmartSQL.Desktop.Tests/StorageManifestReaderTests.cs` | Реальные synthetic D:-файлы, byte/UTF-8/missing guards + новые pre-open UNC/device cases |
| `tests/SmartSQL.Desktop.Tests/StorageRootReadOnlyPreflightTests.cs` | Marker pairing/no-fallback/source unchanged + locked file/UNC/malformed identity/32 vs 33 legacy boundary |

## Найдено и исправлено до переноса

Независимое code review (`gpt-5.6-sol medium`) нашло **P2: UNC manifest source открывался до lexical network guard**. `Inspect` мог читать portable или bootstrap из `\\host\share`, поскольку UNC target проверялся позднее в policy, а source reader проверял только absolute/name. Добавлен запрет Windows UNC/device namespace **до FileStream**, без попытки сетевого соединения. Регрессии проверяют portable/bootstrap/direct-reader paths, отказ без IO inner exception и отсутствие fallback. Mapped drives/reparse/real physical identity остаются FS-stage; lexical guard их не «доказывает».

Дополнительно 12 focused cases: UNC/device inputs, эксклюзивно заблокированный portable manifest с неизменными source bytes, повреждённый/oversized installed root marker, legacy bound 32/33. Одно trailing whitespace в selector убрано. Policy precedence и текущие permissions не менялись.

Независимый docs review (`gpt-6-luna medium`) рассмотрел все 12 incoming audit files: блокирующих архитектурных противоречий нет при сохранении статуса reference/proposal. Parent прочитал handoffs/prompts, сопоставил с D056/WORK_PLAN и актуальными `Truncated`, `RowLimit`, `PublishSingleFile=false`, D:-guard ThemePreferences. Исторические инструкции явно помечены; текущий реестр не переносится в них.

## Проверка официальных источников и ограничения

В incoming Markdown — 24 разных внешних URL. 23 страницы доступны через web-tool; страница Yandex upload (`.../reference/upload`, обе локали и `.html` варианты) не открылась. Это не доказательство удаления API, но повторное подтверждение конкретного upload-контракта этим URL **не заявляется**. Ссылка сохранена как provenance, claim понижен до требующего повторной проверки. Реальных OAuth/upload/download/restore calls не было.

- [Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [scopes](https://developers.google.com/workspace/drive/api/guides/api-specific-auth), [appData](https://developers.google.com/workspace/drive/api/guides/appdata), [resumable upload](https://developers.google.com/workspace/drive/api/guides/manage-uploads), [files](https://developers.google.com/workspace/drive/api/reference/rest/v3/files): основные факты соответствуют документации; документальный доступ не заменяет live acceptance/reinstall restore.
- [Yandex PKCE](https://yandex.ru/dev/id/doc/ru/codes/code-url), [refresh](https://yandex.ru/dev/id/doc/ru/tokens/refresh-client), [registration](https://yandex.ru/dev/id/doc/ru/register-client), [Disk scope](https://yandex.com/dev/disk-api/doc/en/concepts/quickstart): наличие refresh_token в ответе **не подтверждает rotation**. Optional ID/secret в теле не означает secretless client: те же credentials могут передаваться Basic header. Безопасный distributed desktop flow остаётся EXT-OAUTH gate.
- [pg_dump 17](https://www.postgresql.org/docs/17/app-pgdump.html), [pg_restore 17](https://www.postgresql.org/docs/17/app-pgrestore.html), [pg_dumpall 17](https://www.postgresql.org/docs/17/app-pg-dumpall.html) сопоставлены с incoming current/18: scope одной DB, отдельные globals и риск кода при restore подтверждены. Утилиты dump/restore не запускались, поддержка production backup не появилась.
- .NET deployment/single-file/AesGcm, Avalonia accessibility, Inno AppId, Windows Known Folders, SQLite Backup, RFC8252 и libsodium secretstream/pwhash открыты по исходным официальным ссылкам из пакета. Это проверка соответствующего design rationale, не принятие новой crypto/dependency и не performance/security certification.

## Реально выполненные проверки

`scripts/Build.ps1 -NoRestore`: Debug **0 warnings/errors**. Исходные 66 новых storage cases прошли до исправления; после исправления выполнена полная `scripts/Test.ps1 -NoBuild -NoRestore` с own PostgreSQL 17 / loopback 15432: **754 passed / 0 skipped** — Domain 1 / Application 144 / Infrastructure 492 / Desktop 117; **60 real PG integration facts**. 78 storage cases = 66 incoming + 12 добавленных при интеграции. TRX `.workspace/test-results/*2026-10-17_22_10_22*.trx`. Own PG штатно остановлен.

Локальные Markdown-ссылки, 123 уникальные M-IDs/18 этапов, 26 стабильных T/PLAN issues, whitespace/provenance и отсутствие посторонних data/runtime files проверяются до commit. Merge history сохраняет оба source head; raw origin files при необходимости доступны через эти SHA. Новую экранную приёмку/EXE/installer не запускали: runtime остаётся прежним, выпуск с новым поведением этим helper-кодом не обещается.

## Использование дальше — без потери материалов

WORK_PLAN остаётся единственным порядком; M01.04 **открыт**. Не переписывать готовые selector/reader/preflight с нуля. Следующий code slice: conservative physical root/parent guards + bounded реальный legacy scanner, explicit current-root handling и bootstrap selection identity; затем crash/interprocess initialization, cached root перед theme/preferences/DB, ранняя ошибка и EXE/launcher parity. M01.05 migration/M01.06 expanded races и существующий manual UI долг не закрыты.

Не менять runtime только ради «активации» helper-классов. `DirectoryNotFound` optional read + `LegacyDiscoveryCompleted=true` — условный кандидат, не разрешение создать root без проверки родителей/тома/данных; installed bootstrap path должен выводиться доверенным caller из product policy. Portable/env existing markers требуют отдельной физической стадии. ThemePreferences D:-guard не снимать без dev isolation/acceptance.

Текущие решения D056/DEC-FORMAT/DEC-KEYS не заменены деталями исследований. M05–M18 читают относящиеся к ним файлы из таблицы при входе в этап. ML knowledge получает только allowlisted public facts/sources с readiness и отдельными synthetic/holdout, без command prompts/секретов/реальных DB/history и без обучения модели.
