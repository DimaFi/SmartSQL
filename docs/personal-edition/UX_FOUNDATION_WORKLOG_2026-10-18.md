# UX Foundation — первая кодовая итерация (18.10.2026)

**Статус:** кандидаты на интеграцию в двух независимых PR. Ничего из перечисленного не считается готовым release-функционалом до Windows build/xUnit и ручной UX-приёмки. **Не объединять неподтверждённый код автоматически и не закрывать UF09.**

## Состояние до изменений

- `main` исходно `036305b0da05687ca5f67aa3ea93373ebd903e64`, docs/Personal blueprint, M01.04 runtime с прежним 771 tests checkpoint.
- Открыты M01.04 manual gate / M01.05, шесть UF09 замечаний. Данные и продуктовые root/storage условия не менялись.
- Среда GitHub редактирования **не имеет** .NET 10 SDK, Windows/Avalonia runtime и пользовательского D:; автотесты и UI здесь **не запускались**.

## Кандидат A — UF09-03/04 (PR #30)

[Pull Request #30](https://github.com/DimaFi/SmartSQL/pull/30)

**Изменено:** `src/SmartSQL.Desktop/Views/CreateTableDialog.cs`, новый `FieldRowOrdering.cs`, `tests/SmartSQL.Desktop.Tests/FieldRowOrderingTests.cs`.

1. У каждого добавляемого поля есть кнопки ↑ и ↓ для перестановки; они недоступны на границах, одна оставшаяся строка защищена.
2. `FieldRowOrdering.MoveByOne` меняет порядок в коллекции, а представление синхронизировано с `_rows`, из которой `CreateTable` формирует `FieldInput[]`.
3. Полосе прокрутки оставлен отступ 18 px; изменён размер диалога. Действительное отсутствие overlap **НЕ** доказано без screenshot.
4. Есть семантические accessible labels и подсказки действий по полю; обновляются при вводе/перемещении.
5. **Не реализовано:** drag/drop физических или уже созданных столбцов. Изменение порядка в новом wizard — только **до** создания, не DDL существующей таблицы.

**Обязательная приёмка:**
- `scripts/Build.ps1` / `scripts/Test.ps1` на D:, ошибки/warnings/trx сохранить.
- Новая таблица A/B/C: переместить C наверх, создать, закрыть/открыть, структура и значения в ожидаемом порядке, никаких потерянных полей.
- Удаление, добавление, смешанные перемещения; перенос с клавиатуры, доступные названия кнопок и disabled state при одном поле.
- Длинный список 5/20/100 полей, small window, Light/Gray, Windows scale 100/150/200%; полоса не перекрывает кнопки.
- Проверить нормальный viewport и клики remove на последней строке, вертикальную/горизонтальную прокрутку.

## Кандидат B — SQL-find (PR #31)

[Pull Request #31](https://github.com/DimaFi/SmartSQL/pull/31)

**Изменено:** новый `Queries/SqlTextSearch.cs`, `Views/DatabaseQueryView.cs`, `tests/SmartSQL.Desktop.Tests/SqlTextSearchTests.cs`.

1. Поиск буквального текста SQL без regex, с учётом case-insensitive и UTF-16 индексов, next/back + wrap.
2. Панель Ctrl+F, Enter/Shift+Enter, F3/Shift+F3, Escape; поиск не исполняет и не изменяет SQL.
3. Текущий `IReadOnlySqlExecutor`, SQL query history/results, Db permissions и provider logic не затронуты.

**Обязательная приёмка:**
- Build/Test, корректность search offsets на длинном SQL с emoji/CRLF/кавычками/многострочными комментариями; Ctrl+F/Enter/Shift+Enter/F3/Escape.
- Проверить фокус поиска, `Ctrl+Enter` по редактору по-прежнему выполняет обычный *read-only* SQL, а при фокусе в поисковом поле — не выполняет.
- Смена Simple/Advanced и темы сохраняет SQL, текущее выделение/результат.
- Операция SQL in progress + Cancel/close, табы одного и разных соединений, DPI/keyboard/Narrator и отсутствие accidental execution.

## Следующие работы после проверок

1. После acceptance merge подходящих PR отдельно в `main`, read-back exact remote SHA, обновление WORK_PLAN/HANDOFF/CAPABILITY_MATRIX/USER_FEEDBACK с доказательствами.
2. **UF09-05:** воспроизвести локальную Boolean-ячейку, подключить удобный input Да/Нет/(NULL по типу), не менять серверный SQL без основания.
3. **UF09-01/02:** ясные названия проектов/подключений, onboarding, без скрытого создания новых DB; это отдельные M03/M10 срезы.
4. **UF09-06:** icon Undo и отдельная History *только* с честными границами отката.
5. **UF09-03 продолжение:** visual reorder столбцов уже открытой таблицы — отдельная local view preference, не физический ALTER.
6. M01.05 storage migration строго после результата пользовательской M01.04 приёмки.

**Ветка с описанием не является подтверждением исправления.** Статусы остаются открытыми, пока реальные тесты и UI не прошли.
