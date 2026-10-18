# Codex: инструкция для реальной реализации SmartSQL Personal

**Это дополнение, а не отмена** `AGENTS.md`, `docs/HANDOFF.md`, `docs/TASKS.md`, `docs/WORK_PLAN.md`. Следовать существующей очереди M01–M18 и пользовательскому M01.04 acceptance. Документ можно давать основному Codex как постоянную продуктовую спецификацию. **Не исполнять весь список за один коммит.**

## Prompt для нового рабочего сеанса

> Ты работаешь в `D:\Code\SmartSQL`, C#/.NET 10 LTS/Avalonia 12. Репозиторий `DimaFi/SmartSQL`. Сначала прочитай `AGENTS.md`, `docs/HANDOFF.md`, `docs/TASKS.md`, `docs/WORK_PLAN.md`, `docs/DECISIONS.md`, `docs/CAPABILITY_MATRIX.md`, `docs/USER_FEEDBACK_PREVIEW9.md`, затем все файлы `docs/personal-edition/`. Проверь `git status`, ветку и HEAD; чужие изменения не перезаписывай.
>
> Мы строим собственный **SmartSQL Personal**: полноценный приятный настольный клиент, функциональный ориентир DBeaver Lite 26.2, но без копирования DBeaver-кода/иконок/UI. UX-референсы только по принципам: плотность и пространственная модель SQL IDE; тихая навигация и фокус Linear; доступные views/filters таблиц как в Notion; command palette и вкладки; Fluent/HIG доступность. Сохраняй **свои** цвета/логотип/стили в `Styles/Tokens.axaml`, Simple/Advanced, Gray/Light, native caption buttons. Исходные данные никогда не теряй молча.
>
> Это предложение **расширения продуктового охвата**, не новая очередь. Текущая задача определяется `WORK_PLAN` и фактическими пользовательскими воротами M01.04; M01.05 только после авторизованного результата пользовательской проверки. Если сейчас уместно и возможно, сначала устраняй UX замечания UF09-01..06 в рамках соответствующих M02/M03/M10 без риска storage, отдельно показывая каждый результат.
>
> Перед кодом сделай мини-аудит: какой существующий класс переиспользуется, конкретное поведение, DBMS/version scope, security/data/NULL/precision effect, negative cases и приёмка. Никакого переписывания ядра или необоснованных new frameworks. Сделай один небольшой vertical slice, потом build/test и, где доступно, реальную ручную UI-приёмку на синтетических D: файлах. Публикуй коммиты после проверки, обновляй CAPABILITY_MATRIX/HANDOFF и status только фактами. Не утверждай «готово», если не исполнял тестов/не проверял интерфейс.

## Приоритетная последовательность

| Slice | Область | Реальное начало | Проверка |
| --- | --- | --- | --- |
| 0 | Baseline/согласованность main и docs | Сейчас | status/HEAD, actual user result of M01.04, 771 checkpoint not mistaken for current |
| 1 | UX аудит preview.9 + acceptance existing M02 | Сейчас параллельно при доступном UI | P0-01..20, UF09-01..06, screenshots, exact open gates |
| 2 | Простая навигация/булев редактор/scroll footer/иконки Undo | После изучения реального UI и выбора безопасного slice | Переключения без потери данных, native write unchanged |
| 3 | Стабильное хранилище old-root migration | **Только после M01.04 результатов** | M01.05 copy-first; M01.06 separate races; never silent fallback |
| 4 | Welcome/recents/тип источника и where-data | M03 deps | Не создаёт БД автоматически, no secrets |
| 5 | Excel master wizard | M04 | Source unchanged, loss report, mapping, tests |
| 6 | Copy/restore/catalog/outbox | M05 | Verified restoration, durable queue, no WAL raw copy |
| 7 | Cloud adapters / schedule / PG backups | M06–M08 and provider scope | Google/Yandex/PG independently verified |
| 8 | Native exports/import + advanced layout polish | M09/M10 | type fidelity, independent sessions, no bulk without capability |
| 9 | Charts / English / new providers | M12–M15 | native types / consistent UI / per provider test |
| 10 | ERD, schema changes, advanced SQL write/transactions | M16 + safety contract | partial metadata visible, migration preview, human-owned manual SQL |
| 11 | Action/Permission + AI | M17–M18 | AI only structured actions, approval for writes, audit |
| 12 | Remaining Lite parity gaps | После проверенного foundation | matrix-feature by feature, no Enterprise-only scope inflation |
| 13 | Team architecture | **Только отдельное решение** | [TEAM_FUTURE](TEAM_FUTURE.md) |

Этапы P1/P2 из матрицы (SQL autocomplete, formatter, builder, ERD, charts) — backlog extensions; конкретные Work Plan entries согласовать перед исполнением, не обходить M01–M18. Выполнять частями с отдельными лицензионными проверками библиотек.

## Пример проработки UX slice: UF09-05

**Hypothesis:** Boolean local/native field имеет серверную валидацию, но presentation не предлагает удобный editor. **Факт пока не доказан** — сначала воспроизвести на синтетическом проекте. План:
1. Найти `DatabaseEditValueValidation.Normalize`, `FieldValue`, `MainWindow.GridPresentation.cs`, `DatabaseCellEditDialog` и converter UI.
2. Подтвердить текущие сценарии для managed vs PostgreSQL vs native SQLite; выяснить где значение редактируется.
3. Ввести «Да/Нет» и `NULL` только если native nullable, не преобразовывать пустое в false.
4. Не трогать provider SQL/transport/storage.
5. Добавить UI/mapper unit tests (true, false, null, nonnullable, read-only), UI keyboard/scale/reopen test.
6. Обновить только конкретный UF09 статус после проверки, не все M02 gate.

## Пример проработки UX slice: UF09-01

**Hypothesis:** пользователю непонятно, чем отличаются несколько источников и почему сразу появляются примеры. План: разделить подписи деревьев на «Локальные проекты» и «Подключения», дать иконку типа источника и tooltip с БД/endpoint/path без секрета, clearly mark demo. Избежать превращения default managed project в новую БД при неудачном open: onboarding M03 и storage gating. Не переписывать connection identity и независимость вкладок.

## Правила PR

1. Одна пользовательская цель / одна независимая группа файлов; допускается параллельная работа только с непересекающимися файлами, интегрирует parent.
2. Перед кодом создать чек-лист test fixtures и риск potenial data loss. SQL и native DBMS metadata — только Infrastructure; UI не строит SQL.
3. Отдельно учесть и ручной SQL (пользовательский), и AI proposals — не объединять их authority.
4. Не хранить user DBs, passwords, user SQL history, ML data, snapshots и builds в Git. Применить текущую D:-политику, не менять C:/global tools.
5. `scripts/Build.ps1`, `scripts/Test.ps1`; WPF не добавлять, Avalonia сохранять; verified remote SHA после push.
6. Отчёт: что реализовано, что **частично**, какие тесты действительно выполнены, что пользователь должен проверить и что остаётся у Codex. Каждый не выполненный gate оставить открытым.

## Следующее главное действие после чтения

**Не запускать Team Server и не переписывать SQL слой.** Сначала убедиться в реальном статусе main и M01.04 пользовательского теста. Если есть доступ к UI, сделать маленький UF09 UX slice без изменения storage и записать результат. Если UI недоступен, подготовить проверяемые unit/synthetic тесты текущего ближайшего разрешённого Work Plan пункта, не подменяя ручную приёмку.

## Как передавать это другому агенту

Можно дать ссылку на `docs/personal-edition/README.md`, но **обязательно** напомнить прочесть `WORK_PLAN` и `HANDOFF`. Нельзя поручать одному агенту весь Lite scope «до конца» в одном цикле: он разрушит зависимость между features и acceptance.
