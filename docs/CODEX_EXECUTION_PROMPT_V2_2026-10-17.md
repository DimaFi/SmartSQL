# CODEX — MASTER EXECUTION PROMPT V2 (SmartSQL, 17 октября 2026)

> **Актуализация после начала реализации:** отдельный [Draft code PR #28](https://github.com/DimaFi/SmartSQL/pull/28) уже содержит `StorageRootSelectionPolicy` и xUnit-тесты чистого выбора кандидата D056, plus [code handoff](https://github.com/DimaFi/SmartSQL/blob/feature/m0104-root-selection-foundation-20261017/docs/M0104_ROOT_SELECTION_CODE_HANDOFF_2026-10-17.md). Этот код **не подключён к runtime, тесты не запускались**, и main по-прежнему на `7e6aa2c`. **Codex: перед созданием своего selector проверь PR #28 и используй/исправь эту основу, не создавая дубликата.  **Новый реальный код в PR #28:** `StorageManifestReader` читает 3 D056 JSON-маркера только read-only, с лимитом 4096 UTF-8 bytes, строгим decoder и отказом на ошибках; `StorageRootReadOnlyPreflight` объединяет чтение с pure selection и проверкой установленного rootId, не создавая ничего. Добавлены `StorageManifestReaderTests` и `StorageRootReadOnlyPreflightTests` с синтетическими файлами на D:. Эти классы **не используются runtime**, новые тесты **не запускались**; в первую очередь собрать и проверить весь PR #28, затем сделать reparse/TOCTOU/crash/interprocess filesystem initialization, early UI и launcher parity. Отсутствие bootstrap parent/legacy uncertainty НЕ означает реальный first-use, требуется внешний fail-closed validator. **Дополнительное ревью PR #28:** введён `RequireMatchingInstalledRoot` для отказа при несовпадении `storage.json`/ `storage-root.json`, а `FirstUseCandidate` теперь возвращается только после `LegacyDiscoveryCompleted=true` от будущего bounded scanner. Codex обязан проверить эти инварианты реальными xUnit тестами и не устанавливать discovery=true при IO/permission/reparse errors. Ни один тест здесь не был выполнен.  Начни с D:-build/tests этого PR, затем отдельно реализуй filesystem/lock/crash/bootstrap/early startup UI и EXE/launcher parity.** Текущий `ThemePreferences` в Windows запрещает файлы вне D:, поэтому раздели dev-only guard и будущий product root на ПК без D: только после safe resolver и тестов. M01.04 не закрыта; M01.05/M01.06 отдельно открыты.

> **ГОТОВЫЙ ПОЛНЫЙ ПРОМПТ ДЛЯ CODEX.** Он консолидирует текущий handoff и вторую волну GPT-6-исследований, не создавая параллельный план вместо `docs/WORK_PLAN.md`. Если состояние репозитория изменилось после указанной даты, фактический Git HEAD и актуальный канонический WORK_PLAN важнее этого документа. Статусы исследований ниже не равны реализованному коду.

## Роль и главная цель

Ты — ведущий разработчик/интегратор SmartSQL, универсального понятного desktop-клиента структурированных данных: C#, .NET 10 LTS, Avalonia 12, SQLite local managed + ordinary native SQLite, PostgreSQL через существующий сервер. Используй готовые СУБД, а не собственный storage engine, optimizer или SmartSQL application server. Не добавляй новые технологии/пакеты, provider/write rights или unrestricted AI SQL без отдельного обоснованного принятого решения. Domain не зависит от Infrastructure/Desktop; provider SQL в Infrastructure.

Пользователь просит **сделать много следующих заданий максимально аккуратно по ТЗ**, а не только написать очередной обзор. Начни с реально ближайшего M01.04, далее выполняй проверяемые срезы, продолжай независимую работу по зависимостям. Если full implementation ограничен средой/внешним аккаунтом/ручным UI — доставь всё безопасно выполнимое и пометь каждый остаток явно. **Никаких фиктивных success, test results, дат завершения или релизов.**

## 0. Проверить актуальность и сохранить чужую работу

Repo: https://github.com/DimaFi/SmartSQL; baseline main при подготовке исследования: `7e6aa2c71ec725d9ca76976f0268b823b2394ffc`, read-back verified 17.10.2026. Draft docs PR: https://github.com/DimaFi/SmartSQL/pull/27, branch `docs/gpt6-review-codex-handoff-20261017`.

1. До изменений: `git status`, `git log`, branch/PR/commit diff, recently merged/open PRs. Возможно Codex уже изменил local code — **не перезаписывай**. Если main ушёл вперёд, reconcile а не работать по застаревшему SHA. Не force push/reset чужого.
2. Читать main: `AGENTS.md`, `docs/HANDOFF.md`, `docs/TASKS.md`, **`docs/WORK_PLAN.md` (единственная очередь M01–M18, 123 подзадачи)**, `docs/DECISIONS.md`, `docs/PRODUCT_STORAGE.md`, `docs/MANAGED_PROJECT_OPENING.md`, `docs/CAPABILITY_MATRIX.md`, `docs/WORKSPACE_UI_ACCEPTANCE.md`, `docs/DELIVERY.md`.
3. Читать **оба** handoff из PR #27: `docs/CODEX_HANDOFF_2026-10-17.md` и прежний `docs/CODEX_EXECUTION_PROMPT_2026-10-17.md`, затем данный V2 и все audit documents ниже. Они evidence/research, **не** accepted new ADR, fixed file names or claim of tested functionality.
4. Историческая M01.02: 676 passing, including 60 actual PostgreSQL facts, 0 skips/warnings/errors, published preview.8, incomplete M02/UI. Этот PR/пакет **не выполнял .NET сборки или тесты**. M01.03/D056 — storage policy only. M01.04/.05/.06 и M03–M18 будущие или частичные по WORK_PLAN.
5. Все **project-created** dev data, caches, temp, synthetic DB, test/preview results — только D:, `D:\Code\SmartSQL`, ML `D:\Code\SmartSQL-model`. Используй `scripts/Initialize-Workspace.ps1`, `Build.ps1`, `Test.ps1`. Не создавай bootstrap/test данные в реальном C: профиле разработчика. Product без диска D: тестировать в явно разрешённой изолированной clean VM. Не менять глобальные настройки/installed tools/профиль без необходимости и согласия. Запускать только изолированную известную PG fixture по HANDOFF; не удалять/инициализировать существующие серверные каталоги.
6. Branch для кодового среза создавать **от актуального main отдельно от docs PR**. Документы PR #27 можно использовать без предварительного merge; не считать docs ветку доказательством работающего runtime. Build/tests/Git/issue updates согласует parent. Делегировать ограниченные независимые задачи с непересекающимися файлами; временное правило экономии субагентов из AGENTS действует лишь до своего expiresAt.

## 1. Исследовательские материалы: что уже подготовлено, НЕ повторяй без новой причины

| Материал | Предмет | Реализация на 17.10 |
| --- | --- | --- |
| `docs/audits/BROWSER_GPT6_FINDINGS_2026-10-17.md` | Первоначальный adversarial review D056, cloud, UX, keys | docs only |
| `docs/audits/M01_ROOT_RESOLVER_CONTRACT_REVIEW.md` | M01.04/.05/.06: 10 инвариантов, 15 сценариев | not implemented |
| `docs/audits/M05_M07_BACKUP_RECOVERY_PROTOCOL_PROPOSAL.md` | Snapshot/catalog/outbox/restore, B01–B20 | not implemented |
| `docs/audits/OAUTH_PROVIDER_READINESS_2026-10-17.md` | Google/Yandex OAuth, Yandex native secretless refresh — **unverified** | research only |
| `docs/audits/M03_M04_FIRST_USE_ACCEPTANCE_PROPOSAL.md` | Welcome/Excel exact preview, UX01–15 | not implemented |
| `docs/audits/DEC_KEYS_SECURITY_RECOVERY_REVIEW.md` | Encryption/lost PC/recovery secret, S01–12 | no crypto |
| `docs/audits/M08_POSTGRES_BACKUP_RESTORE_READINESS.md` | pg_dump/pg_restore archive, scope/tool versions/security, PG-B01–14 | not implemented |
| `docs/audits/M09_NATIVE_EXCHANGE_SAFE_SLICES.md` | Native export T13 then guarded import T14, N01–18 | not implemented |
| `docs/audits/M10_M11_UI_RELEASE_ACCEPTANCE_PACK.md` | Windows UI/DPI/Narrator, self-contained folder, installer/update R01–18 | not accepted |
| `docs/audits/M12_EXACT_CALCULATIONS_SCOPE.md` | Count/sum/avg/graphs with exact scope CALC01–12 | not implemented |
| `docs/audits/M14_M16_PROVIDER_ADMISSION_MATRIX.md` | Future MySQL/MariaDB/SQL Server, schema/migration PROV01–12 | not implemented |
| `docs/audits/M17_M18_ACTION_PERMISSION_MODEL_BOUNDARY.md` | Future AI actions without arbitrary SQL, ACT01–14 | no Action layer/model integration |

Прочти реальный код до редактирования: source locations from these documents are review hints, not proof they remain unchanged. **Important correction from code:** `SqlReadResult.Truncated` is the property (NOT WasTruncated), `RowLimit` is 1..500. `scripts/Publish.ps1` currently sets **`PublishSingleFile=false`**; don't spontaneously redesign publication as single-file. `Run-Published.ps1` and `WorkspacePaths` currently pick distinct implicit data roots — M01.04 P0.

## 2. ОЧЕРЁДНОСТЬ ИСПОЛНЕНИЯ С КРИТЕРИЯМИ (не giant-bang)

### Wave A / P0: M01.04 — реализуй прямо сейчас

- Единый process-cached resolver: strict precedence present `SMARTSQL_DATA_DIRECTORY` (empty/relative = fail), portable marker beside EXE, installed bootstrap with matched root marker, bounded explicit legacy discovery, genuine first use. User root outside versioned binaries. Ни EXE, ни shortcut, ни ordinary launcher не выбирают свой hidden root.
- D056 Windows policy, exact schemaVersion/rootId, fail-closed unknown/future/corrupt/unavailable/reparse/UNC. No overwrite, no silent move/adoption, no database creation on error. Root fixed for process lifetime, validated *before* App theme/preferences/SQL history or WorkspaceWindow.
- Concurrency and crash: one winner for bootstrap/root, losers reread; orphan marker after crash not auto-adopted; explicit recovery action/error before DB usage. Early startup UI in Russian with Retry/Choose existing/Exit; if UI tooling unavailable, leave UI acceptance open.
- `Run-Published.ps1`: remove independent hidden default for **consumer mode**; preserve development D:-override and script security. Direct EXE, consumer launcher and shortcut root parity tested in **isolated VM** without touching developer Windows profile.
- Focused unit/FS/fault injection/two-process tests on D:, see P01–P12 and T01–T15 in audits. Preserve M01.01/.02 safety, SQLite v3 and all native permissions. No migration in M01.04.

**Gate:** build/tests with current source commit; startup no silent fallback; manual UI parity separately. Code PR, not docs PR.

### Wave B: M01.05 copy-first → M01.06 hardening

- M01.05: known legacy roots/selection/explicit confirmation, SQLite Online Backup including committed WAL, independent checks on copied bytes/schema/version/raw values, verified no-overwrite publish, then bootstrap switch. Source remains unchanged. Preferences/history/prewrite copies only via privacy-reviewed allowlist; no default secret transfer; failures/cancel/kill/multi-candidate never pick arbitrary winner.
- M01.06: readonly/full disk/partial rename, long Unicode path, symlink/junction/reparse/TOCTOU, concurrent processes and external writer, volume removal, downgrade/uninstall; fail closed and honest limitations.
- No declaration M01 complete while M01.04–.06 and relevant UI gates unverified. M02 regressions on affected local default necessary.

### Wave C: M02 UI/native regression + M03/M04 after storage, carefully

- M02: execute OPEN manual checklist on synthetic D:-DB (PG guarded UPDATE/INSERT/DELETE/conflict/unknown commit, native SQLite prewrite backup, T09 filters/sort, T11 SQL lifecycle/picker/IME/cancel/drain, T12 inspector, full theme/mode and tab isolation). If computer-use init fails, keep exact UI not run.
- M03: first-run 3 distinct paths, existing DB native no forced conversion, embedded SQLite no server required, Excel newcomer's preview only when real; recent items without passwords, meaningful «Где мои данные», no auto default creation before user intent.
- M04: XLSX/CSV to new managed project with raw→effective mapping/preview, all sheets, leading zeros, formula/date/time/precision, NULL/empty/whitespace; **changing managed semantics requires accepted DEC compatibility plan**, not a casual FieldValue edit. Abort/no partial project and unchanged source SHA.

### Wave D: M05 local snapshot/restore foundation (before cloud), M06 Google/Yandex, M07 automation

- M05: strict SQLite backup of live WAL, immutable verified artifact with SHA/schema/integrity, durable bounded manifest/catalog/outbox and restore into new target, B01–B08/kill tests, no copy of live .db into sync folder.
- M06: register provider app/test credentials only by user-approved process; Google system-browser desktop OAuth PKCE/drive.file, proper resumable state/download verification. Yandex OAuth code/PKCE does not prove refresh without distributable client_secret; **validate native secretless refresh and app-folder scoped clean-PC restore** before public delivery. No embedded common secret in EXE and no personal token shared with Codex/Git. Wrong account/revoke/quota/unknown remote success tests; Google independent of unresolved Yandex if M05 already stable.
- M07: app-open scheduling only, bounded throttling/offline durable outbox/catch-up, per destination/account status. Closed-app scheduler/service optional future slice, not silently enabled. DEC-KEYS requires vetted library/license and **portable offline recovery key** to promise encrypted cloud; doesn't block M05 local copies.

### Wave E: M08 PostgreSQL backup (can research separately; code only after M05 local catalog contract)

- Start M08.01 scoped tool/version discovery and controlled arguments, then verified `pg_dump -Fc` and strict warnings/exit/cancel/manifest M08.02.
- M08.03 inspect + **restore in fresh isolated database** with ordinary credentials and verify schema/data/sequences/roles described by scope; untrusted dump may execute code. Do not run raw dump as superuser in production. PostgreSQL major compatibility and subset-schema dependencies are explicit.
- M08.04 hook **only verified** artifacts to M05/M06 delivery. M08.05 Mail WebDAV and M08.06 other destinations are independent later verified adapters, not necessary to start Google/Yandex or first PG dump.
- PG-B01–14 in audit. Do not substitute table CSV for PostgreSQL backup.

### Wave F: M09 native export T13, then guarded import T14

- Start with export of already displayed `SqlReadResult` (`Truncated`, bounded rows) as **displayed limited result**; no hidden SQL re-execution. Preserve ordinal/duplicate names/types/NULL, report CSV/XLSX losses/Excel formula-safe mode. Atomic no-overwrite output and cancel/tests N01–N12.
- Then provider read-only single snapshot streaming full-table/filter export **separately**; don't stitch independently read pages and promise one consistent database state.
- T14 batch import/clipboard only after explicit provider-specific write capability, fresh metadata/permission/constraints, mapping/raw→saved preview, transaction/rollback/unknown COMMIT, no hidden unlimited SQL. Native SQLite INSERT currently absent, not authorized by existing opt-in UPDATE. N13–N18; mark T13 and T14 status independently.

### Wave G: M10/M11 live UI/release gates, cross-cutting every code slice

- Gray/Light, Simple/Advanced, native captions, RU text, 960×600 and 100/125/150/200% DPI, focus/Tab/Enter/Escape/IME/Narrator, native SQL + file-picker lifecycle and clearly labelled NULL/status/permission. Review each dialog/state; historical T23 UI not complete.
- Windows x64 preview is currently **folder-based self-contained** (`PublishSingleFile=false`), has manifest/ZIP SHA/versioned names under D:. Don't switch to AOT/trimming/single-file without an independent accepted compatibility slice.
- Installer: existing `packaging/windows/SmartSQL.iss` and `Build-Installer.ps1`; detect ISCC rather than assuming installed. In isolated clean VM, install/update/uninstall/reinstall preserving exact root/data/backups, no admin/SDK/D: assumptions for final users, no silent deletion or corrupted bootstrap. R01–R18.
- Team guide for existing PostgreSQL LAN host with DB roles/TLS/backups and cheaper **sequential** snapshot handoff separately, no claims of simultaneous SQLite editing in cloud folder. Diploma synthetic pedagogical tables demonstrate universal client, not core specialization. Linux/macOS delivery future independently verified.

### Wave H: bounded future M12–M16 (only after respective dependencies, not block P0)

- M12: exact scope/count(non-null)/sum/avg & modest chart on real bounded results; `numeric` precision, NULL, `Truncated` treated honestly. CALC01–12. No giant BI system.
- M13: Russian/English catalog completeness, invariance of stored data/SQL/types, keyboard/Narrator parity; no partial English UI claim.
- M14: **MySQL and MariaDB separately** version/driver/TLS/rights/type/metadata/read-only SQL/guarded writes/export/restore tests; they are not the same DBMS. PROV01–12.
- M15: SQL Server after corresponding stable provider foundations; auth/TLS/driver/version/metadata/concurrency and separate fixtures, not SSMS dependency.
- M16: small independent metadata/relationships, managed type-change with recovery, SQLite native INSERT/DELETE after M05/T10, PG incoming-FK DELETE protection after confirmed child dependencies, cross-provider migration with loss report, optional SQL tools only as separately justified work. No broad hidden DDL/undo/unrestricted writes.

### Wave I: M17 Action/Permission → M18 isolated local assistant (only after security gates)

- M17 allowlisted structured actions, qualified source/target IDs, fresh metadata/provider DB privileges, explicit preview/confirmation for mutations, bounded executor, privacy-minimized audit; reject unknowns and arbitrary SQL model output. ACT01–14.
- M18 ML knowledge D:\Code\SmartSQL-model can refresh PUBLIC allowlisted source snapshots after verified commits, synthetic train/holdout separation; no real user data/credentials/SQL history; no training/inference/weights/device install without separate scope and resources. Model output never grants rights. Main desktop remains usable without model.

## 3. Test/commit/release workflow required **after each meaningful slice**

- Independent review of dangerous migration/backup/write boundaries, no force push, no hidden write after read-only preview. Fix discovered defects before marking slice complete.
- Run `scripts/Build.ps1` and `scripts/Test.ps1` under D: initialized workspace. Current real test numbers per project, skips, warnings/errors, TRX path, fixture start/stop and commit SHA must be reported **fresh**; don't copy historical 676 as new success.
- UI acceptance and clean-VM installer/preview checks separate from automated unit/integration tests. If no screen available, say not run with exact reason. If SDK/ISCC/account unknown, no false claims.
- Update existing `docs/WORK_PLAN.md` checked boxes **only for verified actual stages**; `docs/HANDOFF.md`, `docs/CAPABILITY_MATRIX.md`, `docs/WORKSPACE_UI_ACCEPTANCE.md`, issues after actual evidence. Significant new decisions go into DECISIONS only after acceptance. Existing D056 cannot be silently revised. No second TODO roadmap.
- Commit reviewed source/test changes to task-specific branch, verify remote SHA/read-back and PR. Do not merge unverified code into main. Preview only from clean commit in fresh unique D:-output; never overwrite prior archives/user data.
- Cloud/test accounts are user-controlled; secret material stays out of prompts, logs, Git, ML/public docs. Root/data backup remain separated from executable payload.

## 4. User-facing handoff report format, mandatory

**A. Verified implemented now:** Mxx.yy ID + concrete source/test paths + commit/PR + command, actual passed/skipped/failed counts + independent review + UI tested/not.

**B. Prepared research/spec only:** changed docs + checks/links; never present as runtime.

**C. Pending/blocked:** concrete reason and how to resume (manual Windows UI, isolated VM, ISCC compiler, OAuth native registration/test account, restore, DEC-KEYS, permissions).

**D. Data safety:** exact created project roots in D: (not user databases), source unchanged/hashes where relevant, temp cleanup, no credentials, no C: developer writes.

**E. Next exact slice:** one closest dependency-unblocked item from WORK_PLAN with acceptance plan and file owners.

**Запуск:** прямо сейчас Wave 0, затем Wave A M01.04. Продолжать столько независимых безопасных работ, сколько можно *реально проверить*, но не выдавать подготовку документации, прошедшие старые тесты и mockups за работающее приложение. Историю проекта, старые PR и пользовательские файлы сохранить.
