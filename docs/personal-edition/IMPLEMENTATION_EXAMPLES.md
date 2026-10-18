# Практические эскизы реализации — C# / Avalonia

**Важное ограничение:** примеры ниже — **предлагаемые изолированные эскизы**, не существующие API и не проверенный production-код. Имена `Personal*` специально отличаются от текущих, чтобы их нельзя было ошибочно счесть внедрёнными. Перед использованием Codex обязан сверить реальные классы main, обновлённые архитектурные решения, собрать проект, написать негативные тесты и начать с наименьшего вертикального среза. Не вставлять все примеры массово в рабочий продукт.

## 1. Capability catalog — маленькое расширение существующих интерфейсов

Сегодня `IDatabaseBrowser`, `IDatabaseEditor`, `IDatabaseRowCreator`, `IDatabaseRowDeleter` разделены правильно. Добавление action в toolbar разрешать только после явного capability текущей сессии. Не добавлять `CanDoEverything`.

```csharp
// SmartSQL.Application — пример нового независимого контракта.
namespace SmartSQL.Application;

public enum PersonalOperation
{
    ReadTable,
    UpdateCell,
    InsertRow,
    DeleteRow,
    ReadStructure,
    PreviewSqlPlan,
    ExportTable
}

public sealed record PersonalOperationSupport(
    PersonalOperation Operation,
    bool Allowed,
    string? ReasonCode,
    string? HumanExplanation);

public interface IPersonalOperationCapabilities
{
    // Ключевая идея: проверка относится к объекту/сессии, не к названию провайдера.
    Task<PersonalOperationSupport> CheckAsync(
        string connectionId,
        string schema,
        string objectName,
        PersonalOperation operation,
        CancellationToken cancellationToken);
}
```

**Что нужно проверить:** source ID нельзя брать из отображаемого имени соединения; Schema/name идентичность должна соответствовать текущему `DatabaseTableRef`. Желательно адаптировать к существующим типам проекта вместо введения параллельной строковой идентичности. `Allowed=true` — UI hint, а **не security grant**. Provider обязан повторно проверить роль, schema, key, concurrent DDL и row conflict при операции. Тесты: read дозволен/write запрещён; после смены роли кнопку disabled; stale capability → refused; Simple/Advanced не меняет разрешения.

## 2. Безопасная модель визуального SELECT-builder

UI создаёт **типизированный AST**, а Infrastructure знает конкретный SQL dialect. Значение фильтра всегда parameter; identifier выбирается только из подтверждённых metadata. Пример намеренно поддерживает только 1 таблицу/1 сравнение — потом добавить JOIN, grouping и parentheses отдельными кусками.

```csharp
// Application: никакого SQL в контракте намерения.
public sealed record PersonalSelectIntent(
    string Schema,
    string Table,
    string[] ProjectColumns,
    PersonalEqualsFilter? Where,
    int Limit);

public sealed record PersonalEqualsFilter(string Column, string? Value);

// Infrastructure/PostgreSQL: демонстрация только SELECT с одним параметром.
public static class PersonalPgSelectRenderer
{
    public static Npgsql.NpgsqlCommand Build(
        Npgsql.NpgsqlConnection connection,
        PersonalSelectIntent intent,
        IReadOnlySet<string> confirmedColumns,
        IReadOnlySet<string> confirmedTables)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (intent.Limit is < 1 or > 500) throw new ArgumentOutOfRangeException();
        if (intent.ProjectColumns is null || intent.ProjectColumns.Length == 0)
            throw new ArgumentException("Select at least one column");
        var qualifiedName = intent.Schema + "." + intent.Table;
        if (!confirmedTables.Contains(qualifiedName))
            throw new InvalidOperationException("Unconfirmed table");
        if (intent.ProjectColumns.Any(c => !confirmedColumns.Contains(c)) ||
            (intent.Where is not null && !confirmedColumns.Contains(intent.Where.Column)))
            throw new InvalidOperationException("Unconfirmed column");
        if (intent.ProjectColumns.Distinct(StringComparer.Ordinal).Count() != intent.ProjectColumns.Length)
            throw new ArgumentException("Duplicate projection");

        static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
        var sql = "SELECT " + string.Join(", ", intent.ProjectColumns.Select(Q)) +
                  " FROM " + Q(intent.Schema) + "." + Q(intent.Table);

        var command = connection.CreateCommand();
        if (intent.Where is { } filter)
        {
            // Null и пустая строка — разные логические операции.
            if (filter.Value is null)
                sql += " WHERE " + Q(filter.Column) + " IS NULL";
            else
            {
                sql += " WHERE " + Q(filter.Column) + " = @value";
                command.Parameters.AddWithValue("value", filter.Value);
            }
        }
        // Предел согласован с прикладной валидацией; metadata/identifier
        // limits, native type params и server permissions проверяются отдельно.
        command.CommandText = sql + " LIMIT " + intent.Limit;
        return command;
    }
}
```

**Для production обязательно добавить:** корректную catalog/table identity вместо конкатенированного ключа; PostgreSQL exact native types при параметризации (не все значения строки), quote limits/Unicode, query cancellation, resource limits, read-only transaction, server version, table/view scope, no implicit schema search path, readonly enforcement. Никогда не передавать в `WHERE` произвольный текст человека или от модели. Тесты со столбцами `"A\"B"`, reserved keywords, Cyrillic, empty string, `null`, injection-shaped values, stale metadata. Builder **не** пытается round-trip произвольный SQL через regex.

## 3. Состояния изменяющей ручной SQL-операции

`IReadOnlySqlExecutor` не менять на API неограниченных запросов. Новый manual advanced contract — отдельно:

```csharp
namespace SmartSQL.Application;

public enum PersonalSqlOutcomeKind
{
    Completed,             // Сервер подтвердил операцию.
    RolledBack,            // Подтверждён rollback.
    FailedBeforeExecution, // Ничего не отправлено.
    Conflict,              // Требует reload/reconcile.
    UnknownCommit          // Запрос мог быть исполнен.
}

public sealed record PersonalSqlOutcome(
    PersonalSqlOutcomeKind Kind,
    long? RowsAffected,
    string MessageCode,
    string? SafeExplanation);

public interface IPersonalManualSqlSession : IAsyncDisposable
{
    // Создавать только через явный advanced workflow & configured provider.
    Task<PersonalSqlOutcome> RunUserSqlAsync(
        string userSql,
        CancellationToken cancellationToken);

    Task<PersonalSqlOutcome> CommitAsync(CancellationToken cancellationToken);
    Task<PersonalSqlOutcome> RollbackAsync(CancellationToken cancellationToken);
}
```

**Требования:** конкретная transaction/session state machine (Idle/Executing/InTransaction/Committing/Unknown/Closing), connection ownership, sequential execution, explicit autocommit policy, close prompt, release/cancel/drain, sensitive log policy. После `UnknownCommit` UI запрещает «повторить», предлагает перечитать объект/серверную историю. Не пытаться определять DML regex по первому слову. Сервер/драйвер определяет настоящую атомарность и права, а не наш enum.

## 4. Визуальное «Да/Нет / Не задано»

В текущем `DatabaseEditValueValidation.Normalize` Boolean умеет распознавать «Да/Нет», но preview.9 сообщает отсутствие удобного editor в UI. Это иллюстрация presentation conversion, а не исправление UF09-05:

```csharp
public enum PersonalBooleanChoice { NotSet, Yes, No }

public static class PersonalBooleanChoiceMapper
{
    public static string? ToNativeInput(PersonalBooleanChoice choice, bool nullable) =>
        choice switch
        {
            PersonalBooleanChoice.Yes => "true",
            PersonalBooleanChoice.No => "false",
            PersonalBooleanChoice.NotSet when nullable => null,
            PersonalBooleanChoice.NotSet => throw new InvalidOperationException(
                "Поле не допускает пустое значение."),
            _ => throw new ArgumentOutOfRangeException(nameof(choice))
        };
}
```

Для уже реализованного native editor надо передать результат **через действующую provider validation**, не напрямую в SQL. Тестировать записи true/false/null отдельно и reopen, отказ nonnullable null, keyboard navigation, disabled in read-only. Local managed semantic `NULL/empty/whitespace` не переписывать этим change.

## 5. Avalonia: семантические цвета, не hard-coded «красивый экран»

Уже есть `Styles/Tokens.axaml`. Следующий XAML только демонстрирует стиль уже существующей палитры и адаптивность; не создавайте вторую иконографию:

```xml
<!-- Фрагмент нового view. В существующий UI интегрировать после аудита. -->
<Border Background="{DynamicResource UiSurfaceBrush}"
        BorderBrush="{DynamicResource UiSoftBorderBrush}"
        BorderThickness="1"
        CornerRadius="{DynamicResource UiCardRadius}"
        Padding="16">
  <StackPanel Spacing="12">
    <TextBlock Text="Открыть базу данных"
               FontWeight="SemiBold"
               Foreground="{DynamicResource UiTextBrush}" />
    <TextBlock Text="SQLite-файл или подключение к серверу"
               TextWrapping="Wrap"
               Foreground="{DynamicResource UiMutedTextBrush}" />
    <Button Content="Выбрать источник"
            AutomationProperties.Name="Выбрать источник данных" />
  </StackPanel>
</Border>
```

**Не** маскировать системные кнопки окна custom chrome без отдельного решения: действующая native frame возвращена по просьбе пользователя. Footer диалога с cancel/confirm разместить **вне scroll container**, чтобы UF09-04 не повторился. `DynamicResource` позволяет теме смениться без потери UI state. Хоткеи с платформенными различиями через command abstraction; `AutomationProperties` и keyboard focus test.

## 6. Узкий UI-only порядок столбцов

Данные остаются связанными со стабильными Field IDs/Native identifiers. View order записывается в local nonsecret preferences, физическая схема не изменяется:

```csharp
public static IReadOnlyList<Guid> PersonalReorderVisibleFields(
    IReadOnlyList<Guid> current,
    Guid dragged,
    int insertIndex)
{
    ArgumentNullException.ThrowIfNull(current);
    if (current.Distinct().Count() != current.Count ||
        !current.Contains(dragged))
        throw new ArgumentException("Invalid view columns");
    var result = current.Where(id => id != dragged).ToList();
    if (insertIndex < 0 || insertIndex > result.Count)
        throw new ArgumentOutOfRangeException(nameof(insertIndex));
    result.Insert(insertIndex, dragged);
    return result;
}
```

Для native — не вводить synthetic ID, который может совпасть после ALTER: source/table/column/version identity и invalidation. Тест: перестановка не меняет порядок/значения физической БД, не перемещает запись, не требует ALTER permission; keyboard move controls fallback drag&drop.

## 7. UI selection vs provider operation

Неправильно: `UpdateRow(selectedRowIndex, text)` — индекс после сортировки/фильтрации нестабилен. Правильно: использовать уже существующий `DatabaseCellUpdate(Table, EditHandle, ColumnName, Value)` для подтверждённых PG/native SQLite редакторов. Не кэшировать EditHandle в local global history и не повторять после unload. Поддержка «Undo» для server DB требует отдельной reversible action с проверкой текущей версии и прав.

Псевдо-алгоритм кнопки:

```text
1. Identify active session/source/object + selected provider handle.
2. Query provider-confirmed capability (UI gate).
3. Type-specific edit, normalized preview and current-source label.
4. Request explicit permission only for this session/action where required.
5. Provider revalidates identity/schema/current values/rights.
6. Execute guarded write once. Observe COMMIT vs unknown outcome.
7. Consume edit handle; reload page; report applied-vs-refresh-failed separately.
8. On conflict or uncertain COMMIT, no automatic retry or optimistic success.
```

## 8. Import wizard без скрытой потери данных

Использовать существующие `CsvImportPreview`, `CsvNewTablePreview`, `XlsxTableTransfer` и managed preview. **Не** писать отдельный CSV/XLSX parser ради UX. Proposed state machine:

```text
PickSource → InspectFormat/Sheets → MapHeadersAndColumns → ValidateRows
→ PreviewSourceVsDestination (loss report) → ConfirmDestinationPath
→ ValidateBackup/AtomicTemp → Commit/Publish → Verify/ShowLocation
       ↘ Cancel/Failure → DeleteOwnTemp, preserve source and target
```

Типы: `00123` как текст; огромный numeric не «приблизительно тот же»; timezone preserved or warning; XLSX formula cell cannot be treated as executed code. Не произвольный silent overwrite. Data-only scope и исходный Excel сохраняются.

## 9. ERD: рендерить *только* верифицированную структуру

Опора на `DatabaseStructure.Constraints`, `DatabaseForeignKey` и `DatabaseMetadataCoverage`. Если FK coverage `Partial`/ `Unavailable`, на диаграмме показать «связи могут быть не полными», а не утверждать отсутствие связей. Layout/user node positions хранить отдельно от native schema. Исключить визуальные fake FK по совпадающим именам колонок.

```csharp
// Псевдокод: render source is object-owned metadata only.
// Для полного графа нужен безопасный bounded crawl других таблиц.
var confirmedForeignKeys = structure.Constraints
    .Where(c => c.Kind == DatabaseConstraintKind.ForeignKey &&
                c.ForeignKey is not null)
    .Select(c => (Source: structure.Table, Target: c.ForeignKey!.ReferencedTable))
    .ToList();
```

Здесь `Target` только отображаемое имя, не уникальный идентификатор; реальную схему/каталог и quoted identifiers надо разрешать точно.

## 10. Вычисления и графики: не подменять точность визуализацией

```text
SQL Result / Table Snapshot (origin, timestamp, page/scope)
  → Type-aware columns & null/missing diagnostics
  → Validated aggregate in decimal/native numeric or provider SQL
  → Bounded ChartData with format/unit and numerical precision warning
  → Plot renderer
```

Для `numeric(38,20)` нельзя незаметно преобразовать в IEEE 754 и объявить сумму точной. Bar/line/pie/count — отдельные пользовательские сценарии, тестировать empty/NULL/negative/very-large values, label collision, export.

## 11. Как Codex превращает рецепт в настоящий PR

Перед конкретным срезом:
1. `git status`, прочитать `AGENTS.md`, `docs/HANDOFF.md`, `docs/WORK_PLAN.md`, `docs/DECISIONS.md`, этот пакет.
2. `rg` реальных существующих классов, тестовых fixtures и стадий реализации.
3. Небольшое специфицированное изменение в 1–3 существующих компонентах, если возможно; не создавать новые `Personal*` API без необходимости.
4. Unit + property/integration tests с синтетическими БД на разрешённом D:, без remote production данных.
5. `scripts/Build.ps1`, `scripts/Test.ps1`; затем ручная UI-приёмка и 100/150/200%.
6. Документировать exact source SHA, tests counts, manual status и ограничения; не называть скетч/PR завершённой feature.
