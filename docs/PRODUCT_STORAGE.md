# Хранение данных продукта — M01.03 / D056

Статус 17 октября 2026: **D056 принято; M01.04 runtime подключён, код/автотесты готовы, ручная приёмка открыта**. [Фактическая реализация и 771-test evidence](RUNTIME_STORAGE_M0104.md). M01.04 реализует разрешение путей/запуск, M01.05 — copy-first migration, M01.06 — расширенную failure/race matrix. Это не новая очередь вместо WORK_PLAN. Последний кодовый результат M01.04: 771 tests; новая preview.9 подготавливается для пользовательской приёмки. Сама policy не подтверждает ручную приёмку/новый installer.

## Проблема и границы

Сейчас `WorkspacePaths` без env выбирает `<workspace>/data/local` либо `<EXE>/data/local`. `Run-Published.ps1` выбирает `<build>/run-data/local` (или `<extracted app>/run-data/local`). Installer shortcuts запускают EXE напрямую. Один payload поэтому может открывать разные базы/preferences/history; обе папки также могут зависеть от номера сборки. Это подтверждено чтением кода, не новой экранной проверкой.

Цель: выбранное пользовательское хранилище переживает замену бинарников. Root — каталог локального состояния SmartSQL, **не единственное допустимое место проектов**. Открытая внешняя SQLite/managed база остаётся по явно выбранному пути; PostgreSQL остаётся на своём сервере. Root не переносит внешние файлы автоматически и не заменяет DBMS permissions.

Все действия разработки/тестов/preview этой сессии остаются на D:. Пользовательский product default ниже не разрешает агенту создавать данные на C:, менять профиль Windows, установленные инструменты или глобальные environment settings.

## Выбранная политика Windows

| Режим | Выбор данных | Обновление и назначение |
| --- | --- | --- |
| Обычный пользовательский запуск EXE/ярлыка/ZIP | Known Folder LocalApplicationData + `SmartSQL/data`; при сохранённом выборе — выбранный root из bootstrap | Путь не зависит от версии/EXE/current working directory. ZIP/self-contained сам по себе не означает переносимое хранилище |
| Явный portable | `smartsql.portable.json` рядом с EXE задаёт `schemaVersion:1` и `dataDirectory` | Absolute path либо relative к каталогу marker. Это осознанный режим без записи installed bootstrap. При переносе всей portable-папки relative path сохраняет смысл |
| Явный override / тест / разработка | Непустой absolute `SMARTSQL_DATA_DIRECTORY` | Значение сохраняет текущий смысл корня, не получает скрытый суффикс. Не меняет обычный bootstrap. Dev scripts задают D:-путь; тесты используют отдельный D:-root |

В Windows путь LocalApplicationData получают системным API/.NET SpecialFolder, не сборкой `C:\Users\...`. Такое размещение не требует диска D: у конечного пользователя. Установка бинарников и хранение данных — независимые решения; текущий D:-ограниченный installer ещё не продуктовый installer для любого ПК.

Для installed выбора небольшой bootstrap находится в `LocalApplicationData/SmartSQL/storage.json`: `schemaVersion:1`, `dataDirectory` (absolute), `rootId` (GUID). Сам root содержит `storage-root.json` с `schemaVersion:1` и тем же `rootId`. Это метаданные расположения, не секрет/права доступа и не новая версия managed DB. Повреждённый/будущий формат и несовпадение rootId не перезаписываются. Bootstrap сохраняется атомарно только после проверки root; при сбое старый выбор сохраняется. Автоматический backup bootstrap сам по себе не восстанавливает БД.

Portable marker — только явный opt-in, не поиск любых JSON/баз по родительским каталогам. RootId в root позволяет распознать ранее выбранное хранилище; portable не использует installed bootstrap. Relative path допускается только для portable marker; он нормализуется относительно marker и проверяется теми же guard-правилами. Нельзя молча менять relative path при обновлении: нужно сохранять portable-контейнер целиком или явно выбрать прежний root. Root не располагается внутри заменяемого app/version каталога. Для отдельной версии с другим относительным расположением нужен явный прежний stable root; угадывание по имени папки запрещено.

Обычный пользователь может явно выбрать другой локальный root позже. Смена root с переносом данных — отдельная проверяемая операция M01.05, а не редактирование preferences на лету. Uninstall по умолчанию оставляет пользовательские root/bootstrap/внешние проекты. Удаление данных требует отдельного явного действия и не является шагом обновления.

## Один resolver и порядок запуска M01.04

1. До обращения к default DB/preferences/history определить и зафиксировать расположение один раз за процесс. Сам выбор кандидата — чистая функция; создание/проверка каталога — отдельная стадия. Не вычислять root заново для каждого свойства.
2. Если `SMARTSQL_DATA_DIRECTORY` **присутствует**, разобрать его как обязательный absolute override. Пустое/relative/неверное значение — ошибка, а не переход к следующему кандидату. `SMARTSQL_TEMP_DIRECTORY` сохраняет explicit absolute semantics; dev всё равно проверяется на D:.
3. Иначе, при наличии portable marker рядом с EXE разобрать его. Повреждение/unsupported version/недоступное назначение — ошибка. Не переходить незаметно к installed mode.
4. Иначе прочитать installed bootstrap. Существующий выбор должен существовать и совпадать с rootId; пропавший диск/каталог не создаётся заново. Это не first run. Показать выбранный путь и действия повторить/выбрать существующее/восстановить/отменить.
5. При отсутствии bootstrap выполнить ограниченное обнаружение **известных legacy-кандидатов**: текущие `<EXE>/data/local`, соседний прежний `run-data/local`, явно переданный пользователем прежний путь. Не рекурсивный обход дисков/профилей. Любой legacy-кандидат с данными блокирует тихое создание default. M01.04 может показать необходимость миграции/выбора; M01.05 выполняет копирование. Несколько кандидатов не выбирать по mtime/размеру. Не объединять их.
6. Только при действительно новом выборе без найденного legacy-состояния разрешено инициализировать новый root с no-overwrite marker, проверить возможность работы и атомарно записать bootstrap. Инициализация root не равна созданию/открытию БД; для базы действуют CreateNew/OpenExisting и будущий onboarding M03.
7. Одинаковый выбранный режим/настройки должны давать одинаковый root у EXE, ярлыка, Start/run script и после обновления. Script не содержит собственного второго default: он передаёт только явный override либо вызывает обычный resolver. Нельзя исправлять EXE/launcher различие глобальной переменной среды пользователя.

Для development сценария `Initialize-Workspace.ps1`/Run.ps1 продолжают задавать D:-override. Обычный debug из checkout без env должен распознать development-контекст и использовать D:-workspace policy либо явно отказать до пользовательского installed bootstrap. Product release не ищет `SmartSQL.sln` как способ выбора данных. Preview launch acceptance запускается с явным synthetic D:-root; product-default отсутствие D: проверяется в изолированной разрешённой среде, не записью в профиль разработчика.

Если явный root уже содержит legacy flat layout, он не считается новым только из-за отсутствия marker. Не дописывать rootId и не создавать новую БД до решения M01.05. Empty explicit root можно инициализировать; существующий неизвестный непустой каталог нельзя автоматически объявить хранилищем. Root/base/current directory ошибки должны быть доступны до создания WorkspaceWindow через минимальный startup error flow; прежний запуск без UI и необработанного исключения недостаточен.

## Содержимое и сохранность

Начальный контракт сохраняет текущие имена, без массового переезда внутренних файлов:

| Под root | Назначение / статус |
| --- | --- |
| `storage-root.json` | Идентичность root; новый M01.04 marker, не backup данных |
| `smartsql.db` | Существующий default managed filename; открывать только явно подтверждённый файл, не создавать как fallback |
| `pedagogical-practice-demo.db` | Нынешний demo; отдельный от реальных пользовательских проектов |
| `workspace-preferences.json`, `theme-preferences.json` | Текущие отдельные режим/тема; переносятся явно, ошибки не обнуляются |
| `connections.json` | Нынешние несекретные profiles; не считать безопасным экспортом без проверки содержимого |
| `queries/history.json` | Нынешняя bounded local SQL history; приватные данные, не training corpus, не default cloud payload |
| `backups/native-sqlite/` | Нынешние обязательные prewrite copies, сохраняются после обновления |
| `snapshots/`, `catalog/`, `outbox/` | **Будущие M05**. Durable snapshot/state, не cache. Отдельный формат/atomic protocol будет спроектирован перед реализацией |
| `temp/`, `cache/`, `dotnet-bundles/` | Пересоздаваемые runtime artifacts. Только собственные временные файлы подлежат очистке; не удалять pending snapshot/upload/restore по маске |

Новые именованные проекты по-прежнему могут сохраняться в выбранную пользователем папку. Before-upgrade копия M01.02 пока остаётся рядом с исходным файлом; M01.03 её не перемещает. Нельзя обещать переносимость всех native prewrite artifacts либо включать их в restore без отдельного контракта.

Токены Google/Яндекс/DB credentials и recovery keys не должны становиться plaintext bootstrap/portable files. Реализация secret store/encryption/recovery — DEC-KEYS/M05–M06. Перенос root на другой ПК не обещает перенос авторизации. Отдельный live local root — не облачный sync folder: в облако идут только завершённые проверенные snapshots после M05/M06. Выбор «облака» нельзя реализовать переездом активной SQLite в общую папку.

## Ошибки, совместный доступ и миграция

- Сначала canonical path и bounded validation; отказ для корня диска, app/version directory, файла вместо каталога, собственного DB/sidecar как каталога, unsupported reparse/network target. V1 root — локальный каталог; UNC/network FS и автоматический live-root sync не объявлять поддержанными. Детальная race-resistant проверка M01.06; проверки пути не OS sandbox.
- Не трактовать запись пустого probe как доказательство достаточного места/полной долговечности/защищённых ACL. Отказ записи/rename, read-only volume, исчезновение root и нехватка места получают честную ошибку. Root identity — только дополнительная проверка против ошибочного назначения, не криптографическая аутентификация.
- Не менять root у уже запущенного процесса. Конкурирующие initializers/bootstrap writers должны иметь одного победителя и перечитывать результат; не затирать другой выбор. Переключение root/миграция блокируется при активных собственных операциях; внешние SQLite writers учитываются snapshot API. Поведение двух Desktop-процессов требует acceptance и не является обещанием многопользовательского сервера.
- M01.05 показывает источник/назначение, получает подтверждение конкретного переноса, копирует в новую цель через согласованный snapshot, проверяет schema/version/raw values/hashes, затем публикует. Оригинал остаётся. Переносимые preferences/history проверяются отдельно; секреты не переносятся автоматически. Отмена/сбой не переключает bootstrap на неполную копию.
- После переключения корректный root доступен новой версии; downgrade, не понимающий marker/DB format, получает отказ, а не новый root. M01.04 не должен обходить ограничения M01.02 или делать автоконверсию ordinary SQLite.

## Приёмка следующей реализации

M01.04: unit table для env/portable/bootstrap/new/legacy/error precedence; filesystem cases на D: для identity/no-overwrite/corrupt selection/readonly/unavailable; EXE и launcher разрешают один root без копии алгоритма; прежние mode/theme/history/backups видны после смены версии. Невозможный root не создаёт ни default DB, ни новый fallback. Tests должны проверять реальные побочные эффекты/неизменность данных, не только строки путей.

M01.05: два legacy-кандидата; активный WAL; исходник не меняется; cancel/обрыв/нехватка места/занятая цель; verified copy до переключения и повторный запуск. M01.06/M02/M11: Unicode/длинный путь/reparse/подмена/конкурентность/ACL, читабельная startup ошибка, чистый обычный ПК без D:/SDK, установка/обновление/uninstall и обе темы/масштабирование. Не закрывать UI gate автотестами.

## Другие ОС и источники

Windows x64 остаётся единственной поставленной платформой. Linux/macOS — не новый implementation slice: при их реализации учитывать XDG разделение data/config/state/cache и macOS Application Support/Caches вместо hardcoded Windows paths. Не добавлять сейчас отдельные сервисы/пакеты.

Выбор product paths — решение SmartSQL; официальные источники подтверждают системные расположения, а не работоспособность нашего resolver:

- [Microsoft Known Folders / FOLDERID_LocalAppData](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid): системный per-user folder; не привязывать путь к имени пользователя/диску C:.
- [XDG Base Directory Specification](https://specifications.freedesktop.org/basedir/latest/): раздельные назначения data/config/state/cache; future Linux alignment.
- [Apple file system guidance](https://developer.apple.com/documentation/foundation/using-the-file-system-effectively): различие постоянных support files и пересоздаваемого cache; future macOS alignment.

Источники проверены 17 октября 2026. Они не означают adoption Windows App SDK/UWP: приложение остаётся Avalonia/.NET, нового dependency нет.
