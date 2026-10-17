# Codex: расширенное исполняемое задание после GPT-6 review (17.10.2026)

> **Интеграционная пометка 17.10.2026:** исходная передача сохранена как исторический справочный материал. Текущая проверка: 754 tests, helper-основа M01.04 не подключена к runtime; M01.04 открыт. Прежние SHA и «tests NOT RUN» ниже относятся к моменту подготовки ветки. Этот prompt не запускает автоматически новые waves и не заменяет запрос пользователя, HANDOFF/WORK_PLAN. [Реестр всех файлов и фактических проверок](audits/BRANCH_INTEGRATION_2026-10-17.md).

> **Обновление:** для следующего запуска Codex используйте [MASTER EXECUTION PROMPT V2](CODEX_EXECUTION_PROMPT_V2_2026-10-17.md); этот файл сохранён как историческая v1-инструкция и не является отдельной очередью.



> **Текст для передачи в Codex целиком.** Не является новым WORK_PLAN и не подтверждает выполненный код. Требование владельца — «делай дальше много заданий, учитывая ТЗ и уже сделанное», поэтому **работай по порядку зависимостей, не закрывай неподтверждённые этапы**. Если реальных условий (тестовый сервер/время/ручной экран) недостаточно, оставь конкретную карточку незавершённых пунктов; не делай рискованный объединённый коммит.

## Контекст, который обязательно прочитать перед изменением кода

Репозиторий: https://github.com/DimaFi/SmartSQL. Исторический main на подготовку этого пакета — **7e6aa2c71ec725d9ca76976f0268b823b2394ffc**. Draft PR **#27** содержит исследовательские материалы; **основная ветка не изменена**. Сначала получи актуальный main и посмотри git status/log/ветки/незавершённые PR, чтобы не затереть чужую работу.

Обязательные документы main: AGENTS.md, docs/HANDOFF.md, docs/TASKS.md, docs/WORK_PLAN.md (единственная каноническая очередь 18 этапов/123 задачи), docs/PRODUCT_STORAGE.md (D056), docs/MANAGED_PROJECT_OPENING.md, docs/CAPABILITY_MATRIX.md, docs/WORKSPACE_UI_ACCEPTANCE.md. Из ветки PR #27: docs/CODEX_HANDOFF_2026-10-17.md и все 6 исследовательских материалов под docs/audits (перечень ниже).

Установленный стек: C# / .NET 10 / Avalonia 12, Domain → Application → Infrastructure, Desktop composition. SQLite встроен; managed local project отличен от ordinary native SQLite; PostgreSQL сервер внешний. Никакой новой СУБД, unrestricted AI SQL, переписывания движка или скрытой схемной миграции. Mode/Theme не дают права на запись.

Последний **исторически подтверждённый** результат M01.02: 676 tests, включая 60 PG facts, 0 skipped/errors/warnings; это **не** проверка новых задач. M01.03 (D056) пока policy только на бумаге. M01.04/M01.05/M01.06 в коде **не завершены**. Облачные API/OAuth/scheduler/encryption не реализованы, installer/UI gates открыты.

**Environment safety:** ВСЕ проектные файлы, user-like tests, caches, build/test/temp, synthetic DB/ML/preview на D:. Главный checkout D:\Code\SmartSQL; ML D:\Code\SmartSQL-model. Используй scripts/Initialize-Workspace.ps1, Build.ps1, Test.ps1. Не меняй глобальные настройки ОС, не запускай product-default запись в реальный Windows профиль разработчика. Для clean PC no-D: acceptance — только явно разрешённая изолированная VM. Исходники, данные и секреты не выкладывать в браузер/ML. Не устанавливать новые инструменты без необходимости и согласования.

## Пакет подготовленных исследований — что именно уже сделано

1. docs/audits/BROWSER_GPT6_FINDINGS_2026-10-17.md — 10 проблемных сценариев D056, 3 решения, Google/Yandex краткий brief, onboarding и crypto.
2. docs/audits/M01_ROOT_RESOLVER_CONTRACT_REVIEW.md — 10 инвариантов, 15 негативных/позитивных сценариев, crash/concurrency/reparse matrix.
3. docs/audits/M05_M07_BACKUP_RECOVERY_PROTOCOL_PROPOSAL.md — F1 DB_COMMITTED / F2 LOCAL_VERIFIED / F3 REMOTE_VERIFIED / F4 RESTORE_VALIDATED; durable outbox, 20 future tests.
4. docs/audits/OAUTH_PROVIDER_READINESS_2026-10-17.md — официальная OAuth проверка Google/Яндекс; важный **непроверенный** native refresh Яндекса без вшитого client_secret.
5. docs/audits/M03_M04_FIRST_USE_ACCEPTANCE_PROPOSAL.md — три маршрута welcome, Excel data-loss matrix, 15 будущих UX checks.
6. docs/audits/DEC_KEYS_SECURITY_RECOVERY_REVIEW.md — угроза потери ПК/ключа, 3 кандидата, формат/authentication/12 checks.

Это **исследования, а не принятые новые ADR**. Ссылки актуальны по состоянию на 17 октября 2026; перед реальной реализацией перечитай живую официальную документацию. Указанные в файлах будущие acceptance не являются пройденными тестами.

## Волна 0: подготовка, scope и запрет опасной параллельности

- Проверь main/PR #27/diff/issues; не смешивай этот docs Draft PR с code PR. Создай **отдельную рабочую кодовую ветку от актуального main** и ссылайся в описании на материалы PR #27.
- Прочти D056, AGENTS/HANDOFF/WORK_PLAN; выпиши точные files ownership. Независимые аналитические/review задачи можно распараллеливать, но root resolver, startup и launcher имеют общего интегратора, чтобы не получить два «источника истины».
- Отдельно опиши reversible setup для тестовых баз на D:, как подтверждаешь отсутствие посторонних файлов/секретов и отсутствие активных пользовательских процессов. Для PostgreSQL используй только **собственный test fixture** (loopback 15432) по правилам HANDOFF, не трогай service пользователя.
- Перед кодом реши в рамках D056 три вопроса: crash protocol bootstrap/rootId, interprocess first-use coordination, fail-closed legacy discovery/UX. Если нужен новый обязательный механизм сверх D056 — сначала предложи ADR, не меняй молча.

## Волна 1 (P0): РЕАЛИЗОВАТЬ M01.04 единый root resolver и ранние ошибки

**Результат:** один process-stable data root для прямого EXE, ярлыка, обычного launcher, любой версии; ясный отказ без пустой подмены.

1. Root selection (pure deterministic): strict precedence ENV_PRESENT > portable marker > installed bootstrap > bounded known legacy > genuine first-use. Env присутствует, но пуст/relative => ошибка. Portable relative только marker-relative. Installed root LocalApplicationData/SmartSQL/data или explicit persisted root, bootstrap/marker schemaVersion:1 and matching rootId. Нет второго default.
2. Safe IO stage: explicit no-overwrite root marker + bootstrap, initializers race coordination, crash recovery/re-read, unavailable volume/identity mismatch/corrupt/future JSON/reparse/UNC restrictions. Не затирай чужое содержимое и не принимай orphan автоматически. Resolve once process-wide, а не на каждый WorkspacePaths.DataDirectory property.
3. Program/App startup: resolver **до** theme/mode/profiles/default DB; минимальный ранний русский error UI (путь/причина, retry/choose existing/cancel). Ошибка до Avalonia не должна завершиться непонятным silent exception. Native caption/buttons/Gray/Light сохранить.
4. Refactor Run-Published.ps1: без explicit -DataDirectory использовать resolver приложения; dev launcher может иметь явный D:-override, но продуктовый обычный launcher не подменяет путь своим run-data/local. Не менять global env.
5. Добавь tests в SmartSQL.Desktop.Tests: cases P01–P12 из CODEX_HANDOFF и T01–T15 из M01_ROOT_RESOLVER_CONTRACT_REVIEW, включая **реальные два процесса** и injected crash/failure. Tests только synthetic data на D:, а no-D: и protected-profile acceptance — isolated VM/manual, не пользовательский профиль.

**Граница:** M01.04 НЕ переносит legacy файлы, не копирует live WAL, не меняет SQLite v3, не создаёт новую SQLite из неизвестной, не добавляет cloud/AI/пакеты «на будущее». Любое сложное symlink race/TOCTOU за documented conservative bounds перенеси в M01.06 с явным остатком.

**Доказательства:** Debug build, full tests, no skips, exact source commit, diff inspection, hashes existing synthetic legacy files, release EXE entrypoint parity и ручной UI status отдельно. Если экран не работает, писать «UI не проверен».

## Волна 2 (после В1): M01.05 copy-first migration и M01.06 hardening

**M01.05:** разработать confirmed source→destination migration только после отдельного подтверждения конкретного пути пользователем. Multiple legacy candidates всегда требуют явного выбора. Для live SQLite использовать согласованный backup API/verified standalone copy включая committed WAL, checksum/schema/version/raw typed data, сохранность источника, no-overwrite/new root commit после проверки. Preferences/history/prewrite backups переносить allowlist с отдельной проверкой, без секретов и без изменения внешних проектов. Kill/cancel/no-space/collision не переключает bootstrap на incomplete root. Не делать рекурсивный поиск чужих дисков и auto-merge.

**M01.06:** раздельные tests Unicode/long paths, junction/reparse and parent replacement, file-vs-dir/SQLite sidecars, missing/read-only volume, partial write/flush, two Desktop processes, downgrade/uninstall/upgrade and permission failures. Любую невозможную race guarantee отразить как limitation; не объявлять Path.GetFullPath полноценной sandbox проверкой.

**Gate:** проверенное восстановление legacy data, сохранённый original SHA, confirm-before-switch, повторный запуск, direct EXE/launcher parity, доступная русская ошибка. M01 не закрывается пока M01.04–.06 и UI acceptance не пройдены.

## Волна 3 (параллельно после независимых prerequisites): M02 regression и M03/M04 groundwork

**M02:** выполнить существующий acceptance WORKSPACE_UI_ACCEPTANCE на synthetic D:-данных: PG single-cell/insert/delete conflicts, native SQLite prewrite backups, T09 filters/sort, T11 SQL tab cancellation/lifecycle/history/IME, T12 inspector exact/partial/unknown, theme/mode and distinct same-name connections. Не расширять записи, права или SQL parser ради быстрого зелёного теста. Screen unavailable ⇒ gate remains open.

**M03 welcome:** три коротких пути: существующий SQLite/PG (native без конверсии), embedded local SQLite без сервера, Excel/CSV newcomer. Не делать весь редизайн или скрыто создавать default DB. Direct restore показывать enabled только после M05. Добавь честное «Где мои данные», recent paths без паролей, ошибки missing/corrupt/unknown и helper по официальному PostgreSQL. Используй UX01–UX15 из research.

**M04 import:** единый мастер XLSX/CSV→новый проект только после принятой semantic decision для NULL/empty/whitespace; preview показывает raw→saved, multiple sheets/headers, leading zeros, large numbers, formulas/date-time/locale, abort without partial commit и source SHA unchanged. Нельзя менять Domain FieldValue/v3 без compatibility/ADR/test. Не прятать неподдержанные преобразования.

Можешь предварительно реализовать **только безопасные независимые M03 UI части** после стабильного M01, но не заявлять целый этап completed из-за welcome mock.

## Волна 4: M05 local snapshots/restore, M06 protocol research, M07 automation — БЕЗ прыжков

**M05 (код после M01):** provider-neutral application contracts (маленькие), Infrastructure SQLite Online Backup с WAL, immutable payload + bounded manifest + SHA/integrity/catalog + durable outbox + local restore в новый target. B01–B08, crash/restart/test injection. Никакой upload живой SQLite. PostgreSQL dump scope отдельно M08.

**M06 research/spike:** Google Desktop OAuth PKCE drive.file visible app-owned files and resumable verified upload. Яндекс отдельно сначала доказать **native refresh без общего вшитого секрета**, app:/ scoped upload/list/download/delete + clean-PC recovery. Не подменять реальную OAuth регистрацию ChatGPT Google Drive connector. Без реальных учётных данных и test-account consent не заявлять integration pass. Учитывать wrong account/revoked token/lost final upload response/quota/checksum and download integrity.
Если Yandex secretless refresh не поддержан/не подтверждён, оставить blocker EXT-OAUTH, не делать небезопасный workaround; Google можно вести отдельно после общей M05 основы.

**M07:** app-open scheduler only, bounded attempts/throttling/offline outbox/catch-up, отдельные статусы Google/Яндекс, no false «восстановлено» after uploading. Background when app closed — отдельная опция/поздний срез.

**DEC-KEYS:** threat model already prepared, но security library/format/recovery key решение ещё **не принято**. Сравни .NET AES-GCM framing vs vetted libsodium secretstream vs maintained container with license/audit; не писать свою криптографию. Clean-PC restore without prior OS key container, wrong key/tamper/rollback warnings. Не давать ложных encryption guarantees и не отправлять sensitive plaintext без принятой политики.

## Неизменные quality/release ограничения

- Tests/build/test artifacts только D:. Менять только нужные project files, не обновлять packages ради эстетики, не ломать .NET 10/Avalonia 12/cross-provider architecture.
- Для каждого среза: что менялось, зачем, точное число новых/старых тестов, passed/skipped/failed, PG facts, warnings/errors, отдельный Git SHA, результаты независимого review, UI/manual gates, реальный EXE/installer status. Не переносить историческую метрику 676 в новый код.
- Не менять main/не merge до проверок и подтверждения. Код в отдельном PR. Секреты, реальные БД, histories, ML corpus не публиковать. Обновить canonical WORK_PLAN checked boxes только после фактических evidence; HANDOFF, CAPABILITY_MATRIX, WORKSPACE_UI_ACCEPTANCE, issues актуализировать по факту, не подменять «написан план» статусом implemented.
- Временные preferences по субагентам из AGENTS rule20 действуют только до указанного expiresAt; после автоматически возвращается нормальный режим. Никаких новых AGENTS rules ради этого пакета.
- Если одновременно идут изменения другого агента: остановить конфликтные записи, сверить HEAD и выбрать disjoint files. Не делать force push/reset чужой работы.

## Итоговый отчёт владельцу (обязательный формат)

1. **Completed/verified** — точные Mxx.yy IDs, изменённые source/test files, commit SHA/PR URL, командa и реальный результат tests.
2. **Docs/research only** — конкретные specs или решения для будущего, помеченные not implemented.
3. **Not performed/blockers** — UI, clean-PC no-D:, installer, OAuth test accounts, key security review, full restore и т. п.
4. **Next executable slice** — один ближайший блок, зависимость, критерии и файлы; не создавать вторую очередь вместо WORK_PLAN.
5. **Data safety** — какие файлы/корни реально были созданы/затронуты, подтверждение отсутствия пользовательских файлов/секретов и сохранения оригиналов.

**Начни с Волны 0 и M01.04.** Продолжай независимые маленькие проверяемые задачи, когда условия позволяют, но не заменяй полные тесты и защиту пользовательских данных скоростью выполнения.
