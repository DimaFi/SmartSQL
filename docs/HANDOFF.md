# Продолжение SmartSQL

## Прочитать перед работой

`AGENTS.md` → [WORK_PLAN](WORK_PLAN.md) → [TASKS](TASKS.md) → [CAPABILITY_MATRIX](CAPABILITY_MATRIX.md) и относящиеся [DECISIONS](DECISIONS.md), затем Git status/history. WORK_PLAN — единственный порядок: **M01–M18, 123 подзадачи**, T01–T25 — постоянные GitHub identities. Старые Stage/N/A — история/группы, не конкурентная очередь.

## Следующая конкретная работа

**M01.02: полная совместимость managed-формата и проверяемая защита upgrade.** M01.01 реализован/проверен: явные OpenExisting/CreateNew, отказ empty/ordinary/missing, atomic no-overwrite creation и ReadWrite runtime без recreate fallback. Проверить metadata/data/physical schema integrity, фактическую копию перед upgrade и понятность upgrade сообщений; не менять safe open/create. Ручная M01.01 UI-приёмка остаётся открыта.

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

Последняя clean self-contained Windows x64: `D:\Code\SmartSQL-builds\0.7.0-preview.6\win-x64-self-contained-verified-t12-28a4bcb\app\SmartSQL.Desktop.exe`; sourceDirty=false, 237 payload-файлов. ZIP SHA256 `236a17e2c089f8c0f2664f3058cfdfc56a6cb1735b569fe79c2fb81aab1578f3`. Новый Release UI/installer не принят. ISCC отсутствовал при последней проверке; installer не собирался. Старые build/runtime data не перезаписывать.

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
