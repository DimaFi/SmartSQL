# M01.04–M01.06: сценарии определения root и обработки отказов

> **Интеграционная пометка 17.10.2026:** reference/proposal, не новая принятая архитектура и не реализованные функции. Использование, перепроверка источников и ограничения — [реестр интеграции](BRANCH_INTEGRATION_2026-10-17.md). WORK_PLAN / DECISIONS остаются источниками текущего порядка и решений.

> **Статус:** проект проверяемого контракта для Codex, **не принятого нового ADR**. Источник истины — [PRODUCT_STORAGE](../PRODUCT_STORAGE.md) / D056 и [WORK_PLAN](../WORK_PLAN.md). Исходники не изменены; результаты тестирования далее — **ожидаемые**, не фактические. Base main на 17.10.2026: `7e6aa2c71ec725d9ca76976f0268b823b2394ffc`.

## Инварианты, которые нельзя нарушать

- **I01** Любой доступ к `smartsql.db`, `connections.json`, `workspace-preferences.json`, `theme-preferences.json`, `queries/history.json` и пользовательским backup начинается только после успешного выбора **одного** process-stable root.
- **I02** Ошибка выбора, несовпадение идентичности или отказ IO **не** заменяется новым хранилищем; не открываются и не создаются управляемые таблицы.
- **I03** `SMARTSQL_DATA_DIRECTORY` присутствует, даже если пустая строка, означает обязательный override с отказом при ошибке; не разрешается переключение на portable/installed.
- **I04** Portable marker — явный выбор, не признак ZIP/EXE; относительный путь интерпретируется **только** относительно каталога самого marker, а не CWD.
- **I05** Граница root — конфигурация локального состояния, не граница выбора внешних .db и не разрешение копировать/менять внешнюю БД.
- **I06** Неизвестный/повреждённый/будущий маркер, неизвестный непустой каталог или известные legacy-данные блокируют создание нового default.
- **I07** Root marker/bootstrap пишутся без перезаписи чужого файла; конкурирующие процессы не перетирают выбор друг друга.
- **I08** Debug/dev/test/preview используют отдельный явный D:-root: запрещено создавать installed bootstrap/profile в реальном пользовательском профиле разработчика.
- **I09** Ранний отказ отображается понятной ошибкой при запуске; ошибку нельзя спрятать в лог без UI, изменить prefs, попытаться CreateNew или silently choose another root.
- **I10** Ни legacy SQLite, ни ordinary native SQLite, ни активный WAL не перемещаются/модифицируются в M01.04; это отдельная проверяемая M01.05.

## Двухфазное моделирование, не проект собственной СУБД

```text
  ResolveCandidate(env present?, portable file?, installed bootstrap?, known legacy?)
            |
            +--> INVALID --> StartupFailure (no data read/write)
            |
            +--> LEGACY_FOUND/AMBIGUOUS --> SelectExisting/M01.05 needed (no init)
            |
            +--> EXISTING_CHOICE --> ValidateFolderAndRootId --> ResolvedRoot
            |
            +--> FIRST_USE_ONLY --> PrepareUnclaimedRoot
                                         |
                                         +--> durable root marker (no overwrite)
                                         +--> validate completed root
                                         +--> atomic bootstrap no-overwrite
                                         +--> reread root marker/bootstrap
                                         +--> ResolvedRoot
```

Это **иллюстрация требуемых свойств**, а не указание реализовать SQL-подобный transaction manager. Детали fsync/flush, rename durability, lock sharing, ownership/rollback и NFS/reparse должны быть установлены реализацией на поддерживаемом Windows NTFS. `File.Move` и `Path.GetFullPath` сами по себе не доказывают полной устойчивости к отключению питания/TOCTOU.

### Особые состояния

| Наблюдаемое состояние | Обязательное поведение | Контроль |
| --- | --- | --- |
| Env отсутствует, portable отсутствует, bootstrap отсутствует, legacy отсутствует и root ещё пуст | First-use, один rootId, без создания user DB | T01 |
| Env присутствует, но `""`, пробелы, relative, malformed | Ошибка env с указанием имени; installed/bootstrap не читается и не создаётся | T02 |
| Portable marker есть, schemaVersion future/JSON damaged/relative escapes marker folder into app bin | Отказ/нужен ручной выбор; никакого installed fallback | T03 |
| Installed bootstrap есть, выбранный том исчез | Показать старый путь/Retry/выбрать *существующее* хранилище/отмена; не создать новый | T04 |
| Bootstrap rootId != `storage-root.json` | Ошибка идентичности, без автоматической замены marker/root | T05 |
| Root marker создан, но bootstrap не опубликован после crash | **Сирота**: явное recovery/choose existing, не автоматически принять как новый root. Проверить наличие user data перед предлагаемой повторной инициализацией | T06 |
| Bootstrap появился, root marker отсутствует/corrupt | Fail closed, без auto-regeneration/auto-adoption | T07 |
| Параллельно два first-use процесса | Один подтверждённый root; другой перечитывает победителя либо отказывает без расхождения | T08 |
| Прямой EXE vs shell shortcut vs launcher/обновлённая сборка | Одинаковый rootId и набор prefs/history (кроме намеренно явного dev override) | T09 |
| Legacy `app/data/local` и `run-data/local` оба содержат файлы | No winner by time, explicit selection. Не мигрировать внутри M01.04 | T10 |
| Root path `C:\`/root volume, app version dir, файл, `.db`/WAL/SHM, junction, UNC | Fail closed; app не создаёт данных | T11 |
| ReadOnly/no space/denied temp directory/никакого D: на продуктовом ПК | Ясный ранний отказ (либо установленный product root без D: в разрешённой изолированной среде); dev policy не ослаблена | T12 |
| `SMARTSQL_TEMP_DIRECTORY` присутствует, но относителен | Явная ошибка; не писать в process/system temp как резервный fallback | T13 |
| Повторная инициализация и вызовы `WorkspacePaths.DataDirectory` | Не меняют выбранный root в течение процесса; без новых probe side effects на каждое свойство | T14 |
| Внешняя SQLite/PG уже выбрана пользователем | Root не переписывает путь базы, provider permissions остаются прежними | T15 |

## Протокол проверки для Codex

1. Сначала сделать **табличную проверку precedence** без файловой системы: обрабатывать presence env отдельно от string value, version-bound JSON и явно нормализованный candidate. Логи/исключения без чувствительных значений.
2. Затем маленькую **filesystem boundary** с injectable path/provider и разрешённым D:-тестовым root, иначе auto-test может писать в профиль CI/разработчика.
3. **Fail injection** после каждого собственного write/rename/flush, включая токены concurrency и занятое имя; сравнить список созданных файлов и SHA legacy данных. Никакое исключение не должно превращаться в successful empty default.
4. **Два процесса** тестировать реально (а не два потока под одним lock); check bootstrap/rootId и что loser не создаёт альтернативный active root.
5. **Entry-point parity** отдельно проверить внутри релизной Windows VM: с `SMARTSQL_DATA_DIRECTORY` **отсутствующей**, not dev D:-override. Проверка без записей в реальный профиль разработчика.
6. Проверить real-source smoke `WorkspaceWindow`: имя/путь из resolver подхватывают `SqlQueryHistoryStore`, `ThemePreferences`, `JsonWorkspacePreferencesStore`, `JsonConnectionProfileStore`, native prewrite backups. Приложение не должно инициализировать старый default из UI до resolver.
7. После M01.04 **не закрывать**: copy-first legacy migration, concurrent external writes, symlink races, reinstall/uninstall/downgrade, Windows min-size/DPI/Narrator.

## Кодовые места, подтверждённые чтением

- [WorkspacePaths.cs](../../src/SmartSQL.Desktop/WorkspacePaths.cs): `DataDirectory` сейчас вычисляется при каждом запросе и создаёт каталог/write probe; default зависит от AppContext.BaseDirectory/workspace.
- [Program.cs](../../src/SmartSQL.Desktop/Program.cs): `InitializeRuntime()` вызывается до старта Avalonia, без раннего UI catch.
- [Run-Published.ps1](../../scripts/Run-Published.ps1): собственный `run-data/local` default, отдельный от EXE; **сценарий исторических данных**.
- [App.axaml.cs](../../src/SmartSQL.Desktop/App.axaml.cs): тему загружает во время Initialize, а затем создаёт WorkspaceWindow; root resolution должен произойти раньше.
- [WorkspaceWindow.cs](../../src/SmartSQL.Desktop/Views/WorkspaceWindow.cs): SQL history, theme/mode и profiles завязаны на `WorkspacePaths.DataDirectory`; проверять side effects.
- [MANAGED_PROJECT_OPENING](../MANAGED_PROJECT_OPENING.md): `CreateNew` и `OpenExisting` различны; предыдущая M01.02 копия и WAL validation не отменяются.

**Возможные открытые решения, без фиксации ADR:** механизм Windows lock/lease и commit двух файлов, представление действий recover для `orphan root`, границы распознавания legacy при standalone ZIP с несколькими versions. Parent/Codex должны принять конкретный механизм до кодового изменения; при необходимости запись в `DECISIONS.md`. Этот документ не даёт права скрытно создать новый volume/root.