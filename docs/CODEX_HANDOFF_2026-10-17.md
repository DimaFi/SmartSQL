# Передача Codex: SmartSQL после GPT-6 browser review (17.10.2026)

> **ACTIONABLE HANDOFF; не новая очередь и не приказ автоматически вливать непроверенный код.** Сохраняйте `docs/WORK_PLAN.md` единственным планом, `docs/DECISIONS.md` — журналом принятых решений. Эта ветка / PR содержит **только документы**; M01.04 не выполнен, tests/UI/installer не проверялись. Прочтите [`audits/BROWSER_GPT6_FINDINGS_2026-10-17.md`](audits/BROWSER_GPT6_FINDINGS_2026-10-17.md) целиком.

## 0. Что подтверждено

- Основание: `main` SHA `7e6aa2c71ec725d9ca76976f0268b823b2394ffc`, проверьте актуальный HEAD и отсутствие новых изменений **до работы**. `AGENTS.md`, `docs/HANDOFF.md`, `docs/TASKS.md`, `docs/WORK_PLAN.md`, `docs/PRODUCT_STORAGE.md`, `docs/MANAGED_PROJECT_OPENING.md` обязательны к чтению.
- **M01.01/M01.02** по историческому отчету: 676 tests, 0 skipped, 60 real PG cases, Debug 0 warnings/errors; это не результат этого PR. **M01.03/D056** — решение о хранении, не runtime. **M01.04** ожидает реализацию. T04/T11/T12/T23 и полный UI/release gate остаются открыты.
- Сравнение `src/SmartSQL.Desktop/WorkspacePaths.cs` и `scripts/Run-Published.ps1` подтверждает рассогласование пути без явного env. Не исправляйте это глобальной env переменной, созданием новой БД или рекурсивным поиском по дискам.
- Приложение: Avalonia 12 / .NET 10. Нет собственной СУБД, unrestricted AI SQL, cloud/OAuth/autobackup/шифрования. Любой existing ordinary SQLite открывается **native**, не превращается в managed.

## 1. Следующий безопасный кодовый срез: M01.04

**Цель пользовательская:** независимо от версии, EXE/ярлыка/launcher найти **одно выбранное** место preferences/SQL history/default data; при любой неоднозначности/отсутствии прав показать отказ вместо новой пустой базы.

### Implementation contract (выбирать точный механизм в рамках D056, не выдумывать новый storage format)

1. Выделить **single process-cached resolver** с чистой стадией выбора и отдельной стадией безопасной файловой инициализации. Разрешить storage root до чтения `smartsql.db`, prefs, histories. Не менять root во время жизни процесса.
2. Precedence без fallback: env variable **present** (включая empty => error), explicit portable marker beside EXE, installed bootstrap (`LocalApplicationData/SmartSQL/storage.json`), bounded legacy detection, first-use only when реально нет состояния. Empty/relative env или corrupt/future portable marker — ошибка, не skip. `SMARTSQL_TEMP_DIRECTORY` остаётся explicit absolute.
3. Installed bootstrap `schemaVersion:1/dataDirectory/rootId` ↔ root `storage-root.json` с совпадающим GUID. Существующий unavailable/missing/future/corrupt/mismatched root — fail closed before DB access; никаких автопочинок rootId. Root location независима от EXE/version и от текущей рабочей папки. Пользовательские внешние SQLite/PG connections не перемещаются.
4. **Прежде чем писать bootstrap**, определить и документировать crash/concurrency protocol. Два начальных процесса не могут записать разные выбранные roots. После crash между root marker и bootstrap никто не усваивает сироту молча или не теряет его; no-overwrite; повторный запуск показывает recover/choose, не создаёт пустую замену. Подходящее решение: platform file lock/atomic compare-and-publish + reread under lock; фактическое API и failure tests выбирает исполнитель.
5. Guard-проверки: refuse root of volume, app/version directory, file-as-directory, own `.db`/sidecar-as-directory, unsupported reparse/UNC/network. Normalize relative path **только** из explicit portable marker относительно marker directory и проверять root outside replaceable app binaries. Считать path probing проверкой, не OS sandbox. Race-hardening углублять в M01.06.
6. Bounded legacy candidates (`<EXE>/data/local`, associated historic `run-data/local`, явный путь пользователя): при признаках данных **не** создавать новый default root. Два кандидата — ambiguous error/selection; migration файлов/SQLite WAL НЕ входит в M01.04 (это M01.05). На обычном продукте не запускать поиск по дискам/другим профилям.
7. Ошибку раннего старта показывать **до** WorkspaceWindow (минимальный ограниченный startup error UI): context (mode, sanitized path, reason) + retry/choose existing/exit. При ошибке не вызывать managed CreateNew/OpenExisting. Сохранить обычные native caption buttons и не принимать экранные проверки за unit tests.
8. `Run-Published.ps1`: удалить независимый implicit `run-data/local` selector в **конечном user flow**; без `-DataDirectory` использовать один application resolver, с явным `-DataDirectory` передавать абсолютный env **только child process**. Сохранить существующие ограничители D: для dev/test/preview; не делать из них требование к пользовательскому ПК. Если dev launcher продолжает намеренно использовать отдельный D:-override — называть его явным dev launch, не доказательством parity обычных entrypoints.
9. `WorkspacePaths.InitializeRuntime()` и `Program.Main`: исключить uncaught silent exit при env/permission/marker failure. `TEMP/TMP/TMPDIR` и bundle caches инициализировать после правильного root в границах явной политики; отсутствие product bootstrap не должно создавать что-либо в профиле разработчика.
10. Не трогать SQL guards, managed schema v3, DB providers, cloud, ML, настройки глобальной системы или изменения пользовательских данных вне разрешённого сценария. Любое новое архитектурное обязательство сверх D056 сначала объяснить и записать в `DECISIONS.md` **после** принятия владельцем/parent.

### Границы файлов и тестов

| Изменение | Предполагаемые файлы | Предосторожность |
| --- | --- | --- |
| Root resolution/startup | `src/SmartSQL.Desktop/WorkspacePaths.cs`, `src/SmartSQL.Desktop/Program.cs`, возможно новые небольшие Desktop types | Не протаскивать Windows path/JSON storage через Domain/Application |
| Safe entrypoint | `scripts/Run-Published.ps1`, dev helpers только при реальной необходимости | Не терять D:-isolation для dev/test/preview |
| Startup failure UI | минимальный Desktop dialog/flow; `App.axaml.cs` / existing composition только по необходимости | Никакого создания user DB до resolved root |
| Tests | новые `tests/SmartSQL.Desktop.Tests/WorkspacePathsTests.cs` и focused helpers | Unit precedence + FS side effects на D:, mock/inject known folders; tests не пишут в профиль Windows разработчика |
| Documentation | `docs/PRODUCT_STORAGE.md`, `docs/DECISIONS.md` при новом принятом ADR; `docs/HANDOFF.md`, `docs/CAPABILITY_MATRIX.md`, `docs/WORK_PLAN.md` после actual verified slice | Не закрывать M01.04 по одному build; не создавать второй roadmap |

### Acceptance gate M01.04 (минимум)

- **P01:** env absent/present-empty/whitespace/relative/bad/absolute; invalid present env refuses with *no new DB/marker/default root*.
- **P02:** correct portable marker relative/absolute; corrupt/future/path unavailable; explicit portable mode never silently selects installed bootstrap.
- **P03:** bootstrap valid/missing/corrupt/future/rootId mismatch/root volume missing; existing data SHA unchanged.
- **P04:** first-use only with *no detectable known legacy data*; explicit nonempty unknown dir cannot silently become fresh root.
- **P05:** two known legacy roots block auto-selection; explicit past path can be selected, **not migrated** by M01.04.
- **P06:** kill injected before marker / after marker / before bootstrap atomic commit / after commit; no arbitrary second root/no-overwrite.
- **P07:** simultaneous first-use processes: exactly one rootId selected, losers reread or fail safely.
- **P08:** UNC/junction/reparse parent/root, version/app path, file/DB/WAL as root: fail closed before DB/preferences access.
- **P09:** no rights, disk full, readonly, unavailable temp: consistent explicit error without truncating existing prefs/history.
- **P10:** repeated launch after replace binary, direct EXE vs user launcher vs shortcut, same root and preferences/SQL history/backups. Product clean PC without D: (in authorized **isolated** environment).
- **P11:** no accidental managed creation on existing ordinary SQLite, no implicit migration to new schema.
- **P12:** minimum-size/high-DPI/keyboard/Narrator startup error *manually* tested; if computer-use unavailable, log gate as **not executed**, do not mark done.

## 2. Следующие независимые исследования / codable stages

### M01.05 and M01.06, after verified M01.04

**M01.05** explicit copy-first migration: source/target selection and confirmation, SQLite online backup including active WAL, full physical/typed data integrity and hash verification, atomic publish, old source never deleted, cancellation/kill/no-space no switch. Handle `connections.json`, theme/mode prefs, history and prewrite backups with explicit allowlist and privacy rules, no automatic secret transfer. **M01.06** reparse/TOCTOU/identity/UTF-16 long path/multi-process/ACL/downgrade matrix. Не расширять M01.04 до невалидированного auto-migrator.

### M03/M04 onboarding, not now

Implement three user paths after storage: existing DB (native browse without conversion), no-server user (embedded local SQLite or server help), Excel novice (sheet/field conversion preview before new project). Preserve Gray/Light, Simple/Advanced and caption buttons. Excel’s `00123`/large number/formula/NULL/empty conversions must be explicit; current managed FieldValue normalizes empty/whitespace. Prior to changing it: acceptance+separate decision. Read the browser findings design; do not render planned flows as already working.

### M05→M06→M07 cloud, not now

Local verified immutable SQLite snapshots + manifest/catalog/outbox + clean target restore (M05) must come **before** provider upload (M06). Two adapters: Google `drive.file` visible app-owned backup files, Yandex restricted app folder if real test confirms required OAuth/scope/restore. Upload status is not verified backup; expired session/lost final response gets reconciliation before retry. M07 app-open automatic scheduling/status is distinct from future closed-app service. Tests C01–C12 are in browser review. Do not ask for personal tokens or silently sync live SQLite.

### DEC-KEYS, research only

Threat model and offline recovery key are distinct from local OAuth secret store; select vetted AEAD/format/library only after API/licensing/security review. Test clean-PC restore without old DPAPI/profile. Losing only key cannot be called recoverable. Do not block **unencrypted local** M05 snapshot development with future optional encryption study, but do not send sensitive plaintext to cloud without explicit product decision.

## 3. Execution/verification protocol (mandatory)

1. Before changes: `git status`, `git log -n 10`, read AGENTS/HANDOFF/TASKS/WORK_PLAN and compare current HEAD to baseline. Branch this work; independent agents only disjoint owners under `AGENTS.md` rule20 temporary economy period. No edits outside user permission.
2. Dev/ML/test/preview **only D:**: `D:\Code\SmartSQL` and separate `D:\Code\SmartSQL-model`. Reuse `scripts/Initialize-Workspace.ps1`, `scripts/Build.ps1`, `scripts/Test.ps1` and the configured PostgreSQL fixture; never write to C:/Windows profile or global settings just to simulate production.
3. Implement M01.04 in small stages; unit/FS tests first for root protocol and side effects. Run Debug build/test; label passes for **current commit only** and give counts/warnings/skips/test result paths. Then manual Windows UI/release acceptance separately. No fake test success.
4. Read-only independent review of crash paths, env, data integrity, reparse, EXE parity; remediate. Update exact task status/evidence in WORK_PLAN and HANDOFF, material runtime scope in CAPABILITY_MATRIX; if needed accepted ADR in DECISIONS. No blanket 'M01 done' while M01.05/M01.06/UI open.
5. Commit verified code to branch, read back pushed remote SHA, PR; do not merge without user/parent review and actual gates. Preserve user-created project files and existing history/backups; no model/credentials/real SQL history in source, PR or external review.

## 4. Definition of done for THIS documentation PR

- This review is captured under `docs/audits/`; Codex has a specific M01.04 contract, file ownership and acceptance tasks.
- Runtime implementation is **not done**, and no new full build/tests/manual screen/API run claimed. Main branch and existing accepted D056 untouched by this PR.
- For owner-facing reporting, use exact states: `docs prepared` / `Codex implementation pending` / `M01.04 tests pending` / `M01.05–.06 pending` / `cloud and encryption research only`.

**DO NOT** close any task based only on this review, create a parallel roadmap, start AI training, install new tools, or assert a new preview/installer/cloud backup exists.
