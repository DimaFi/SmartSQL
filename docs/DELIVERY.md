# Поставка SmartSQL для Windows x64

Готовое приложение создаётся рядом с исходниками, по умолчанию в `D:\Code\SmartSQL-builds`. Исходники остаются в `D:\Code\SmartSQL`. Публикация содержит только приложение, runtime при self-contained, запускатель и контрольные суммы; локальные базы, профили, пароли, backups, результаты тестов, NuGet-кэш и модель не копируются.

## Публикация

Из корня исходников, PowerShell 7:

```powershell
./scripts/Publish.ps1 -Version 0.7.0-preview.1 -Online
```

По умолчанию создаётся **self-contained directory** для `win-x64`: установленный .NET на компьютере пользователя не требуется. Single-file, trimming, ReadyToRun, подпись и автообновления не включены. Скрипт использует `Initialize-Workspace.ps1`: все NuGet-кэши и TEMP остаются в `.workspace` на D:, промежуточная сборка изолирована в новой папке поставки `.build`; обычные Debug/Release-каталоги исходников не перезаписываются. Для сборки нужен установленный .NET 10 SDK и доступные зависимости/runtime pack. Для разрешённой загрузки недостающих пакетов используйте `-Online`: он передаёт `https://api.nuget.org/v3/index.json` только текущему вызову restore, сохраняя workspace-кэш на D:. Пользовательские и глобальные NuGet sources не меняются; ничего не устанавливается в систему. Без `-Online`/`-Offline` используются эффективные настройки источников, которые могут не содержать ни одного feed. Режим источников отражён в manifest (`restoreMode`).

Результат: `<OutputRoot>\<Version>\win-x64-self-contained-<UTC>-<commit>\`:

- `app\SmartSQL.Desktop.exe` — запускаемое приложение и соседние зависимости;
- `app\Run-SmartSQL.ps1` — изолированный запуск с явным каталогом данных;
- `app\manifest.json` — версия, commit, dirty-флаг, SDK, режим поставки, платформа и SHA-256 каждого файла payload;
- `app\SHA256SUMS.txt` — те же SHA-256; сам manifest и файл сумм исключены, чтобы избежать циклического хэширования;
- `SmartSQL-<Version>-win-x64-self-contained.zip` и `.zip.sha256` — переносимый архив с содержимым `app` в корне;
- `publish.log`, `.build` — диагностика и промежуточные результаты, в ZIP не входят.

Каждый запуск создаёт отдельную папку. `-BuildId reviewed-1` задаёт фиксированное имя, но существующая папка всегда отклоняется. Ничего не удаляется; неудачная сборка сохраняется для диагностики. Сборка по процедуре воспроизводима при одинаковых исходниках/SDK/зависимостях; ZIP и manifest содержат время публикации, поэтому побайтовая идентичность архивов не заявляется. Dirty-сборка явно отмечена и не равнозначна сборке сохранённого Git-коммита.

Если нужен осознанный вариант без runtime:

```powershell
./scripts/Publish.ps1 -Version 0.7.0-preview.1 -Deployment FrameworkDependent
```

Этот вариант требует установленный **.NET 10 x64 Runtime** на целевом компьютере; Avalonia не требует Windows Desktop Runtime. Автоматического переключения с self-contained на framework-dependent нет. `-Online` и `-Offline` взаимно исключаются; их совместный выбор отклоняется до создания папок. `-Offline` разрешает restore только из подготовленного workspace NuGet-кэша и отключает сетевой NuGet audit для этого вызова; отсутствие пакетов завершит сборку ошибкой. Приготовленный SDK/runtime на машине разработчика сам по себе не заменяет NuGet runtime pack для self-contained публикации. Первая попытка self-contained публикации в текущем окружении завершилась `NU1100` из-за отсутствующих runtime packs 10.0.12 и недоступного настроенного источника. Неудачная папка сохраняется; для повторной попытки с явно разрешённой сетью выберите новый BuildId:

```powershell
./scripts/Publish.ps1 -Version 0.7.0-preview.1 -BuildId acceptance-online-1 -Online
```

Указание `-Online` не меняет режим поставки: он по-прежнему self-contained, без автоматического fallback.

## Запуск и данные

Для проверки новой поставки:

```powershell
./scripts/Run-Published.ps1 -BuildDirectory 'D:\Code\SmartSQL-builds\0.7.0-preview.1\win-x64-self-contained-<build-id>' -Wait
```

Без `-DataDirectory` запускатель использует постоянный `<build>\run-data\local` рядом с `app` (для ZIP, распакованного без подпапки `app`, — `run-data\local` внутри собственной папки portable): повторные запуски этой сборки сохраняют базы и выбор режима. Для свежей изолированной приёмки задайте новый отдельный каталог на D: через `-DataDirectory`; предыдущие данные не удаляются:

```powershell
./scripts/Run-Published.ps1 -BuildDirectory 'D:\Code\SmartSQL-builds\0.7.0-preview.1\win-x64-self-contained-<build-id>' -DataDirectory 'D:\Code\SmartSQL-builds\acceptance\0.7.0-preview.1' -Wait
```

После распаковки ZIP аналогичный запускатель находится внутри:

```powershell
& 'D:\Apps\SmartSQL-portable\Run-SmartSQL.ps1' -DataDirectory 'D:\SmartSQL-data\personal'
```

Переменные `SMARTSQL_DATA_DIRECTORY`, `SMARTSQL_TEMP_DIRECTORY`, `TEMP/TMP/TMPDIR` и каталоги runtime/cache задаются только для процесса и его дочернего приложения; после запуска настройки вызывающего процесса восстанавливаются. Глобальные настройки пользователя не меняются. Скрипт не открывает исходную пользовательскую базу и не удаляет данные при выходе.

Прямой запуск portable `SmartSQL.Desktop.exe` также поддержан: существующий `WorkspacePaths` без переменных использует `data\local` рядом с executable, временные файлы — под ним. Поэтому portable следует распаковывать в отдельную папку **на D:**, вне дерева исходников и вне каталога с родительским `SmartSQL.sln`; иначе текущая политика поиска workspace может выбрать данные исходного проекта. `Publish.ps1` отклоняет OutputRoot внутри исходников. Все три скрипта поставки проверяют существующие компоненты путей на ReparsePoint: junction/symlink отклоняются, ошибки чтения атрибутов также останавливают действие. Проверки выполняются до создания каталогов. Они предназначены для локального процесса сборки, а не как защита от конкурентной подмены каталогов другим процессом. Каталог `data` не является частью поставки и появляется только при запуске. Для максимально явного разделения используйте запускатель и `-DataDirectory`.

## Основа установщика

В `packaging/windows/SmartSQL.iss` подготовлен Inno Setup 6 recipe: Windows x64, per-user, `PrivilegesRequired=lowest`, постоянный AppId, ярлыки, необязательный ярлык рабочего стола, иконка `SmartSQL.ico`. По умолчанию приложение устанавливается в `D:\SmartSQL-app\<username>`; выбор другого диска блокируется, поскольку прямой запуск хранит данные рядом с приложением. Общая `ValidateInstallDirectory` проверяет также предков на `SmartSQL.sln` и reparse points, отклоняя установку внутрь исходников или через junction; проверка действует и в silent mode. Реализация использует официальные [Inno ExpandFileName](https://jrsoftware.org/ishelp/topic_isxfunc_expandfilename.htm), [DLLGetLastError](https://jrsoftware.org/ishelp/topic_isxfunc_dllgetlasterror.htm) и [Win32 GetFileAttributesW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileattributesw). Без ISCC эта Pascal-часть остаётся непроверенным компиляцией scaffold. Обновление использует прежний каталог установки. Базы, профили, backups и временные файлы приложение создаёт в `data\local`, установщик их не поставляет и не удаляет; `[UninstallDelete]` отсутствует. При удалении приложения пользовательские файлы остаются в прежней папке. Перенос сохранённых данных между отдельно распакованными portable-версиями выполняется пользователем явно, автоматически базы не перемещаются.

Компиляция **только уже установленным** ISCC:

```powershell
./packaging/windows/Build-Installer.ps1 -BuildDirectory 'D:\Code\SmartSQL-builds\0.7.0-preview.1\win-x64-self-contained-<build-id>'
# либо явный путь к существующему компилятору:
./packaging/windows/Build-Installer.ps1 -BuildDirectory 'D:\Code\SmartSQL-builds\0.7.0-preview.1\win-x64-self-contained-<build-id>' -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
# публикация и попытка компиляции за один вызов:
./scripts/Publish.ps1 -Version 0.7.0-preview.1 -Online -Installer
```

Для prerelease-версии полный `AppVersion` сохраняется (`0.7.0-preview.1`), а `VersionInfoVersion` передаётся отдельно числовым (`0.7.0`); формат manifest-версии и диапазон компонентов 0–65535 проверяются до вызова ISCC. Перед компиляцией проверяются SHA-256 файлов manifest и наличие опубликованной иконки, self-contained режим обязателен. Выход — новая папка `installer`, executable и SHA-256. Повторная компиляция в существующий каталог отклоняется. Компилятор не скачивается и не устанавливается. Если его нет, скрипт явно сообщает, что installer **не создан**, а уже созданные directory/ZIP сохраняются.

Read-only проверка окружения 2026-10-15: SDK 10.0.401, установленный runtime/host 10.0.12; `Microsoft.NETCore.App.Runtime.win-x64` отсутствовал в workspace NuGet-кэше. ISCC/Inno Setup в PATH и стандартных папках, WiX и NSIS в PATH не найдены. Это описание основы поставки, а не подтверждение созданного/протестированного installer. Проверенная основным агентом preview-публикация `0.7.0-preview.1`, сборка `b`, уже создана с `-Online`: 237 файлов payload, загрузка собственного `coreclr.dll` из папки приложения подтверждена. В ограниченной UI-проверке фильтр SQLite `id > 200` показал строки 201–205, переключение Advanced сохранило состояние, сброс вернул 100 строк. Это проверка preview-приложения, а не installer; финальная чистая публикация и её SHA фиксируются отдельно. Runtime packs после успешного restore находятся в workspace-кэше, поэтому следующая публикация может явно использовать `-Offline`. Неподписанные пакеты не имеют подписи издателя; подпись и релизный CI этим срезом не добавляются.

Чистая T10-поставка `0.7.0-preview.2/win-x64-self-contained-verified-t10-a676505` создана с -Offline из удалённо проверенного `a676505bbe8ba0ccd6553e2a1588fa306af3740e`: sourceDirty=false, 237 payload-файлов. Все SHA и ZIP проверены; ZIP SHA256 `1478033068c9c1600220c3cabd6e1ec7418a8ce865526f55c63b114dc90387a2`. Windows FileDescription/ProductName — SmartSQL. Эта проверка не означает приёмку installer.

Чистая T11 self-contained поставка `0.7.0-preview.3/win-x64-self-contained-verified-t11-3d72f15` создана из удалённо проверенного `3d72f15ffbfdba6307f582d55e2330742cf2c1dc`: sourceDirty=false, все 237 файлов payload проверены. ZIP 78 712 164 байт; SHA256 `93803ef2e3f62b6afbc043aa960ed77f643ecc3fa2e681a613ae8182685504a8`. Эта release-папка проверена сборкой/manifest/SHA; экранная SQL-приёмка выполнялась в Debug, не объявляется полной приёмкой нового EXE или installer.

Чистая автономная сборка тем/подсветки `0.7.0-preview.4/win-x64-self-contained-verified-ui-8867f5c` создана 16 октября из опубликованного и удалённо проверенного `8867f5c553a3073712456ad3055a09793246d367`: sourceDirty=false, self-contained Windows x64. Все 237 файлов payload сверены с manifest; ZIP 78 723 033 байта, SHA256 `29565c7fc8d35662da77419ac184463905f1a50c412e37b5d6eaf1d60dd6c188`. EXE FileDescription/ProductName — SmartSQL. Каталог `app` не содержит runtime-данных; новая ZIP не включает настройки/базы. Экранная приёмка этого среза выполнялась в Debug; новый Release EXE и installer не объявляются полностью принятыми. Тестовый PostgreSQL-кластер штатно остановлен.

Чистая автономная T11-поставка `0.7.0-preview.5/win-x64-self-contained-verified-t11-lifecycle-30c6829` создана 16 октября из удалённо проверенного `30c682944f076d5d724c4630fb76ac3a7eadab2a`: sourceDirty=false, self-contained Windows x64, все 237 SHA файлов payload подтверждены. ZIP 78724845 байт; SHA256 `7a9e02ac981f1fbd03fb32bc7c44661b40568f66292463e747eee9888bd22be3`. app не содержит runtime-данных; FileDescription/ProductName — SmartSQL. Новый Release EXE не запускался на экране из-за сбоя инструмента; installer не собран, ISCC отсутствует. Результат проверки кода — 593 теста (1/118/435/39), 55 PG-фактов без пропусков. Тестовый PostgreSQL-кластер штатно остановлен.

Чистая автономная T12-поставка `0.7.0-preview.6/win-x64-self-contained-verified-t12-28a4bcb` создана 16 октября из удалённо проверенного `28a4bcbbaa97e7ac0eab6c4239aca51078a77ff9`: sourceDirty=false, self-contained Windows x64, все 237 payload SHA/размеров и состав ZIP проверены. ZIP 78753789 байт; SHA256 `236a17e2c089f8c0f2664f3058cfdfc56a6cb1735b569fe79c2fb81aab1578f3`. Runtime-данных нет, FileDescription/ProductName — SmartSQL. Новый EXE не запускался на экране: повторная инициализация computer-use снова завершилась kernel assets/path/os error3. Проверка кода — 614 тестов (1/129/445/39), 60 PG-фактов без пропусков; собственный кластер остановлен. Installer не собран; ручная T11/T12/T04/T23 приёмка открыта.

M01.01 preview.7: чистая self-contained Windows x64 поставка `0.7.0-preview.7/win-x64-self-contained-verified-m0101-74fe89e` из опубликованного `74fe89e7cb1d71234926d870443ca561d9f7ccbd` (sourceDirty=false). Все 237 payload SHA/размеров и состав ZIP проверены: 78 756 941 байт, SHA256 `f30ed3a4c01e44080b55c189b109eb386700077ad6960f831ad80958c2c65de4`. Runtime-данных/модели нет. Debug 638 tests (1/129/469/39), 60 real PG facts, 0 warnings/errors/skips; owned PG stopped. Новый EXE на экране не запускался: computer-use initialization по-прежнему kernel assets/path/os error3. Installer не собран, M01.01/T04/T11/T12/T23 ручная приёмка открыта. Следующий кодовый срез M01.02.

M01.02 preview.8: чистая self-contained Windows x64 поставка `D:\Code\SmartSQL-builds\0.7.0-preview.8\win-x64-self-contained-verified-m0102-1ac1480\app\SmartSQL.Desktop.exe` из опубликованного/remote-SHA verified `1ac1480cf08a17d937f6d07b1abf92062b5e682d` (sourceDirty=false). Все 237 payload SHA/размеров, 239 ZIP entries и их байты проверены: ZIP 78 766 276 байт, SHA256 `794acc1f6a0bf64afc01df9b46a80de77b5ff2059ce3d0261b540879ec1ae071`. Runtime/model artifacts отсутствуют. Новый EXE на экране не запускался из-за computer-use initialization failure; installer не собран. Debug 676 tests/60 real PG facts/0 skips/warnings/errors; owned PG stopped. Следующий кодовый срез M01.03, manual acceptance открыт.
