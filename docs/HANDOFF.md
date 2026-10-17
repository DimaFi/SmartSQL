# Продолжение SmartSQL

## Прочитать перед работой

`AGENTS.md` → [WORK_PLAN](WORK_PLAN.md) → [TASKS](TASKS.md) → [CAPABILITY_MATRIX](CAPABILITY_MATRIX.md) и относящиеся [DECISIONS](DECISIONS.md), затем Git status/history. WORK_PLAN — единственный порядок: **M01–M18, 123 подзадачи**, T01–T25 — постоянные GitHub identities. Старые Stage/N/A — история/группы, не конкурентная очередь.

## Следующая конкретная работа

Пользователь начал личную проверку preview.9 и попросил **пока зафиксировать** [шесть замечаний UF09-01–UF09-06](USER_FEEDBACK_PREVIEW9.md). Не переходить автоматически к миграции или исправлениям по этому сообщению. Дерево подключений/сценарии БД, перестановка полей, перекрытие прокрутки, Boolean-ввод, undo/история открыты; скриншоты не закрывают storage/restart acceptance.

**Следующее действие — пользовательская приёмка preview.9 / M01.04** по USER_TEST_M0104.md. Код runtime подключён, full Debug 771 tests/60 PG/0 skips/warnings/errors. После пользовательской обратной связи следующий code slice **M01.05 copy-first migration**. M01.06/ручная UI/no-D VM открыты. До результатов не менять storage дальше автоматически.

M01.03 отдельно определяет product storage вне versioned binaries, EXE/launcher и ПК без D:; правило D: нашей разработки не менять. После M01 — M03/M04 first-use/Excel и M05 snapshot/restore; M06 Google+Яндекс, M07 автоматика/статус. Native export M09 можно вести параллельно после его read/format contracts; T13 не потерян. M02/manual debt идёт при доступном screen tool, M10/T23 — после каждой UI-функции и отдельным полным проходом.

## Завершённая консолидация плана — 16 октября

Полный M01–M18/123 план проверен тремя субагентами по независимым направлениям; замечания по managed regression, Simple/Advanced/shell contract и порядку English приняты и повторно проверены. Устаревшие competing queues и статусы в индексах/архитектурных/UI документах согласованы. Проверены уникальность/последовательность ID, локальные ссылки, 26 issue mappings и diff whitespace. Код приложения не менялся, новый build/tests/EXE не выполнялся.

Коммит плана `1ccc9075aa07eb25a88963ec50a5da86cba17d01` опубликован, remote main SHA подтверждён на момент публикации. Все 26 существующих PLAN/T01–T25 issue bodies обновлены и read-back сверены; состояния сохранены (T05 closed, остальные open). Receipt в github-issues.json, текущий HEAD читать из Git. Следующий шаг остаётся M01.01; опубликованный план не закрывает ручную приёмку и будущие функции.

Последующее уточнение пользователя: SmartSQL снижает порог входа и автоматизирует работу с готовыми СУБД; собственный DBMS engine/server не делаем. D052/PROJECT_GOALS/ARCHITECTURE закрепляют границы движка, адаптеров, UI и будущих cloud/AI. Очередь M01.01 и 18/123 не изменены; приложение и результаты code/UI проверок не менялись.

## Проверенная основа

- Текущий кодовый срез: M01.01, его commit/HEAD определять по Git. Историческая T12 основа `28a4bcbbaa97e7ac0eab6c4239aca51078a77ff9`; старые EXE receipts относятся к своему source SHA.
- Последний full Debug M01.01: **638 passed / 0 skipped / 0 warnings/errors**, Domain 1 / Application 129 / Infrastructure 469 / Desktop 39; **60 реальных PG-фактов**. 24 новых opening cases; TRX `*2026-10-16_23_36_49*.trx` и `*2026-10-16_23_36_50*.trx`. [Контракт/границы](MANAGED_PROJECT_OPENING.md).
- Managed SQLite CRUD/четыре типа/структура/фильтры/paste/CSV/JSON/XLSX/backup/restore; ordinary SQLite browse/typed query/opt-in single-cell UPDATE с обязательной копией; PostgreSQL native read/guarded UPDATE/INSERT/DELETE; shared tree/tabs/mode; read-only SQL/highlighting/history/files; первый object-owned structure inspector.
- Gray/Light и закреплённая theme button, явный Simple/Advanced/память, native caption buttons; user S-branding в EXE/окнах/taskbar частично принят.
- Ограничения: native SQLite INSERT/DELETE/general undo нет; все PG incoming FK блокируют DELETE; SQL-write нет, SQLite SQL UTF-8/часть columnless CTE ограничены; T12 incoming/domain/view dependencies не включает. Успешный GetStructureAsync сохраняет current handles, остальные guards не ослаблены.
- Полная ручная T04/T11/T12/T23 открыта. Screen tool не инициализировался (kernel assets/path/os error 3); это не доказанный app defect. Предыдущая partial UI evidence в WORKSPACE_UI_ACCEPTANCE, новый проход не заявлять.

Субагент частично написал тесты; все три назначенных агента достигли usage limit. Parent реализовал provider/callers, дополнил тесты, провёл build/full tests и source review. Полное независимое финальное ревью не выполнено. Повторная computer-use инициализация снова отказала, новая ручная приёмка не заявляется.

## Поставка и среда

Историческая T12 clean self-contained Windows x64: `D:\Code\SmartSQL-builds\0.7.0-preview.6\win-x64-self-contained-verified-t12-28a4bcb\app\SmartSQL.Desktop.exe`; sourceDirty=false, 237 payload-файлов. ZIP SHA256 `236a17e2c089f8c0f2664f3058cfdfc56a6cb1735b569fe79c2fb81aab1578f3`. Новый Release UI/installer не принят. ISCC отсутствовал при последней проверке; installer не собирался. Старые build/runtime data не перезаписывать.

Всё создаваемое для разработки/тестов/публикации на D:. Только scripts/Build.ps1, Test.ps1, Run.ps1 и delivery helpers. ML целиком отдельно `D:\Code\SmartSQL-model`; 9 прошлых stub-тестов не реальная модель/интеграция, remote ML проверить до claims.

Owned PG fixture `.workspace/postgresql-tests` (17), 127.0.0.1:15432, smartsql_browser_test, была штатно остановлена после T12. Перед новым provider прогоном проверить точный root/status, запустить только её, задать SMARTSQL_POSTGRES_TEST_PORT=15432; не reset/initdb/не трогать installed service. После прогонов parent штатно останавливает её. Credentials/data/TRX/.workspace игнорируются Git; auth values не выводить.

## Подтверждённые новые требования

Аудит [USER_ENTRY_AND_CLOUD_AUDIT](USER_ENTRY_AND_CLOUD_AUDIT.md) и три subreports сведены в M01–M08. Пользователь выбрал **Google Диск и Яндекс Диск**, приоритет — автоматические копии и восстановление; дешёвая команда через общий PostgreSQL, snapshots по очереди отдельно. Никакой cloud delivery/автоматики/OAuth регистрации/нового Excel wizard пока нет.

Нерешённые входы — раздел 7 WORK_PLAN: product paths, empty/NULL/format compatibility, encryption/keys/secret store, retention/outbox, OAuth/test accounts, PG backup scope/utilities, closed-app background, ISCC/signing/clean machine/screen tool, расширенные writes и ML resources. Согласованные приоритеты не спрашивать заново; блокер одного входа не останавливает независимую работу.

## Непрерывные материалы для будущей модели

По новому запросу пользователя: D:\Code\SmartSQL-model\knowledge — 14 архитектурных facts, 5 provider profiles, 18 synthetic training candidates и 10 отдельных holdout cases. README описывает ограничения/обновление; model-local Refresh-KnowledgeSources/Validate-Knowledge связывают public source snapshots с clean verified product commit и hashes. Материалы не являются runtime permissions, обучение/модель/server/weights/Desktop integration не запускались. Исходники/скрипты/данные корпуса только в ML workspace, нового model remote нет. После каждого релевантного проверенного среза обновлять facts/examples/sources и проверять разделение train/eval; raw source plans не выдавать за готовый scope. D054, AGENTS rule22.

## Работа с субагентами и завершение

Протокол — раздел 8 WORK_PLAN: exact Mxx.yy/T-ID, fixed contract, точные владельцы файлов, независимое review. Предпочтения gpt-5.6-sol high или gpt-6.1-sol medium, без Astra/extra-high. Текущие 4 слота включают parent; builds/tests/fixture/Git/docs integration/publish координирует parent.

После каждого среза обновить actual status/evidence, матрицу/UI checklist/HANDOFF и existing issues. Не повторять весь план здесь. Commit/push без данных/секретов; remote SHA и body read-back verify обязательны до claims. Закрытый code slice с открытой UI-приёмкой явно так и называется. Если срез прерван, оставить карточку Mxx.yy с реально изменёнными файлами/проверками/следующим действием.

M01.01 preview.7: чистая self-contained Windows x64 поставка `0.7.0-preview.7/win-x64-self-contained-verified-m0101-74fe89e` из опубликованного `74fe89e7cb1d71234926d870443ca561d9f7ccbd` (sourceDirty=false). Все 237 payload SHA/размеров и состав ZIP проверены: 78 756 941 байт, SHA256 `f30ed3a4c01e44080b55c189b109eb386700077ad6960f831ad80958c2c65de4`. Runtime-данных/модели нет. Debug 638 tests (1/129/469/39), 60 real PG facts, 0 warnings/errors/skips; owned PG stopped. Новый EXE на экране не запускался: computer-use initialization по-прежнему kernel assets/path/os error3. Installer не собран, M01.01/T04/T11/T12/T23 ручная приёмка открыта. Следующий кодовый срез M01.02.

Корпус модели локально закоммичен в 30a7d3635cfa809792b9175a7bce8f431dc74f99 (remote не настроен/публикация модели не заявляется). Проверены 18 source hashes на продуктовый code commit 74fe89e7cb1d71234926d870443ca561d9f7ccbd и идентичность всех 18 Git blobs captured bytes; 14 facts, 5 profiles, 18 train candidates/10 holdout. Model-local Test-Knowledge положительный/отрицательные tamper/leakage checks прошли; Build 0 warnings/errors и 9 stub tests прошли, это не качество реальной модели. Следующее обновление корпуса после релевантного проверенного code slice.

## Историческая остановка 16 октября (отменена возобновлением 17 октября)

16 октября пользователь попросил остановиться и продолжить завтра. Эта остановка отменена новым запросом «начинай» 17 октября. Следующая реализация M01.02; M01.01 код/638 тестов готов, ручная UI-приёмка и полное независимое review открыты. Собственный PG остановлен, фоновых model/training процессов нет. Preview.7 проверена, ML knowledge локально сохранён/проверен (30a7d36), шесть GitHub issue bodies опубликованы/read-back verified. Продолжать после нового запроса пользователя; автоматический запуск на завтра не назначен.

## Возобновление 17 октября и временная экономия

Пользователь возобновил работу над M01.02. Временная экономия действует только до 2026-10-18T04:36:50Z / 18 октября 07:36:50 Europe/Istanbul; до срока использовать gpt-6-luna и gpt-5.6-sol medium с короткими disjoint briefs, после срока вернуть обычные предпочтения AGENTS rule20. Истечение автоматическое по timestamp; одноразовая thread automation smartsql напоминает об этом около 07:37. Она не разрешает сама возобновлять остановленную работу. Не менять глобальные настройки.

## Проверенный срез M01.02 — 17 октября

M01.02: Debug 0 warnings/errors; 676 passed / 0 skipped (Domain 1 / Application 144 / Infrastructure 492 / Desktop 39), включая 60 real PostgreSQL facts. TRX *2026-10-17_11_55_01*.trx и *2026-10-17_11_55_02*.trx; owned PostgreSQL 17 штатно остановлен. 23 новых infrastructure cases и 15 application cases. Независимый read-only review Luna: блокирующих замечаний нет. Экранная инициализация снова отказала (kernel assets/path/os error 3); новая UI-приёмка не заявляется.

Проверка идёт до изменения и повторяется под writer transaction. Legacy field-FK/unique omission и gaps позиции сохраняются; malformed metadata/physical schema/storage/encoding, extra objects/indexes/constraints отвергаются без преобразования. Backup API копирует также committed WAL; копия read-only валидируется и сравнивается fingerprint, затем публикуется без overwrite. Upgrade сообщает версии/путь/SHA; при неуспешной последующей миграции проверенная защитная копия может остаться. Лимиты и контракт: MANAGED_PROJECT_OPENING. Native права, cloud и AI не расширены.

Следующий срез **M01.03 product storage decision**. M01.01/M01.02 UI остаётся открытым вместе с T04/T11/T12/T23. Не повторять M01.02 вместо следующего среза; дальнейшая path identity/failure matrix M01.06.

M01.02 preview.8: чистая self-contained Windows x64 поставка `D:\Code\SmartSQL-builds\0.7.0-preview.8\win-x64-self-contained-verified-m0102-1ac1480\app\SmartSQL.Desktop.exe` из опубликованного/remote-SHA verified `1ac1480cf08a17d937f6d07b1abf92062b5e682d` (sourceDirty=false). Все 237 payload SHA/размеров, 239 ZIP entries и их байты проверены: ZIP 78 766 276 байт, SHA256 `794acc1f6a0bf64afc01df9b46a80de77b5ff2059ce3d0261b540879ec1ae071`. Runtime/model artifacts отсутствуют. Новый EXE на экране не запускался из-за computer-use initialization failure; installer не собран. Debug 676 tests/60 real PG facts/0 skips/warnings/errors; owned PG stopped. Следующий кодовый срез M01.03, manual acceptance открыт.

ML knowledge M01.02: локальный commit 310e14d223e04dbdbbf8f6b688af2fb5e1cbada4, remote не настроен. 20 source hashes и Git blobs captured bytes подтверждены на clean product commit 1ac1480cf08a17d937f6d07b1abf92062b5e682d; 15 facts/5 provider profiles/20 synthetic train candidates/11 separate holdout. Model-local Test-Knowledge positive/tamper/leakage проверки прошли; реальная модель не запускалась/не обучалась, stub build/tests не повторялись для data-only update.

## M01.03 — принятое решение 17 октября

D056 / [PRODUCT_STORAGE](PRODUCT_STORAGE.md): обычный конечный Windows ПК использует стабильный per-user root, portable явный, env override не меняет installed selection; одинаковый resolver для EXE/ярлыков/launcher. Legacy app/data/local vs run-data/local выявлены чтением кода; миграция только copy-first позже. Dev/tests/preview/ML по-прежнему D:. Это docs-only stage: код/EXE не менялись, build/tests повторно не запускались; последний реальный прогон 676 tests M01.02. Luna narrow read-only review без блокеров; ссылки/123 IDs/статусы проверены. Следующая реализация **M01.04**, M01.05/M01.06 и ручная приёмка открыты.

Пользователь попросил приоритетные задания для GPT-6 в браузере: [BROWSER_GPT6_TASKS](BROWSER_GPT6_TASKS.md), общий контекст и 4 отдельных prompts: storage review, official Google/Яндекс integration brief, onboarding, encryption/recovery. Начать с 1/2; результаты вернуть parent, не менять WORK_PLAN и не считать claims сторонней модели проверенным кодом. Секреты/данные/SQL history туда не отправлять.

## Временная остановка по просьбе пользователя — 17 октября

Историческая остановка: пользователь попросил остановиться и продолжить в чате. Новый запрос 17 октября на аудит/интеграцию веток отменил остановку для этой конкретной работы. Следующий пункт **M01.04**: unified root resolver/startup errors по D056/PRODUCT_STORAGE; M01.03 — только принятое решение, runtime не менялся. Перед продолжением читать этот HANDOFF, TASKS, WORK_PLAN, PRODUCT_STORAGE и Git status/history. Не повторять полный аудит вместо реализации.

M01.03 опубликован в 5a9c3beff39585091495ee678b34eacecfbdeaf5, remote SHA подтверждён. 56 local links, 123 unique M-task IDs, 18 stages, 26 issue markers проверены; Luna read-only review без блокеров, 6 issue bodies опубликованы/read-back verified без изменения state. Новый build/tests/EXE не выполнялся; кодовая база M01.02: 676 tests, preview.8, ручная UI-приёмка открыта.

ML knowledge локально сохранён в ddc57874b174911425cfd52db98eef1f2ddc34f4: 21 public source hashes/Git blobs проверены на clean product 5a9c3be, 16 facts/5 profiles/21 synthetic train candidates/12 separate holdout; Test-Knowledge positive/tamper/leakage passed. A16 помечен planned: policy не выдаётся за готовый runtime. Обучение/inference/weights не запускались, ML remote не настроен.

Для GPT-6 в браузере: BROWSER_GPT6_TASKS.md — общий контекст, необходимые вложения и 4 готовых задания. Приоритет 1 storage/migration review, 2 официальный Google/Яндекс integration brief, 3 onboarding, 4 encryption/key recovery после 2. Ответы вернуть для проверки/интеграции, секреты и реальные данные не передавать.

Временная экономия по-прежнему истекает 2026-10-18T04:36:50Z / 18 октября 07:36:50 Europe/Istanbul. Одноразовое напоминание smartsql только отменяет эту preference; оно не возобновляет остановленную работу.
## Аудит и интеграция входящих веток — 17 октября

По новому запросу проверены все origin heads/open PRs: #27 docs 077b8de (22 commits/15 docs), #28 code cae852f (21 commits/7 files), base 7e6aa2c. [Реестр всех 22 файлов](audits/BRANCH_INTEGRATION_2026-10-17.md), чтение PR comments/reviews/threads: пусты. History сохраняет исходные head/коммиты; сторонние предложения не принимаются автоматически как ADR.

Helper-код M01.04 неактивен в production: Select/read-only preflight/4-КиБ strict UTF-8 manifests; исправлен P2 UNC/device pre-open access, добавлено 12 cases к 66 исходным. Full Build/Test Debug **754 passed/0 skipped/0 warnings/errors (1/144/492/117), 60 real PG facts**; TRX *2026-10-17_22_10_22*.trx. Own PG stopped. Нового UI/EXE/installer нет; native права/DB paths не менялись.

Следующая работа всё ещё **M01.04**, использовать существующую основу: actual FS/parent/legacy checks → atomic interprocess initialization/cached root before consumers → early startup error и EXE/launcher parity. RootId/FirstUseCandidate не разрешают доверять физическому root без этой стадии. ThemePreferences D:-guard требует отдельной dev/product policy integration, не простого снятия защиты. M01.05/.06/manual UI открыты. Исследования M03–M18 брать как reference по таблице реестра; CODEX prompts — исторические guides, не альтернативная очередь или приказ выполнить все waves.

### Подтверждённая публикация интеграции

Merge commit `d45353c55d8e6eaed620c244eeb4715728bcbed6` опубликован в main, remote SHA сверён; оба source head являются его предками. GitHub PR #27/#28 повторно прочитаны: closed / merged=true. `git log --all --not main` после переноса пуст; исходные ветки оставлены. 120 local Markdown links, все 22 inventory entries, 123 M-IDs/18 stages/26 issue markers проверены; новый staged diff whitespace чист, runtime/binary/data artifacts не включены. PLAN/T02/T22/T23/T24/T25 bodies опубликованы и read-back verified, прежние issue states сохранены.

ML knowledge обновлён **только локально** в `009af82e97a6bc55cb5fb86448263c3138d75eb5`: 37 allowlisted public source hashes и Git blobs captured bytes сверены на clean product d45353c; 18 facts/5 profiles/23 synthetic train candidates/13 separate holdout. Audits отдельно помечены research/proposal, helper sources tested-inactive, execution prompts не включены. Model-local Test-Knowledge positive/hash-tamper/train-holdout leakage passed. Обучение/inference/веса не запускались; model remote не настроен. Metadata-only receipt после source commit не меняет проверенную реализацию/754-test evidence. Следующая реализация остаётся M01.04, частичный фундамент не означает её завершение.
## M01.04 runtime / личная приёмка пользователя — 17 октября

[RUNTIME_STORAGE_M0104](RUNTIME_STORAGE_M0104.md) / D058: root до Avalonia/theme/preferences/DB; conservative local/metadata/legacy guards; no-overwrite marker, atomic installed bootstrap, Global named locks с one-root lease; cache до выхода; startup error/retry/process-only existing selection; EXE/launcher один resolver, footer path. Developer paths на D:; product CreateDefault theme использует approved root; обычные dev/test constructors D:-guard сохраняют. Все старые inactive-foundation statements выше — исторические и заменены этим срезом.

Build/Test Debug **771 passed / 0 skipped / 0 warnings/errors (1/144/492/134), 60 real PG facts**. TRX *2026-10-17_22_35_11*.trx. 17 новых cases: 13 FS/lifecycle + 4 реальных child-process kill/concurrency. Own PG stopped; test-only probe не публикуется. Независимое final source review без блокеров после исправлений. Screen tool снова kernel assets/path/os error3; UI/no-D VM/RDP pass не заявлять. В runtime остаётся прежний auto-default managed project до M03; миграция unmarked legacy root M01.05 не сделана, новые данные не создаются вместо ошибки.

Пользователь попросил сам протестировать после следующего этапа. Подготовить clean preview.9 под win-x64-self-contained-verified-m0104-20261017 и explicit portable marker на **D:\Code\SmartSQL-user-tests\m0104** (проверено отсутствует перед публикацией). Источники/старые данные не переносить. [USER_TEST_M0104](USER_TEST_M0104.md): прямой EXE, синтетические Contacts, theme/mode/restart, optional PowerShell7 launcher без global policy изменений, busy-second-instance error, номер шага+скриншот+expected/actual. Передать пользователю ссылку на EXE/инструкцию и ждать результата; не начинать M01.05 автоматически.

## Подтверждённая поставка M01.04 / preview.9 — 17 октября

Код `0a2c1edc982b48d9228142a20740fd9f0209aece` опубликован в main, remote SHA сверён. Self-contained Windows x64: `D:\Code\SmartSQL-builds\0.7.0-preview.9\win-x64-self-contained-verified-m0104-20261017\app\SmartSQL.Desktop.exe`, sourceDirty=false, offline publication. Все **238 payload SHA/размера и 240 ZIP entries** сверены, inventory совпадает; test-only Storage.Probe, базы и runtime/test artifacts отсутствуют. ZIP **78 783 421 байт**, SHA256 `57c9b41e55fcacbb6f415c277747d86e91cfd6d575fcc5c7b5ebe9648f7345d1`, sidecar совпадает. Portable marker включён в проверенный payload и указывает `D:\Code\SmartSQL-user-tests\m0104`; перед передачей root ещё отсутствует, публикация его не создавала. Новый EXE намеренно не запускался до личной приёмки пользователя. Installer не создан; clean-PC/no-D/RDP/UI pass не заявлен.

[USER_TEST_M0104](USER_TEST_M0104.md) — инструкция пользователю. Код/Debug evidence 771 tests не менялись после source commit; последующая metadata-only фиксация квитанции не требует новой публикации. PLAN/T02/T22/T23/T24/T25 issue bodies опубликованы, read-back сверены, прежние states сохранены. ML corpus обновлён только локально: `acf395f5b0fac21fa7d5b97845e7f450d1c07ee0`, 48 allowlisted snapshots на source `0a2c1ed`, 19 facts/5 profiles/24 synthetic train candidates/14 holdout, точные Git blob bytes и positive/tamper/leakage checks проверены. Обучение/inference/веса/ML remote не запускались. **Остановиться на пользовательской приёмке; M01.05 после обратной связи.**
