# M01.04 — первая кодовая поставка: side-effect-free root selection

**Статус 17.10.2026:** `code proposed, not integrated; tests written, NOT RUN`. Это отдельный **Draft code PR** от main `7e6aa2c71ec725d9ca76976f0268b823b2394ffc`. В отличие от исследовательского [PR #27](https://github.com/DimaFi/SmartSQL/pull/27), здесь есть реальный C# код и xUnit тесты. Это **только часть M01.04**, не запись в WORK_PLAN о завершении.

## Изменения этой ветки

- `src/SmartSQL.Desktop/Storage/StorageRootSelectionPolicy.cs`: side-effect-free selector для D056; **first-use candidate запрещён, пока внешний bounded legacy-probe не передал `LegacyDiscoveryCompleted=true`**. `RequireMatchingInstalledRoot` дополнительно отвергает отсутствующий/повреждённый или несовпадающий `storage-root.json` относительно выбранного installed bootstrap (не заменяет filesystem identity checks). Типизированные результаты `ExplicitOverride`, `Portable`, `InstalledExisting`, `LegacySelectionRequired`, `FirstUseCandidate`; присутствующий пустой env — ошибка без fallback; portable marker relative к EXE; bootstrap schemaVersion 1 и GUID rootId; bounded JSON/duplicate/missing/unknown fields; lexical root/file/UNC guards; повторяющиеся known legacy paths не выбираются автоматически; root marker GUID парсится отдельно.
- `tests/SmartSQL.Desktop.Tests/StorageRootSelectionPolicyTests.cs`: positive/negative table for env/portable/bootstrap, invalid/future/duplicate/missing JSON, root identity parsing, legacy ambiguous/no overwrite, root path/files/sidecars, no-fs-side-effects. Paths randomly unique in OS temp **только как строки**; тесты НЕ создают каталоги и не изменяют пользовательский профиль.

**Критическое ограничение:** `StorageRootSelectionPolicy.Select()` **ещё нигде не вызывается** в production runtime. `WorkspacePaths.InitializeRuntime()`, `Program.Main`, `Run-Published.ps1` продолжают текущий механизм; это преднамеренно, чтобы не переключить живые данные до завершения fail-closed filesystem phase, early-error UI, concurrency protocol и тестов. Путь пользователя и файлы не изменялись этим PR.

## Найденная проверкой зависимость перед полноценной M01.04

- `src/SmartSQL.Desktop/Presentation/ThemePreferences.cs` содержит дополнительный **Windows-only запрет любых файлов вне D:** в конструкторе. Он осознанно защищает текущую dev-среду, но противоречит будущему product-installed LocalApplicationData root на ПК без D:. Запрещено просто убрать ограничение без безопасной process-root policy и regression tests. Разделить dev-only D:-guard от end-user product paths отдельным зафиксированным code slice; запуск установки в реальный Windows профиль разработчика запрещён.
- `JsonWorkspacePreferencesStore`, `JsonConnectionProfileStore`, `SqlQueryHistoryStore` используют абсолютные пути и **не имеют такого D:-ограничения**. После подключения single resolver проверить, что они читают одно и то же пользовательское хранилище, а не `run-data/local` по launcher.
- `WorkspaceWindow` конструирует `MainWindow` (с возможным default DB) до M03 onboarding. Нельзя выдавать раннюю установку root за устранение silent default creation. Это отдельная M03 задача.
- `WorkspacePaths.GetDataDirectory()` сейчас каждый раз делает `Directory.CreateDirectory` и write probe; новый selector ничего не создаёт. Исполнитель должен реализовать cache+validation стадии, не подменяя их вызовом текущего метода.
- `Program.Main` вызывает `InitializeRuntime()` до Avalonia — ранний startup error flow пока отсутствует. `Run-Published.ps1` независимо выбирает `run-data/local`, пользователю нельзя обещать parity до исправления.
- В тестовой среде разработчика всё созданное на D:, а тест product-default без диска D: только в изолированной согласованной VM; текущие тесты чистые (не создают файлов) и не доказывают файловую устойчивость.

## Контроль перед интеграцией с runtime (следующая задача Codex)

1. **Сначала исполнить реальные тесты** из branch с `scripts/Build.ps1` и `scripts/Test.ps1`, проверить C# compile/format и падения, исправить в этой же отдельной ветке. Не копировать исторические 676 тестов как новый результат.
2. Pure selector получает **уже обнаруженные** known legacy paths; внешний bounded reader обязан явно установить `LegacyDiscoveryCompleted=true` только после полного успешного probe (при exception/uncertainty не устанавливать). Он должен реально найти `<EXE>/data/local` и `run-data/local` без рекурсивного сканирования. Недостоверный или неполный probe не является доказательством first-use.
3. Сделать filesystem stage: fail closed on path/identity/reparse/unavailable, no-overwrite `storage-root.json`/`storage.json`, atomic read/write with cross-process coordination, orphan recovery after crash, rootId recheck under lock. JSON file bytes ограничивать **до** полного UTF-8 decode из памяти; нынешний selector принимает string и сам не читает файлы.
4. В `WorkspacePaths` сохранить выбранный root один раз за process lifetime; передать во все `ThemePreferences`, history, profiles, backups и DB paths. После проверок удалить независимый consumer-default из `Run-Published.ps1`. Не ломать намеренный explicit D:-dev launcher.
5. Поднять безопасный early startup UI до `WorkspaceWindow`; ошибки env/portable/bootstrap показать по-русски, без fallback и пустой БД, с Retry/Select existing/Exit.
6. Разделить dev-only D:-guard и production LocalApplicationData для theme settings с явной policy, **не** с записью в реальный профиль разработчика.
7. Проверить реальные **два процесса**, kill/fault injection, no-D clean Windows VM, portable moved folder, legacy two roots, checksum existing files, active WAL, no accidental managed CREATE.
8. После build/tests/review/UI gates обновить `docs/WORK_PLAN.md`, HANDOFF, CAPABILITY_MATRIX и issue bodies по факту. M01.05 copy-first и M01.06 expanded path races отдельны.


## Дополнительное independent review текущего PR (read-only source review, не тест)

- **Исправлен риск первого запуска без проверки:** пустой список legacy-кандидатов больше сам по себе не считается доказательством того, что поиск старых путей завершён. При `LegacyDiscoveryCompleted=false` pure selector отказывает, если не выбран более приоритетный env/portable/bootstrap и не найдены известные legacy data. Внешний scanner **обязан** задать true только после собственного законченного bounded поиска с обработкой IO/reparse ошибок. Если scanner сам прервётся — fail closed.
- **Исправлено соответствие root marker и bootstrap:** `RequireMatchingInstalledRoot` проверяет, что выбран именно `InstalledExisting`, root marker валиден и rootId совпадает. Нет root marker/другой rootId/future marker → ошибка, не fallback. Тесты добавлены, но ещё **не выполнялись**.
- **Незакрыто:** возможна физическая замена папки после lexical проверки, symlink/reparse/UNC, разрешённые каталоги под executable/version, атомарная файловая запись и concurrent initialization. Критически нужно сначала построить FS/handle-stage с lock, а затем подключать resolver в приложение. Только pure helper не доказывает отсутствие потерь данных.

## Следующий кодовый срез: bounded manifest reader и read-only preflight (продолжение 17.10.2026)

**Реально добавлено в этот же Draft PR #28 (вне runtime):**

- `src/SmartSQL.Desktop/Storage/StorageManifestReader.cs`: read-only открытие только трёх известных имён `smartsql.portable.json`, `storage.json`, `storage-root.json`; 4096 UTF-8 **bytes** max (не char count), invalid byte/BOM/empty/oversize отказ, missing optional отличен от required, IO/ACL errors не превращаются в new root. Читает только синтетический/выбранный caller путь, не создаёт директории и не публикует идентичность сам. Реальный json-policy parsing остаётся за `StorageRootSelectionPolicy`.
- `src/SmartSQL.Desktop/Storage/StorageRootReadOnlyPreflight.cs`: конвейер `env-present → read portable → read installed bootstrap → read required installed root-marker and compare rootId → bounded legacy selection provided by caller`. Для отказа используется исключение, **никогда** `new empty database`. `LegacyDiscoveryCompleted` должен быть явно задан внешним, ещё не реализованным probe. Физическая идентичность root пока **не проверена**.
- `tests/SmartSQL.Desktop.Tests/StorageManifestReaderTests.cs` и `StorageRootReadOnlyPreflightTests.cs`: синтетические файлы исключительно под `SMARTSQL_TEST_DIRECTORY` / `.workspace/test-temp`; Windows tests отказывают при пути не на D:. Проверки отсутствующего/повреждённого/mismatched/oversized файла, UTF-8 byte length, source bytes unchanged, приоритетов и запрета скрытого fallback; **тесты написаны, но ещё НЕ выполнялись**.

**Кодовое ограничение:** оба новых класса **не подключены** к `Program`/`WorkspacePaths`/`App`/`Run-Published.ps1`. Их статус — read-only preflight foundation. Нельзя ставить `M01.04 = done`, публиковать preview/release или допускать reuse пользовательского root по одному совпавшему GUID.

**Незакрытые и опасные случаи для Codex / gate к последующему runtime wiring:**

1. `DirectoryNotFoundException` при отсутствии bootstrap parent в read-only reader трактуется как optional missing; это **не** доказательство безопасного первого запуска при пропавшем системном каталоге/диске или broken link. Внешний Windows FS validator/legacy scanner обязан fail-closed при неопределённости/ACL/symlink. `FirstUseCandidate` остаётся **условным**, требуется empty-target check и отсутствие незаявленных данных, проверенные ближе к commit.
2. Manifest read не привязывается к неизменяемому file handle/path identity между чтением и дальнейшей записью; root может быть подменён. Нужно отдельно безопасное symlink/reparse/UNC/network/parent-ownership/TOCTOU validation, OS lock и reread под lock перед publish. `Guid` rootId — не cryptographic proof ownership.
3. `storage-root.json` rootId пока обязателен в installed mode; explicit env/portable flow требует отдельно утвердить поведение существующего marker/old flat root (legacy/no overwrite), не auto-adopt после ошибки.
4. Блокер `ThemePreferences` hardcoded Windows D:-guard остаётся. Не исправлять «простым разрешением C:» без предписанной изоляции разработки на D: и чистого VM тестирования product LocalApplicationData.
5. Реальные две конкурирующие программы, fault-injected crash, filesystem write probes, atomic bootstrap no-overwrite, early startup Russian UI, launcher parity и restore проверка ещё **не** реализованы.
6. Кодовые тесты/сборка: необходимо на Windows D:-workspace исполнить `scripts/Build.ps1`, `scripts/Test.ps1`, исправить возникающие compiler/test issues. При отсутствии SDK на машине ревью не является passed.

**Новый Codex entrypoint:** прочитай этот handoff и [мастер V2 в docs PR #27](https://github.com/DimaFi/SmartSQL/blob/docs/gpt6-review-codex-handoff-20261017/docs/CODEX_EXECUTION_PROMPT_V2_2026-10-17.md). Уже существуют `StorageRootSelectionPolicy`, `StorageManifestReader`, `StorageRootReadOnlyPreflight` и **три** тестовых класса. Перед новой реализацией проверь diff/main/head, собери и выполни тесты. Далее реализуй проверяемую actual filesystem stage и startup UI, а не дублируй read-only preflight.

## Точный prompt для Codex

> Прочитай `docs/M0104_ROOT_SELECTION_CODE_HANDOFF_2026-10-17.md` и [master V2 в PR #27](https://github.com/DimaFi/SmartSQL/pull/27). Проверь текущий main и отдельную кодовую ветку. Новый класс `StorageRootSelectionPolicy` уже создан, но **не подключён в runtime**: не переписывай его с нуля и не объявляй M01.04 готовой. Начни с `scripts/Build.ps1` и `scripts/Test.ps1` на D:, зафиксируй реальный статус. Затем реализуй единую fs/identity/startup стадию по D056, с fail-closed/lock/crash recovery, и заверши EXE/launcher parity без изменения существующих данных. Отдельно реши dev-only D:-ограничение `ThemePreferences` для будущего ПК без D:. Все code changes только в feature branch, fresh SHA/CI/manual acceptance нужны до merge. Далее переходи к M01.05 и M01.06 лишь после проверенного M01.04.

**Осознанно не сделано:** direct EXE/shortcut parity, installed bootstrap IO, migration, early-error window, Windows build, xUnit execution, UI screenshots, installer and cloud. Не требовать от пользователя повторно подтвердить принятые Google/Яндекс и D056; это уже в плане.
