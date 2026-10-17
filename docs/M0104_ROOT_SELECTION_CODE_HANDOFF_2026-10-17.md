# M01.04 — первая кодовая поставка: side-effect-free root selection

**Статус 17.10.2026:** `code proposed, not integrated; tests written, NOT RUN`. Это отдельный **Draft code PR** от main `7e6aa2c71ec725d9ca76976f0268b823b2394ffc`. В отличие от исследовательского [PR #27](https://github.com/DimaFi/SmartSQL/pull/27), здесь есть реальный C# код и xUnit тесты. Это **только часть M01.04**, не запись в WORK_PLAN о завершении.

## Изменения этой ветки

- `src/SmartSQL.Desktop/Storage/StorageRootSelectionPolicy.cs`: side-effect-free selector для D056, типизированные результаты `ExplicitOverride`, `Portable`, `InstalledExisting`, `LegacySelectionRequired`, `FirstUseCandidate`; присутствующий пустой env — ошибка без fallback; portable marker relative к EXE; bootstrap schemaVersion 1 и GUID rootId; bounded JSON/duplicate/missing/unknown fields; lexical root/file/UNC guards; повторяющиеся known legacy paths не выбираются автоматически; root marker GUID парсится отдельно.
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
2. Pure selector получает **уже обнаруженные** known legacy paths; внешний bounded reader должен реально найти `<EXE>/data/local` и `run-data/local` без рекурсивного сканирования. Недостоверный или неполный probe не является доказательством first-use.
3. Сделать filesystem stage: fail closed on path/identity/reparse/unavailable, no-overwrite `storage-root.json`/`storage.json`, atomic read/write with cross-process coordination, orphan recovery after crash, rootId recheck under lock. JSON file bytes ограничивать **до** полного UTF-8 decode из памяти; нынешний selector принимает string и сам не читает файлы.
4. В `WorkspacePaths` сохранить выбранный root один раз за process lifetime; передать во все `ThemePreferences`, history, profiles, backups и DB paths. После проверок удалить независимый consumer-default из `Run-Published.ps1`. Не ломать намеренный explicit D:-dev launcher.
5. Поднять безопасный early startup UI до `WorkspaceWindow`; ошибки env/portable/bootstrap показать по-русски, без fallback и пустой БД, с Retry/Select existing/Exit.
6. Разделить dev-only D:-guard и production LocalApplicationData для theme settings с явной policy, **не** с записью в реальный профиль разработчика.
7. Проверить реальные **два процесса**, kill/fault injection, no-D clean Windows VM, portable moved folder, legacy two roots, checksum existing files, active WAL, no accidental managed CREATE.
8. После build/tests/review/UI gates обновить `docs/WORK_PLAN.md`, HANDOFF, CAPABILITY_MATRIX и issue bodies по факту. M01.05 copy-first и M01.06 expanded path races отдельны.

## Точный prompt для Codex

> Прочитай `docs/M0104_ROOT_SELECTION_CODE_HANDOFF_2026-10-17.md` и [master V2 в PR #27](https://github.com/DimaFi/SmartSQL/pull/27). Проверь текущий main и отдельную кодовую ветку. Новый класс `StorageRootSelectionPolicy` уже создан, но **не подключён в runtime**: не переписывай его с нуля и не объявляй M01.04 готовой. Начни с `scripts/Build.ps1` и `scripts/Test.ps1` на D:, зафиксируй реальный статус. Затем реализуй единую fs/identity/startup стадию по D056, с fail-closed/lock/crash recovery, и заверши EXE/launcher parity без изменения существующих данных. Отдельно реши dev-only D:-ограничение `ThemePreferences` для будущего ПК без D:. Все code changes только в feature branch, fresh SHA/CI/manual acceptance нужны до merge. Далее переходи к M01.05 и M01.06 лишь после проверенного M01.04.

**Осознанно не сделано:** direct EXE/shortcut parity, installed bootstrap IO, migration, early-error window, Windows build, xUnit execution, UI screenshots, installer and cloud. Не требовать от пользователя повторно подтвердить принятые Google/Яндекс и D056; это уже в плане.
