# SmartSQL — независимое review заданий браузерного GPT-6 (17.10.2026)

> **Статус: research / proposal, не архитектурное решение и не реализация.** Этот документ — входной материал для Codex и владельца. Каноническая очередь остаётся `docs/WORK_PLAN.md` (M01–M18, 123 подзадачи). В рабочий код, `DECISIONS.md`, основной план и права DB этим review ничего не добавлено.

**Опорная версия:** `main` = `7e6aa2c71ec725d9ca76976f0268b823b2394ffc` (17.10.2026). **Материалы:** `docs/BROWSER_GPT6_TASKS.md`, `docs/PRODUCT_STORAGE.md`, `docs/MANAGED_PROJECT_OPENING.md`, `docs/USER_ENTRY_AND_CLOUD_AUDIT.md`, `docs/audits/CLOUD_STORAGE_OPTIONS.md`, `docs/CAPABILITY_MATRIX.md`, `AGENTS.md`. Прочитаны `WorkspacePaths.cs`, `Program.cs`, `Run-Published.ps1`, `Initialize-Workspace.ps1`. Это static/document review, **не** локальный запуск, сборка, тест или проверенный OAuth/API протокол.

## 1. M01.03/D056 — storage review перед M01.04

**Confirmed из исходников:** `WorkspacePaths.GetDataDirectory()` при отсутствии `SMARTSQL_DATA_DIRECTORY` разрешает `<workspace>/data/local` или `<EXE>/data/local`; `Run-Published.ps1` при отсутствии `-DataDirectory` выбирает `<build>/run-data/local`. `Program.Main` вызывает `InitializeRuntime()` до старта Avalonia. Таким образом, для одной публикации запуск через EXE и launcher может вести к разным наборам файлов. Решение D056 не внедрено. Нет доказанного архитектурного блокера для D056, но есть обязательные детали protocol / fail-closed до массового изменения путей.

| ID | Сценарий / опасность | Минимальная мера и проверяемый исход |
| --- | --- | --- |
| R1 | Crash после `storage-root.json`, до `storage.json`: root-сирота | Prepare/validate/commit, запрет silent adoption/overwrite; тест kill каждого перехода, затем явное восстановление выбора без пустого fallback |
| R2 | Два процесса одновременно создают bootstrap с разными `rootId` | Межпроцессная сериализация или compare-and-publish, reread победителя; конкурентные initializers дают один установленный root и не теряют данные |
| R3 | Direct EXE, shortcut и launcher разошлись по каталогам | Только resolver приложения; launcher передаёт исключительно явный override. Тот же root после смены версии и режима запуска |
| R4 | Env `SMARTSQL_DATA_DIRECTORY` существует, но пуст/relative/invalid | Проверять presence отдельно от value; отказ до чтения БД, без fallback. Таблица absent/empty/spaces/relative/absolute |
| R5 | Portable marker/данные оказались в заменяемой version/app папке | Root вне заменяемых бинарников; relative путь допускается для переносимого контейнера целиком, а не произвольного перемещения EXE |
| R6 | Старый `run-data/local` лежит в другой сборке и не попал в bounded discovery | Чёткий предел поиска и действие «Указать прежнее хранилище»; ни автоматического полного поиска дисков, ни выбора по mtime |
| R7 | Bootstrap/root marker corrupt/future/missing/mismatched | Проверка `schemaVersion/rootId` до preferences/DB, отказ без починки чужих файлов, ясный путь восстановления |
| R8 | Root/предок — junction, symlink, UNC/network/sync target, либо путь подменён | Консервативный отказ reparse/network в M01.04; более глубокие handle/race probes — M01.06. Это не OS sandbox |
| R9 | No access/full disk/disconnected volume/temp failure до Avalonia | Минимальный startup error без создания новой БД: путь, причина, Retry / choose existing / Exit. UI acceptance отдельно |
| R10 | В root находятся `connections.json`, `queries/history.json`, native prewrite backups | Allowlist migration (M01.05), целостность файлов, приватные данные не попадают в cloud/ML/логи. Два процесса не означают shared server |

### Три решения до кода

1. **Crash/concurrency protocol**: кто выигрывает выбор installed root; каким атомарным способом создаются root marker/bootstrap; что делать с сиротой после crash; запрещено пересоздавать новый корень молча. Вызвать согласованную процедуру и тестировать с двух процессов.
2. **One entrypoint contract**: precedence present env > portable marker > installed bootstrap > bounded legacy candidates > actual first-use. Dev/debug D:-guard обязателен, а продуктовый LocalApplicationData не должен появляться в dev-сессии без разрешённого изолированного окружения.
3. **Legacy/error UX**: перечислить только известные кандидаты, предлагать явный прежний путь и recovery; на invalid choice не начинать создание/открытие managed DB. M01.05 остаётся отдельной confirmed-copy операцией.

**Разделение ответственности:** M01.04 — resolver/startup, conservative guards; M01.05 — SQLite/WAL-aware copy-first с сохранением источника; M01.06 — expanded races/ACL/Unicode/reparse/TOCTOU. M02/M10/M11 — реальная UI/release acceptance. Нельзя объявлять что-либо done из-за одной документации.

## 2. M05–M07 — Google Диск и Яндекс Диск

Ниже — **implementation brief**, а не работающая cloud-интеграция. Провайдерные контрактные детали взяты из официальных документов, перечисленных в источниках; часть Яндекс endpoints/scopes и runtime behavior остаётся **unknown до отдельного live synthetic protocol spike**.

| Вопрос | Google Drive | Яндекс Диск |
| --- | --- | --- |
| OAuth | Native/Desktop client, системный браузер, Authorization Code + PKCE S256; desktop secret не тайна | Яндекс OAuth supports code/PKCE, но конкретный desktop registration/redirect + отсутствие обязательного embedded secret для refresh нужно подтвердить на зарегистрированном test client |
| Минимальный доступ | `drive.file` для видимых файлов, созданных/открытых приложением; `drive.appdata` — отдельный скрытый scope, не единственное место резервных копий | Кандидат `cloud_api:disk.app_folder` / `app:/`; подтвердить list/upload/download/delete/restore именно с таким ограниченным токеном |
| Загрузка | Binary resumable upload, после обрыва проверить принятую часть; HTTP response не равен restore guarantee | Получить upload URL, PUT байтами, `overwrite=false`; semantics 202/pending/expiry/quota и usable resume проверять на тестовом API, не обещать Google parity |
| Проверка | ID, размер, доступный SHA-256 либо download+local SHA-256; совпавшее имя не proof | Confirmed ready resource, size/hash если есть, иначе byte-identical download+SHA-256; нельзя приравнивать 202 к verified |
| Recovery | Тот же account, перечень app-owned objects, reinstall/another-PC without old catalog; consent/verification required for real distribution | То же, но recovery/account/app-folder scope остаются protocol acceptance; нельзя обещать, что refresh доступен без повторного входа |

**Owner-side registration, без передачи credentials агенту:** Google Cloud project → включить Drive API → OAuth consent/audience/test users → desktop OAuth client → `drive.file` + PKCE/loopback/state → self-test upload/list/revoke/restore → нужные verification/distribution шаги. Яндекс OAuth → зарегистрировать user-facing app, подтвердить допустимые native redirect и конкретные scopes → пройти code/PKCE/refresh/app-folder upload/download/revocation/reinstall на отдельном тестовом аккаунте. Пока нет live проверки, `EXT-OAUTH` остаётся открытым. Не регистрировать аккаунты от имени пользователя и не класть client secrets/tokens в репозиторий.

**Местная база M05 до любых upload:** Online Backup API для live SQLite/WAL в новый snapshot; закрытый immutable snapshot с `snapshotId`, SHA-256 и integrity/schema verification; versioned bounded manifest; durable catalog/outbox с account+destination+remote object ID, без секретов; проверяемый download/restore в *новый* файл; no-overwrite, cancellable/fault-injected state machine; запрет live SQLite в sync folder. PostgreSQL pg_dump/restore — отдельный scope M08. CSV/XLSX export не backup.

**States (proposal):** `DB_COMMITTED → SNAPSHOT_CREATING → LOCAL_VERIFIED → QUEUED → UPLOADING → REMOTE_UNKNOWN/REMOTE_VERIFIED → RESTORE_DOWNLOAD → RESTORE_VALIDATED → RESTORED_NEW_TARGET`. При lost response сначала reconcile exact account/object/snapshot/hash, не делать blind overwrite/retry. Метка `REMOTE_VERIFIED` означает проверенные remote bytes, **не** успешный restore. Нельзя обещать «автокопирование при закрытом приложении» в M07 v1; v1 выполняется только пока SmartSQL открыт, с durable outbox и catch-up on next launch.

### 12 обязательных integration/acceptance сценариев

| ID | Проверка / ожидаемый исход |
| --- | --- |
| C01 | Live WAL + внешний writer, UTF-8/UTF-16/BLOB/Int64/NULL: standalone snapshot открывается, schema/raw values и integrity корректны |
| C02 | Kill на temp/publish: неполный snapshot никогда не маркируется LOCAL_VERIFIED |
| C03 | Offline/restart: outbox сохраняет UUID/account/destination и не пишет ложный delivered |
| C04 | Потерян финальный ответ Google: reconcile remote, нет дубликата/overwrite, exact bytes verified |
| C05 | Google resume partial/expired session: продолжение от подтверждённого offset или новая session для того же snapshotId |
| C06 | Yandex 202/pending/expired URL: delivery только после ready+download SHA; неизвестные протоколы явно reject |
| C07 | Аккаунт A→B, два аккаунта: чужие копии не смешиваются |
| C08 | Revoked/expired OAuth/consent restriction: AUTH_REQUIRED и целый local snapshot, без scope escalation |
| C09 | Disk full/readonly/quota/429/5xx: исходник и последняя good copy целы, bounded retries |
| C10 | Changed remote bytes/manifest corruption/deletion: no restored-success state, no original overwrite |
| C11 | Clean install/clean PC без старого profile/catalog: выбран правильный account/snapshot, отдельная DB действительно открывается |
| C12 | Два устройства/одно имя/ручные snapshots: обе UUID generations сохраняются, нет last-writer-wins |

**Автоматизация:** commit состояния SQLite/PG отделён от отправки; offline storage and queue statuses точные. Retention/удаление выключено до подтверждения последней копии и restore. Бездумная выгрузка корпоративных БД на личный диск запрещена. История SQL, profiles, токены, чужие внешние файлы и настройки не являются default snapshot payload.

## 3. M03/M04/M10 — first-run для трёх аудиторий (design only)

- **Готовая БД:** «Открыть существующую базу» → выбрать обычный SQLite/managed файл либо PostgreSQL connection → сначала безопасный browse → показать источник и права. Отсутствующий файл — отказ, а не создание пустого; чужая SQLite не мигрирует в managed автоматически.
- **Специалист без сервера:** «Создать локальный проект» → встроенный SQLite без сервера и без регистрации; альтернативно «Подключиться к серверу PostgreSQL» с официальной инструкцией установки/подключения. Чужой клиент GUI не требуется; MySQL/MariaDB/SQL Server обозначены как план, а не clickable working provider.
- **Excel-новичок:** «Начать с Excel/CSV» → preview выбора листов/диапазона/заголовков → исходные и **реально сохраняемые** значения → предупреждения (формулы, `00123`, даты/timezone, decimal precision, NULL/empty, whitespace, макросы, несколько листов) → отдельный новый проект. Пока M04 не готов, не изображать импорт напрямую в новый проект реализованной кнопкой.

**UI правило:** текущие Gray/Light, Simple/Advanced, pinned theme/mode controls и native caption buttons сохраняются. Будущий индикатор копий показывает **per-destination** state и не обещает шифрование/auto-cloud. Проверить keyboard, Narrator, min window и DPI 125–200%. Предложение M03 не является разрешением на полный редизайн.

**Измеримые UX checks:** U01 existing SQLite no mutation; U02 PG connection error/retry; U03 first local project no silent default; U04 switch themes/modes preserves permissions; U05 Excel multiple sheets and text IDs preview; U06 cancel import no output; U07 missing recent path no recreation; U08 offline no login needed; U09 user finds project file and last verified copy separately; U10 125–200% DPI + keyboard/Narrator dialog navigation. Результаты пока **не измерялись**.

## 4. DEC-KEYS — threat model (research only)

Threats: lost PC, forgotten passphrase/recovery code, malicious cloud copy, modified/truncated/rolled-back archive and manifest, stolen token, account switch, two devices, disk full, captured running PC. OS-local token storage != portable backup recovery; DPAPI alone не восстанавливает данные на другом ПК. AEAD/authenticated framing must cover metadata needed for correct restore; signed/versioned bounded metadata and keyId, no homemade crypto algorithm.

**Practical candidates:** native .NET AES-GCM with vetted chunk framing, libsodium `secretstream` with supported/verified .NET binding and licensing, or mature encrypted-container library after maintenance/audit/license review. Select only after `DEC-KEYS`, do not ship secretstream-inspired custom protocol. Decide portable offline recovery key vs password KDF; losing the only key must produce explicit *unrecoverable*, not false recovery. Cloud provider still sees timing/size/account metadata. A compromised running machine is out of this guarantee.

**Acceptance:** full restore in new Windows profile or clean VM without previous OS secret container, using *only* archived cloud copy + separately retained recovery key; wrong key/changed bytes/future format/rollback/huge manifest/partial chunk/low disk must fail before replacing source. Exclude tokens/keys/session URLs from logs, bootstrap, repos, manifest and ML corpus. Encryption research does not block M05 local snapshot pipeline.

## 5. Sources, unknowns and honest status

- D056 storage: [`docs/PRODUCT_STORAGE.md`](../PRODUCT_STORAGE.md), [`docs/MANAGED_PROJECT_OPENING.md`](../MANAGED_PROJECT_OPENING.md); existing entrypoints: [`WorkspacePaths`](../../src/SmartSQL.Desktop/WorkspacePaths.cs), [`Program`](../../src/SmartSQL.Desktop/Program.cs), [`Run-Published.ps1`](../../scripts/Run-Published.ps1).
- Official OS/DB: [Microsoft Known Folder](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid), [SQLite Online Backup](https://www.sqlite.org/backup.html), [PostgreSQL pg_dump](https://www.postgresql.org/docs/current/app-pgdump.html).
- Official Google: [native OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [scopes](https://developers.google.com/workspace/drive/api/guides/api-specific-auth), [appData](https://developers.google.com/workspace/drive/api/guides/appdata), [resumable](https://developers.google.com/workspace/drive/api/guides/manage-uploads), [file metadata](https://developers.google.com/workspace/drive/api/reference/rest/v3/files).
- Official Yandex: [register](https://yandex.ru/dev/id/doc/ru/register-client), [auth code](https://yandex.ru/dev/id/doc/ru/codes/code-url), [refresh](https://yandex.ru/dev/id/doc/ru/tokens/refresh-client), [Disk REST](https://yandex.com/dev/disk-api/doc/en/concepts/quickstart). Certain detailed app-folder/upload links were inaccessible in the research context; exact current behavior is **unverified**.
- Official crypto: [libsodium secretstream](https://doc.libsodium.org/secret-key_cryptography/secretstream), [password hashing](https://doc.libsodium.org/password_hashing/default_phf), [.NET AES-GCM](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0).
- **No claim of acceptance:** no Windows build/full tests, UI acceptance, cloud account/API test, installed executable, key-security audit, or new runtime commit was performed for this review. Last repository-documented baseline is M01.02: 676 passing tests, including 60 PG facts; do not misrepresent this as review verification.

**Next:** `docs/CODEX_HANDOFF_2026-10-17.md` describes implementation slice boundaries, touched files and gates. Owner/parent validates suggestions against current HEAD and, only after acceptance, records any new decisions in `docs/DECISIONS.md`. Only `WORK_PLAN.md` tracks task completion.
