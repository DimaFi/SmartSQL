# UX Foundation: нативный Boolean без ручного текстового ввода

Дата: 18 октября 2026. Статус: **кандидат на UI-приёмку, не релиз**. Эта работа расширяет UF09-05 для **native cell UPDATE** и дополняет [managed Boolean PR #32](https://github.com/DimaFi/SmartSQL/pull/32).

## Исходный код и границы

- `src/SmartSQL.Desktop/Views/DatabaseCellEditDialog.cs`: прежнее текстовое поле показывается для всех native типов. Provider уже возвращает `EditableDatabaseColumn.Kind`, `IsNullable` и `CanEdit`; `DatabaseEditValueValidation.Normalize` поддерживает true/false/NULL с точной проверкой прав.
- Новый **DatabaseBooleanChoices**: `Да → "true"`, `Нет → "false"`, `Не задано → null`; последняя опция есть только при `IsNullable=true`. Неизвестная native representation не преобразуется ни в false, ни в NULL.
- Для `Kind == Boolean` диалог показывает ComboBox вместо TextBox/checkbox NULL; исходное native значение и preview/confirmed one-cell write **сохранены**. Для других native типов путь прежний.
- Выбор проходит через текущий **DatabaseEditValueValidation.Normalize**; неизвестная опция блокирует Save. Если число/строка Boolean семантически не изменилась (`1` vs `true`), диалог не отправляет повторный UPDATE.
- Нет новых raw SQL, регистрации ролей, DML runner, batch writes, миграции БД или изменения runtime root. Разрешения/identity/row snapshot/transaction остаются провайдерными.

## Acceptance — обязательно выполнить на Windows

1. Прочитать [WORK_PLAN](../WORK_PLAN.md), [USER_FEEDBACK_PREVIEW9](../USER_FEEDBACK_PREVIEW9.md), проверить `M01.04` acceptance. На разрешённом D: выполнить `scripts/Build.ps1`, `scripts/Test.ps1`; отчёт с SHA и реальными результатами.
2. Реальный PostgreSQL fixture с `boolean` nullable/nonnullable и подтверждённым keyed UPDATE: исходные TRUE/FALSE/NULL, выпадающие значения, reopen/reload, round-trip и no-op, actual server values. Синтетическая БД/другая роль для read-only. `false` ≠ `NULL`.
3. Провайдер SQLite: только если он подтвердил `DatabaseEditValueKind.Boolean`. Без такой capability применять generic native editor как раньше, не пытаться угадать boolean по имени столбца.
4. Нераспознанный representation (`t`, `f`, иное) приводит к пустому выбору/явному предупреждению, **не** к false. При выборе «Да»/«Нет» normalizer выполняет старую проверку.
5. Переход столбцов разных типов в одном диалоге: Boolean → Text → Boolean / nullable → nonnullable; toggle контрола не подтягивает значение из прежней колонки. Invalid/readonly columns не дают Save.
6. Включённый/запрещённый provider UPDATE, stale handle/DDL-conflict/unknown commit, failed refresh after success: никакого автоматического повтора или ложного успеха.
7. Keyboard-only, focus, ComboBox и tooltips, две темы, окно minWidth/scroll, DPI 100/150/200%, screen reader.

## Автотесты в PR

`tests/SmartSQL.Desktop.Tests/DatabaseBooleanChoicesTests.cs`: 8 native presentation spellings, nullable option, required option rejecting NULL, unknown input, foreign selection rejection, readonly guard, non-Boolean error.

**Build/xUnit и ручные тесты ещё не выполнены** в GitHub editing environment: .NET SDK и Windows UI здесь отсутствуют. Не закрывать UF09-05 без проверок; managed и native срезы надо проверять независимо перед интеграцией.
