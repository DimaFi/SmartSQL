# Проверка Personal: функциональность, красота, совместимость, самостоятельность

**Источник статусов:** [../CAPABILITY_MATRIX.md](../CAPABILITY_MATRIX.md) и [../WORK_PLAN.md](../WORK_PLAN.md). Этот список = проектируемые критерии проверки, не протокол уже выполненных тестов. Никаких «pass» без actual evidence.

## 1. Три разные приёмки

1. **Автотесты:** unit, property, integration, fixtures, build. Результат с SHA, коммитом, датой, средой и TRX.
2. **Ручная UI:** реальные окно, мышь, клавиатура, screen reader, масштаб, screenshots. Успех unit не заменяет UI.
3. **Release smoke:** собранный self-contained installer/portable package на чистом ПК и на Linux/macOS; install → create/import/open → edit → backup → reopen/upgrade/restore. Не перезаписывать реальные пользовательские данные.

Критичный результат только после всех необходимых классов приёмки.

## 2. Минимальный набор функциональных сценариев

| Test ID | Шаги | Условие успеха |
| --- | --- | --- |
| P0-01 | Чистый запуск без пользовательских БД | Нет скрытого создания БД; ясно что делать, demo optional |
| P0-02 | Открыть прежний managed SQLite | Тот же ID/данные/типы, без silent schema recreate |
| P0-03 | Подключиться к чужому SQLite | Не создаёт managed metadata, чтение без миграции |
| P0-04 | Подключиться к PostgreSQL под read-only role | Чтение да; все запрещённые actions disabled и server-refused |
| P0-05 | Один и тот же object name в двух source | Вкладки, страницы, действия и SQL строго изолированы |
| P0-06 | Excel большой диапазон, коды 00123, пустые ячейки | Preview сохраняет границы, reporting before commit; исходник цел |
| P0-07 | Изменение Boolean true/false/null | UI round-trip после reopen; nonnullable null refused |
| P0-08 | Drag reorder в data grid | Только presentation, физическая schema и данные без изменений |
| P0-09 | Создание таблицы с 100 полями, small window | Footer/buttons/scroll не перекрываются при scaling |
| P0-10 | Ошибочный путь/отказ открытия | Нет fallback на пустой DB, понятное recovery действие |
| P0-11 | Конкурентный UPDATE/DELETE другой сессией | Provider detects conflict, no silent overwrite |
| P0-12 | Потерянный COMMIT response | Unknown status, no autoretry, reload/reconcile |
| P0-13 | Read-only SQL с unsafe statement | Серверная/authorizer защита, без выполнения |
| P0-14 | Query save/open/cancel/dirty tabs | Без silent truncation/autorun/lost edits |
| P0-15 | Invalid UTF-8, huge SQL, huge cell, binary | Explicit bounded error, no silent replacement |
| P0-16 | Backup with SQLite WAL activity | Verified consistent artifact; original DB unaffected |
| P0-17 | Broken/future backup restore | No overwrite; error/compatibility report |
| P0-18 | Installed/portable path, second process | One root, exact state persistence, expected fail-closed |
| P0-19 | Успешный UPDATE, failed refresh | Success vs refresh failure separated, no retry UPDATE |
| P0-20 | Переключить темы и Simple/Advanced mid-query | SQL/result/selection/permissions unaffected |

Существующие `M02.01–M02.10` и `USER_TEST_M0104` остаются обязательными — таблица выше их **не заменяет**.

## 3. Advanced / Lite-function тесты перед «паритетом»

- **SQL editor:** bounded results, exact native output, snippets, basic completion, dialect select, selection/run, typed params, multiple tabs, file conflict, cancellation and close session. Read-only и manual DML/DDL должны быть разными политиками. No executing proposed AI statement automatically.
- **Transactions:** begin/commit/rollback/manual vs auto, lost connection/unknown COMMIT, deadlock/timeout, uncommitted-close, changes visible only after actual DBMS commit, no retry unknown.
- **Query builder:** 0/1/2 tables, outer joins, quoted names/aliases, null/empty/negative, injection-shaped value, unsupported SQL round-trip refusal; generated SQL pre-visible.
- **ERD:** real FK, composite PK/FK, self-FK, missing referenced object, partial introspection, many tables, pan/zoom, export, saved local layout, read-only no DDL.
- **Charts:** table vs current page vs query result clearly labeled, NULL, outliers, exact decimals, timezone, no false precision; visual export.
- **Data transfer:** 1/100/1e6 rows synthetic bounded streaming, errors mid-batch, conflict destination, Cancel, disk full, unreliable network; no duplicate imports on retry; CSV formula injection protection.
- **Additional providers:** per-server version/role/schema/encoding; precision/identity/generated/constraint/triggers/DDL differences; driver license/security matrix and packaging.
- **Authentication:** TLS invalid CA/host, mutual auth, SSH disconnect/rekey, SSO denial/refresh only for implemented adapters; never force insecure connections.

Каждая строка матрицы FEATURE_MATRIX получает отдельные test IDs/provider scope, иначе статус «Нет/Частично».

## 4. Целевые UX проверки и критерии

### Три реальные аудитории
- **Новичок Excel:** открыть XLSX и сделать безопасную локальную таблицу без SQL, пересортировать и найти строку, понять «куда сохранилось». Не объяснять функцию до старта задачи.
- **Обычный бизнес-пользователь:** подключить готовую БД по выданным параметрам, найти/изменить разрешённые данные, выгрузить результат.
- **SQL-разработчик:** быстро открыть разные источники, запустить SELECT, сравнить результаты, посмотреть FK и исполнение с правильным transaction state.

**User test scorecard:** 5 заданий, completion yes/no, ошибки, количество тупиков/непонятных терминов, время до первого успешного действия, субъективная понятность 1–5. Числовые go/no-go пороги утверждать только по baseline, не выдумывать. Помимо автора пригласить хотя бы 2–3 человека из разных групп до заморозки UI.

### Matrix UI
- Windows 100/150/200%; minimum practical width и ultrawide, обе темы, Simple+Advanced, RU и после реализации EN.
- Навигация мышь/keyboard-only; Tab order, focus ring, Ctrl/Cmd shortcuts; закрытие modal/connection; visible state with tooltips.
- Narrator/аналог ОС: accessible names, row/column context, static/async status announcement, icons and scrollbars.
- Контраст текста ≥ WCAG AA 4.5:1 для обычного текста, significant UI components/focus ≥ 3:1 где применимо; status не только цвет.
- Длинные имена, кириллица, Unicode, bad/empty/default text, 0/NULL/false, disabled and error cases.
- Никаких overlaps и ложных «успешно сохранено». UI thread не выполнять синхронный тяжёлый I/O.
- Animations respect reduced-motion; эффективность таблицы не измерять только красотой screenshot.

### Visual review checklist
Точные сопоставимые screenshots для Welcome / Connections / Table / Filter / CreateTable (few & many fields) / SQL / Structure / Error / Backup in Light & Gray, 100/150/200%. Сопоставлять с **собственным UX_DESIGN**, не пиксельной копией стороннего приложения. Проверить label alignment, line length, radius, spacing, hover/focus, icon baseline, empty states, ellipsis/tooltips and button spacing. Зафиксировать до/после по task code.

## 5. Устойчивость/безопасность

| Риск | Проверка |
| --- | --- |
| Silent data loss/conversion | Loss report before commit; unsupported native types refused or preserved |
| Bad authorization | Permission denied by provider, not only UI button disabled |
| Accidental destructive SQL | Scope, explicit warnings, transaction & backup policy; separate AI gate |
| SQL injection in wizard/filter | AST + identifiers from confirmed metadata + parameters |
| Leaked credentials/tokens | No secrets in logs, profiles, screenshots/public issue artifacts |
| WAL copy corruption | SQLite Online Backup API; integrity/version/hash restore check |
| Concurrent edit and unknown COMMIT | Optimistic guard, no autoretry, reconcile state |
| DoS huge data | Limits, pagination, streaming, progress/cancel, max graph nodes |
| Malicious CSV/XLSX/HTML | Formula-safe CSV export, data-only XLSX parser, no executing embedded HTML/JS |
| Bad cloud status | Local snapshot/remote delivery/download verification are distinct |
| Dynamic theme breakage | No lost selection/edit/SQL/permissions between themes |
| Supply chain | Pin/review NuGet, check OSS license/NOTICE, dependency/security checks |

## 6. Самостоятельная реализация и легальность

- Составить *functional requirement* на основе публичных справок и пользовательских задач; не переносить DBeaver/Eclipse commercial source, binary/resources, оригинальные UI схемы, текстовые ресурсы и иконки.
- Конкретные UX wireframes, component names, wording и brand — **свои**. Факт «дерево слева, таблицы по центру» — обычный industry pattern, а не разрешение на копирование фирменного оформления.
- Код SmartSQL писать самостоятельно на C#/.NET/Avalonia; сторонние пакеты использовать **при совместимых лицензиях**, фиксировать SPDX/NOTICE/зависимости. Apache 2.0 DBeaver Community — не лицензия на proprietary DBeaver Lite. Не обещать юридическую гарантию без counsel.
- Нельзя объявить «полный паритет» пока нет фактов для feature × DBMS × OS × permissions. Приоретизировать честные функции вместо маркетингового 100%.

## 7. Минимальная отчётность каждого PR

```text
Feature ID / WORK_PLAN Mxx / original requirement:
Existing code reused:
Provider and version / permission scope:
Source commit and changed files:
New commands/SQL/writes introduced:
Loss/NULL/precision and recovery impact:
Automated tests executed (command, pass/skip, environment):
Manual UI gates with screenshots (yes/no, exact gaps):
Published EXE/ZIP/installer tested (yes/no):
Unsupported/partial/unknown behavior:
Documentation changed (CAPABILITY_MATRIX, HANDOFF, TASKS/WORK_PLAN):
```

Если tool/sandbox не даёт запустить Windows UI, написать именно **not run**, не «passed» и не автоматически закрывать M02/T23/M11.
