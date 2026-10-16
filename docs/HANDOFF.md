# Продолжение SmartSQL

## Прочитать перед работой

`AGENTS.md` → [WORK_PLAN](WORK_PLAN.md) → [TASKS](TASKS.md) → [CAPABILITY_MATRIX](CAPABILITY_MATRIX.md) и относящиеся [DECISIONS](DECISIONS.md), затем Git status/history. WORK_PLAN — единственный порядок: **M01–M18, 123 подзадачи**, T01–T25 — постоянные GitHub identities. Старые Stage/N/A — история/группы, не конкурентная очередь.

## Следующая конкретная работа

**M01.01: разделить managed open/create**, чтобы ошибочно выбранный ordinary/пустой/отсутствующий SQLite-файл не создавался/не получал служебную схему. Сначала parent фиксирует контракт/ошибки/границы legacy schema, затем disjoint implementation/tests/review. Parent интегрирует UI callers, запускает проверки, ведёт docs/Git/publish.

M01.03 отдельно определяет product storage вне versioned binaries, EXE/launcher и ПК без D:; правило D: нашей разработки не менять. После M01 — M03/M04 first-use/Excel и M05 snapshot/restore; M06 Google+Яндекс, M07 автоматика/статус. Native export M09 можно вести параллельно после его read/format contracts; T13 не потерян. M02/manual debt идёт при доступном screen tool, M10/T23 — после каждой UI-функции и отдельным полным проходом.

## Завершённая консолидация плана — 16 октября

Полный M01–M18/123 план проверен тремя субагентами по независимым направлениям; замечания по managed regression, Simple/Advanced/shell contract и порядку English приняты и повторно проверены. Устаревшие competing queues и статусы в индексах/архитектурных/UI документах согласованы. Проверены уникальность/последовательность ID, локальные ссылки, 26 issue mappings и diff whitespace. Код приложения не менялся, новый build/tests/EXE не выполнялся.

Коммит плана `1ccc9075aa07eb25a88963ec50a5da86cba17d01` опубликован, remote main SHA подтверждён на момент публикации. Все 26 существующих PLAN/T01–T25 issue bodies обновлены и read-back сверены; состояния сохранены (T05 closed, остальные open). Receipt в github-issues.json, текущий HEAD читать из Git. Следующий шаг остаётся M01.01; опубликованный план не закрывает ручную приёмку и будущие функции.

## Проверенная основа

- Code: `28a4bcbbaa97e7ac0eab6c4239aca51078a77ff9` (T12); последний pre-consolidation audit publication `5cd35692c2c620d9cde1ec07c7806c65f0f94f1a`. Текущий HEAD определять по Git.
- Последний full Debug: **614 passed / 0 skipped / 0 warnings/errors**, Domain 1 / Application 129 / Infrastructure 445 / Desktop 39; **60 реальных PG-фактов**. Планирование код не меняло и не создало новый тестовый прогон.
- Managed SQLite CRUD/четыре типа/структура/фильтры/paste/CSV/JSON/XLSX/backup/restore; ordinary SQLite browse/typed query/opt-in single-cell UPDATE с обязательной копией; PostgreSQL native read/guarded UPDATE/INSERT/DELETE; shared tree/tabs/mode; read-only SQL/highlighting/history/files; первый object-owned structure inspector.
- Gray/Light и закреплённая theme button, явный Simple/Advanced/память, native caption buttons; user S-branding в EXE/окнах/taskbar частично принят.
- Ограничения: native SQLite INSERT/DELETE/general undo нет; все PG incoming FK блокируют DELETE; SQL-write нет, SQLite SQL UTF-8/часть columnless CTE ограничены; T12 incoming/domain/view dependencies не включает. Успешный GetStructureAsync сохраняет current handles, остальные guards не ослаблены.
- Полная ручная T04/T11/T12/T23 открыта. Screen tool не инициализировался (kernel assets/path/os error 3); это не доказанный app defect. Предыдущая partial UI evidence в WORKSPACE_UI_ACCEPTANCE, новый проход не заявлять.

## Поставка и среда

Последняя clean self-contained Windows x64: `D:\Code\SmartSQL-builds\0.7.0-preview.6\win-x64-self-contained-verified-t12-28a4bcb\app\SmartSQL.Desktop.exe`; sourceDirty=false, 237 payload-файлов. ZIP SHA256 `236a17e2c089f8c0f2664f3058cfdfc56a6cb1735b569fe79c2fb81aab1578f3`. Новый Release UI/installer не принят. ISCC отсутствовал при последней проверке; installer не собирался. Старые build/runtime data не перезаписывать.

Всё создаваемое для разработки/тестов/публикации на D:. Только scripts/Build.ps1, Test.ps1, Run.ps1 и delivery helpers. ML целиком отдельно `D:\Code\SmartSQL-model`; 9 прошлых stub-тестов не реальная модель/интеграция, remote ML проверить до claims.

Owned PG fixture `.workspace/postgresql-tests` (17), 127.0.0.1:15432, smartsql_browser_test, была штатно остановлена после T12. Перед новым provider прогоном проверить точный root/status, запустить только её, задать SMARTSQL_POSTGRES_TEST_PORT=15432; не reset/initdb/не трогать installed service. После прогонов parent штатно останавливает её. Credentials/data/TRX/.workspace игнорируются Git; auth values не выводить.

## Подтверждённые новые требования

Аудит [USER_ENTRY_AND_CLOUD_AUDIT](USER_ENTRY_AND_CLOUD_AUDIT.md) и три subreports сведены в M01–M08. Пользователь выбрал **Google Диск и Яндекс Диск**, приоритет — автоматические копии и восстановление; дешёвая команда через общий PostgreSQL, snapshots по очереди отдельно. Никакой cloud delivery/автоматики/OAuth регистрации/нового Excel wizard пока нет.

Нерешённые входы — раздел 7 WORK_PLAN: product paths, empty/NULL/format compatibility, encryption/keys/secret store, retention/outbox, OAuth/test accounts, PG backup scope/utilities, closed-app background, ISCC/signing/clean machine/screen tool, расширенные writes и ML resources. Согласованные приоритеты не спрашивать заново; блокер одного входа не останавливает независимую работу.

## Работа с субагентами и завершение

Протокол — раздел 8 WORK_PLAN: exact Mxx.yy/T-ID, fixed contract, точные владельцы файлов, независимое review. Предпочтения gpt-5.6-sol high или gpt-6.1-sol medium, без Astra/extra-high. Текущие 4 слота включают parent; builds/tests/fixture/Git/docs integration/publish координирует parent.

После каждого среза обновить actual status/evidence, матрицу/UI checklist/HANDOFF и existing issues. Не повторять весь план здесь. Commit/push без данных/секретов; remote SHA и body read-back verify обязательны до claims. Закрытый code slice с открытой UI-приёмкой явно так и называется. Если срез прерван, оставить карточку Mxx.yy с реально изменёнными файлами/проверками/следующим действием.
